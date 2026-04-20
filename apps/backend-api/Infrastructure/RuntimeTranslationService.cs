using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using VinhKhanh.BackendApi.Contracts;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed record RuntimeTranslationField(
    string EntityType,
    string EntityId,
    string FieldName,
    string SourceText,
    string SourceLanguageCode);

public sealed record RuntimeTranslationResult(
    string EntityType,
    string EntityId,
    string FieldName,
    string SourceText,
    string Text,
    string SourceLanguageCode,
    string TargetLanguageCode,
    bool WasTranslated,
    bool UsedFallback);

public sealed class RuntimeTranslationService(
    ITextTranslationClient translationClient,
    IMemoryCache memoryCache,
    ILogger<RuntimeTranslationService> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, Lazy<Task<TextTranslationResponse>>> _inflightBatches = new(StringComparer.Ordinal);

    public async Task<RuntimeTranslationResult> TranslateTextAsync(
        string entityType,
        string entityId,
        string fieldName,
        string? sourceText,
        string? sourceLanguageCode,
        string? targetLanguageCode,
        CancellationToken cancellationToken)
    {
        var results = await TranslateFieldsAsync(
            [
                new RuntimeTranslationField(
                    entityType,
                    entityId,
                    fieldName,
                    sourceText ?? string.Empty,
                    sourceLanguageCode ?? "vi")
            ],
            targetLanguageCode,
            cancellationToken);

        return results[0];
    }

    public async Task<IReadOnlyList<RuntimeTranslationResult>> TranslateFieldsAsync(
        IEnumerable<RuntimeTranslationField> fields,
        string? targetLanguageCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var target = NormalizeLanguageCode(targetLanguageCode);
        var indexedFields = fields
            .Select((field, index) => new IndexedField(index, NormalizeField(field), target))
            .ToList();
        if (indexedFields.Count == 0)
        {
            return [];
        }

        var output = new RuntimeTranslationResult[indexedFields.Count];
        var pending = new List<IndexedField>();
        var cacheHitCount = 0;

        foreach (var item in indexedFields)
        {
            if (string.IsNullOrWhiteSpace(item.Field.SourceText) ||
                string.Equals(item.Field.SourceLanguageCode, target, StringComparison.OrdinalIgnoreCase))
            {
                output[item.Index] = CreateResult(
                    item.Field,
                    target,
                    item.Field.SourceText,
                    wasTranslated: false,
                    usedFallback: false);
                continue;
            }

            var cacheKey = CreateCacheKey(item.Field, target);
            if (memoryCache.TryGetValue(cacheKey, out string? cachedText) &&
                !string.IsNullOrWhiteSpace(cachedText))
            {
                cacheHitCount += 1;
                output[item.Index] = CreateResult(
                    item.Field,
                    target,
                    cachedText,
                    wasTranslated: true,
                    usedFallback: false);
                continue;
            }

            pending.Add(item);
        }

        foreach (var group in pending.GroupBy(item => item.Field.SourceLanguageCode, StringComparer.OrdinalIgnoreCase))
        {
            var uniqueRequests = new List<PendingTranslation>();
            var requestByKey = new Dictionary<string, PendingTranslation>(StringComparer.Ordinal);

            foreach (var item in group)
            {
                var cacheKey = CreateCacheKey(item.Field, target);
                if (!requestByKey.TryGetValue(cacheKey, out var request))
                {
                    request = new PendingTranslation(cacheKey, item.Field.SourceText, []);
                    requestByKey.Add(cacheKey, request);
                    uniqueRequests.Add(request);
                }

                request.Items.Add(item);
            }

            try
            {
                logger.LogInformation(
                    "Runtime translation request. targetLanguage={TargetLanguage}; sourceLanguage={SourceLanguage}; uniqueTexts={UniqueTextCount}; totalFields={FieldCount}",
                    target,
                    group.Key,
                    uniqueRequests.Count,
                    group.Count());

                var batchKey = CreateBatchKey(target, group.Key, uniqueRequests.Select(item => item.CacheKey));
                var startedNewBatch = false;
                var lazyBatch = _inflightBatches.GetOrAdd(
                    batchKey,
                    _ =>
                    {
                        startedNewBatch = true;
                        return new Lazy<Task<TextTranslationResponse>>(
                            () => translationClient.TranslateAsync(
                                new TextTranslationRequest(
                                    target,
                                    group.Key,
                                    uniqueRequests.Select(item => item.SourceText).ToList()),
                                CancellationToken.None),
                            LazyThreadSafetyMode.ExecutionAndPublication);
                    });
                if (!startedNewBatch)
                {
                    logger.LogDebug(
                        "[TranslationCache] Coalesced runtime translation request. targetLanguage={TargetLanguage}; sourceLanguage={SourceLanguage}; uniqueTexts={UniqueTextCount}",
                        target,
                        group.Key,
                        uniqueRequests.Count);
                }

                TextTranslationResponse translated;
                try
                {
                    translated = await lazyBatch.Value.WaitAsync(cancellationToken);
                }
                finally
                {
                    if (lazyBatch.IsValueCreated && lazyBatch.Value.IsCompleted)
                    {
                        _inflightBatches.TryRemove(batchKey, out _);
                    }
                }

                for (var index = 0; index < uniqueRequests.Count; index += 1)
                {
                    var request = uniqueRequests[index];
                    var translatedText = CleanTranslatedText(
                        translated.Texts.ElementAtOrDefault(index),
                        target);
                    var usedFallback = string.IsNullOrWhiteSpace(translatedText);
                    var finalText = usedFallback ? request.SourceText : translatedText;

                    if (!usedFallback)
                    {
                        memoryCache.Set(
                            request.CacheKey,
                            finalText,
                            new MemoryCacheEntryOptions
                            {
                                AbsoluteExpirationRelativeToNow = CacheTtl,
                                Size = Math.Max(1, finalText.Length)
                            });
                    }

                    foreach (var item in request.Items)
                    {
                        output[item.Index] = CreateResult(
                            item.Field,
                            target,
                            finalText,
                            wasTranslated: !usedFallback,
                            usedFallback);
                    }
                }
            }
            catch (Exception exception) when (
                !cancellationToken.IsCancellationRequested &&
                (exception is HttpRequestException ||
                 exception is InvalidOperationException ||
                 exception is TaskCanceledException))
            {
                logger.LogWarning(
                    exception,
                    "Runtime translation failed. targetLanguage={TargetLanguage}; sourceLanguage={SourceLanguage}; fieldCount={FieldCount}. Falling back to source text.",
                    target,
                    group.Key,
                    group.Count());

                foreach (var item in group)
                {
                    output[item.Index] = CreateResult(
                        item.Field,
                        target,
                        item.Field.SourceText,
                        wasTranslated: false,
                        usedFallback: true);
                }
            }
        }

        foreach (var item in indexedFields)
        {
            output[item.Index] ??= CreateResult(
                item.Field,
                target,
                item.Field.SourceText,
                wasTranslated: false,
                usedFallback: true);
        }

        var fallbackCount = output.Count(item => item.UsedFallback);
        logger.LogDebug(
            "Runtime translation completed. targetLanguage={TargetLanguage}; fieldCount={FieldCount}; cacheHits={CacheHits}; cacheMisses={CacheMisses}; fallbackCount={FallbackCount}",
            target,
            output.Length,
            cacheHitCount,
            pending.Count,
            fallbackCount);

        return output;
    }

    private static RuntimeTranslationField NormalizeField(RuntimeTranslationField field)
        => field with
        {
            EntityType = NormalizeSegment(field.EntityType, "entity"),
            EntityId = NormalizeSegment(field.EntityId, "unknown"),
            FieldName = NormalizeSegment(field.FieldName, "text"),
            SourceText = NarrationTextSanitizer.Clean(field.SourceText),
            SourceLanguageCode = NormalizeLanguageCode(field.SourceLanguageCode)
        };

    private static RuntimeTranslationResult CreateResult(
        RuntimeTranslationField field,
        string targetLanguageCode,
        string text,
        bool wasTranslated,
        bool usedFallback)
        => new(
            field.EntityType,
            field.EntityId,
            field.FieldName,
            field.SourceText,
            text,
            field.SourceLanguageCode,
            targetLanguageCode,
            wasTranslated,
            usedFallback);

    private static string CleanTranslatedText(string? value, string targetLanguageCode)
    {
        var cleaned = LocalizationContentPolicy.CleanForLanguage(value, targetLanguageCode);
        return string.IsNullOrWhiteSpace(cleaned) ? string.Empty : cleaned;
    }

    private static string CreateCacheKey(RuntimeTranslationField field, string targetLanguageCode)
        => string.Join(
            ":",
            "runtime-translation",
            field.EntityType.Trim().ToLowerInvariant(),
            field.EntityId.Trim().ToLowerInvariant(),
            field.FieldName.Trim().ToLowerInvariant(),
            field.SourceLanguageCode.Trim().ToLowerInvariant(),
            targetLanguageCode.Trim().ToLowerInvariant(),
            CreateSha256(field.SourceText));

    private static string CreateBatchKey(string targetLanguageCode, string sourceLanguageCode, IEnumerable<string> cacheKeys)
        => string.Join(
            "|",
            "runtime-translation-batch",
            sourceLanguageCode.Trim().ToLowerInvariant(),
            targetLanguageCode.Trim().ToLowerInvariant(),
            string.Join(",", cacheKeys));

    private static string CreateSha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeLanguageCode(string? languageCode)
    {
        var normalized = PremiumAccessCatalog.NormalizeLanguageCode(languageCode);
        return string.IsNullOrWhiteSpace(normalized) ? "vi" : normalized;
    }

    private static string NormalizeSegment(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private sealed record IndexedField(int Index, RuntimeTranslationField Field, string TargetLanguageCode);

    private sealed record PendingTranslation(string CacheKey, string SourceText, List<IndexedField> Items);
}
