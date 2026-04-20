using System.Net;
using System.Text;
using System.Text.Json;
using VinhKhanh.BackendApi.Contracts;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class TranslationProxyService(HttpClient httpClient, ILogger<TranslationProxyService> logger) : ITextTranslationClient
{
    public async Task<TextTranslationResponse> TranslateAsync(
        TextTranslationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TargetLanguageCode))
        {
            throw new InvalidOperationException("TargetLanguageCode is required.");
        }

        if (request.Texts is null || request.Texts.Count == 0)
        {
            throw new InvalidOperationException("At least one text segment is required for translation.");
        }

        var targetLanguageCode = LanguageRegistry.GetTranslationCode(request.TargetLanguageCode);
        var sourceLanguageCode = string.IsNullOrWhiteSpace(request.SourceLanguageCode)
            ? "auto"
            : LanguageRegistry.GetTranslationCode(request.SourceLanguageCode);
        var translatedTexts = new string[request.Texts.Count];

        for (var index = 0; index < request.Texts.Count; index += 1)
        {
            var text = request.Texts[index]?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                translatedTexts[index] = string.Empty;
                continue;
            }

            if (!string.Equals(sourceLanguageCode, "auto", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(sourceLanguageCode, targetLanguageCode, StringComparison.OrdinalIgnoreCase))
            {
                translatedTexts[index] = text;
                continue;
            }

            translatedTexts[index] = await TranslateSingleAsync(
                text,
                sourceLanguageCode,
                targetLanguageCode,
                cancellationToken);
        }

        return new TextTranslationResponse(
            targetLanguageCode,
            string.Equals(sourceLanguageCode, "auto", StringComparison.OrdinalIgnoreCase)
                ? null
                : sourceLanguageCode,
            translatedTexts,
            "google-translate-proxy");
    }

    private async Task<string> TranslateSingleAsync(
        string text,
        string sourceLanguageCode,
        string targetLanguageCode,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["client"] = "gtx",
            ["dt"] = "t",
            ["sl"] = sourceLanguageCode,
            ["tl"] = targetLanguageCode,
            ["q"] = text,
        };

        var requestUri =
            "https://translate.googleapis.com/translate_a/single?" +
            string.Join(
                "&",
                query.Select(item => $"{item.Key}={Uri.EscapeDataString(item.Value ?? string.Empty)}"));

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Translation proxy request failed with status {StatusCode} for target language {TargetLanguageCode}.",
                response.StatusCode,
                targetLanguageCode);
            throw new InvalidOperationException("Unable to translate the content to the selected language.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var translatedText = NarrationTextSanitizer.Clean(ReadTranslatedText(document.RootElement));
        if (!string.IsNullOrWhiteSpace(translatedText))
        {
            return translatedText;
        }

        logger.LogWarning(
            "Translation proxy returned an empty translation for target language {TargetLanguageCode}.",
            targetLanguageCode);
        throw new InvalidOperationException("Unable to translate the content to the selected language.");
    }

    private static string ReadTranslatedText(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var segments = root[0];
        if (segments.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var segment in segments.EnumerateArray())
        {
            if (segment.ValueKind != JsonValueKind.Array || segment.GetArrayLength() == 0)
            {
                continue;
            }

            var translatedPart = segment[0].ValueKind == JsonValueKind.String
                ? segment[0].GetString()
                : null;
            if (!string.IsNullOrWhiteSpace(translatedPart))
            {
                builder.Append(translatedPart);
            }
        }

        return WebUtility.HtmlDecode(builder.ToString().Trim());
    }

}
