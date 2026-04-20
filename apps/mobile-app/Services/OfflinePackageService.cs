using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public interface IOfflinePackageService
{
    OfflinePackageState State { get; }
    event EventHandler<OfflinePackageState>? StateChanged;

    Task<OfflinePackageState> RefreshStatusAsync(CancellationToken cancellationToken = default);
    Task<OfflinePackageState> DownloadOrUpdateAsync(CancellationToken cancellationToken = default);
    Task CancelAsync();
    Task<OfflinePackageState> DeleteAsync(CancellationToken cancellationToken = default);
    long? TryGetAvailableFreeSpaceBytes();
}

public sealed class OfflinePackageService : IOfflinePackageService
{
    private const string AppSettingsFileName = "appsettings.json";
    private const string BootstrapEndpoint = "api/v1/bootstrap";
    private const string SyncStateEndpoint = "api/v1/sync-state";

    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    private readonly IOfflineStorageService _storageService;
    private readonly IBundledOfflinePackageSeedService _bundledSeedService;
    private readonly IMobileApiBaseUrlService _apiBaseUrlService;
    private readonly IAppLanguageService _languageService;
    private readonly ILogger<OfflinePackageService> _logger;

    private HttpClient? _httpClient;
    private string? _resolvedBaseUrl;
    private MobileRuntimeAppSettings? _runtimeSettings;
    private CancellationTokenSource? _operationCancellationSource;
    private OfflinePackageState _state = OfflinePackageState.Empty;

    public OfflinePackageService(
        IOfflineStorageService storageService,
        IBundledOfflinePackageSeedService bundledSeedService,
        IMobileApiBaseUrlService apiBaseUrlService,
        IAppLanguageService languageService,
        ILogger<OfflinePackageService> logger)
    {
        _storageService = storageService;
        _bundledSeedService = bundledSeedService;
        _apiBaseUrlService = apiBaseUrlService;
        _languageService = languageService;
        _logger = logger;
    }

    public OfflinePackageState State => _state;

    public event EventHandler<OfflinePackageState>? StateChanged;

    public async Task<OfflinePackageState> RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_state.IsBusy)
        {
            return _state;
        }

        var installation = await _bundledSeedService.EnsureInstalledAsync(cancellationToken)
                           ?? await _storageService.LoadInstallationAsync(cancellationToken);
        var remoteSyncState = await TryFetchSyncStateAsync(cancellationToken);
        var nextState = BuildState(
            installation,
            remoteSyncState,
            installation is null ? OfflinePackageLifecycleStatus.NotInstalled : OfflinePackageLifecycleStatus.Ready,
            canReachServer: remoteSyncState is not null,
            errorMessage: string.Empty);

        PublishState(nextState);
        return nextState;
    }

    public async Task<OfflinePackageState> DownloadOrUpdateAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);

        string? stagingRoot = null;
        try
        {
            var existingInstallation = await _bundledSeedService.EnsureInstalledAsync(cancellationToken)
                                     ?? await _storageService.LoadInstallationAsync(cancellationToken);
            if (!HasAnyNetworkAccess())
            {
                var offlineState = BuildState(
                    existingInstallation,
                    remoteSyncState: null,
                    OfflinePackageLifecycleStatus.Error,
                    canReachServer: false,
                    errorMessage: "Can ket noi mang de tai goi du lieu.");
                PublishState(offlineState);
                return offlineState;
            }

            var operationCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ReplaceOperationCancellationSource(operationCancellationSource);
            var operationToken = operationCancellationSource.Token;

            PublishState(BuildState(
                existingInstallation,
                remoteSyncState: null,
                OfflinePackageLifecycleStatus.Preparing,
                canReachServer: true,
                errorMessage: string.Empty));

            var bootstrapEnvelopeJson = await DownloadBootstrapEnvelopeAsync(operationToken);
            var packageDraft = ParsePackageDraft(bootstrapEnvelopeJson);
            _logger.LogInformation(
                "Offline package draft prepared. version={Version}; files={FileCount}; audios={AudioCount}; images={ImageCount}",
                packageDraft.Metadata.Version,
                packageDraft.Manifest.Files.Count,
                packageDraft.Metadata.AudioCount,
                packageDraft.Metadata.ImageCount);
            stagingRoot = await _storageService.CreateStagingRootAsync(operationToken);

            PublishState(BuildState(
                existingInstallation,
                new RemoteSyncState(packageDraft.Metadata.Version, packageDraft.Metadata.GeneratedAtUtc, packageDraft.Metadata.ServerLastChangedAtUtc),
                OfflinePackageLifecycleStatus.Downloading,
                canReachServer: true,
                downloadedBytes: 0,
                totalBytes: 0,
                downloadedFileCount: 0,
                totalFileCount: packageDraft.Manifest.Files.Count,
                errorMessage: string.Empty));

            var downloadedBytes = 0L;
            var downloadedFileCount = 0;
            foreach (var file in packageDraft.Manifest.Files)
            {
                operationToken.ThrowIfCancellationRequested();

                var bytes = await DownloadFileBytesAsync(file.Key, operationToken);
                var targetPath = Path.Combine(stagingRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var targetDirectory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                var tempPath = $"{targetPath}.tmp";
                await File.WriteAllBytesAsync(tempPath, bytes, operationToken);
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                File.Move(tempPath, targetPath);
                _logger.LogDebug(
                    "Offline asset saved. kind={Kind}; entityType={EntityType}; entityId={EntityId}; languageCode={LanguageCode}; path={Path}",
                    file.Kind,
                    file.EntityType,
                    file.EntityId,
                    file.LanguageCode,
                    targetPath);

                file.SizeBytes = bytes.LongLength;
                downloadedBytes += bytes.LongLength;
                downloadedFileCount++;

                PublishState(BuildState(
                    existingInstallation,
                    new RemoteSyncState(packageDraft.Metadata.Version, packageDraft.Metadata.GeneratedAtUtc, packageDraft.Metadata.ServerLastChangedAtUtc),
                    OfflinePackageLifecycleStatus.Downloading,
                    canReachServer: true,
                    downloadedBytes: downloadedBytes,
                    totalBytes: 0,
                    downloadedFileCount: downloadedFileCount,
                    totalFileCount: packageDraft.Manifest.Files.Count,
                    errorMessage: string.Empty));
            }

            PublishState(BuildState(
                existingInstallation,
                new RemoteSyncState(packageDraft.Metadata.Version, packageDraft.Metadata.GeneratedAtUtc, packageDraft.Metadata.ServerLastChangedAtUtc),
                OfflinePackageLifecycleStatus.Validating,
                canReachServer: true,
                downloadedBytes: downloadedBytes,
                totalBytes: 0,
                downloadedFileCount: downloadedFileCount,
                totalFileCount: packageDraft.Manifest.Files.Count,
                errorMessage: string.Empty));

            packageDraft.Metadata.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
            packageDraft.Metadata.InstallationSource = OfflinePackageInstallationSources.Downloaded;
            packageDraft.Metadata.FileCount = packageDraft.Manifest.Files.Count;

            var bootstrapPath = _storageService.GetStagingBootstrapPath(stagingRoot);
            var manifestPath = _storageService.GetStagingManifestPath(stagingRoot);
            var metadataPath = _storageService.GetStagingMetadataPath(stagingRoot);

            await File.WriteAllTextAsync(bootstrapPath, bootstrapEnvelopeJson, operationToken);
            var manifestJson = JsonSerializer.Serialize(packageDraft.Manifest, _jsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, operationToken);

            packageDraft.Metadata.PackageSizeBytes =
                downloadedBytes +
                Encoding.UTF8.GetByteCount(bootstrapEnvelopeJson) +
                Encoding.UTF8.GetByteCount(manifestJson);

            var metadataJson = JsonSerializer.Serialize(packageDraft.Metadata, _jsonOptions);
            packageDraft.Metadata.PackageSizeBytes += Encoding.UTF8.GetByteCount(metadataJson);
            metadataJson = JsonSerializer.Serialize(packageDraft.Metadata, _jsonOptions);
            await File.WriteAllTextAsync(metadataPath, metadataJson, operationToken);

            PublishState(BuildState(
                existingInstallation,
                new RemoteSyncState(packageDraft.Metadata.Version, packageDraft.Metadata.GeneratedAtUtc, packageDraft.Metadata.ServerLastChangedAtUtc),
                OfflinePackageLifecycleStatus.Installing,
                canReachServer: true,
                downloadedBytes: packageDraft.Metadata.PackageSizeBytes,
                totalBytes: packageDraft.Metadata.PackageSizeBytes,
                downloadedFileCount: packageDraft.Manifest.Files.Count,
                totalFileCount: packageDraft.Manifest.Files.Count,
                errorMessage: string.Empty));

            await _storageService.ReplaceInstallationAsync(stagingRoot, operationToken);
            stagingRoot = null;

            var installed = await _storageService.LoadInstallationAsync(operationToken);
            var completedState = BuildState(
                installed,
                new RemoteSyncState(
                    installed?.Metadata.Version ?? packageDraft.Metadata.Version,
                    installed?.Metadata.GeneratedAtUtc ?? packageDraft.Metadata.GeneratedAtUtc,
                    installed?.Metadata.ServerLastChangedAtUtc ?? packageDraft.Metadata.ServerLastChangedAtUtc),
                OfflinePackageLifecycleStatus.Completed,
                canReachServer: true,
                downloadedBytes: installed?.Metadata.PackageSizeBytes ?? packageDraft.Metadata.PackageSizeBytes,
                totalBytes: installed?.Metadata.PackageSizeBytes ?? packageDraft.Metadata.PackageSizeBytes,
                downloadedFileCount: installed?.Metadata.FileCount ?? packageDraft.Metadata.FileCount,
                totalFileCount: installed?.Metadata.FileCount ?? packageDraft.Metadata.FileCount,
                errorMessage: string.Empty);

            _logger.LogInformation(
                "Offline package installed. version={Version}; files={FileCount}; sizeBytes={SizeBytes}",
                completedState.Metadata?.Version,
                completedState.Metadata?.FileCount,
                completedState.Metadata?.PackageSizeBytes);
            PublishState(completedState);
            return completedState;
        }
        catch (OperationCanceledException)
        {
            var existingInstallation = await SafeLoadInstallationAsync();
            var canceledState = BuildState(
                existingInstallation,
                remoteSyncState: null,
                OfflinePackageLifecycleStatus.Canceled,
                canReachServer: _state.CanReachServer,
                errorMessage: string.Empty);

            PublishState(canceledState);
            return canceledState;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Offline package download failed.");
            var existingInstallation = await SafeLoadInstallationAsync();
            var errorState = BuildState(
                existingInstallation,
                remoteSyncState: null,
                OfflinePackageLifecycleStatus.Error,
                canReachServer: _state.CanReachServer,
                errorMessage: "Không thể tải gói dữ liệu lúc này. Vui lòng thử lại.");

            PublishState(errorState);
            return errorState;
        }
        finally
        {
            ReplaceOperationCancellationSource(null);

            if (!string.IsNullOrWhiteSpace(stagingRoot) && Directory.Exists(stagingRoot))
            {
                try
                {
                    Directory.Delete(stagingRoot, recursive: true);
                }
                catch
                {
                    // Best effort cleanup only.
                }
            }

            _operationLock.Release();
        }
    }

    public Task CancelAsync()
    {
        _operationCancellationSource?.Cancel();
        return Task.CompletedTask;
    }

    public async Task<OfflinePackageState> DeleteAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var existingInstallation = await _storageService.LoadInstallationAsync(cancellationToken);
            PublishState(BuildState(
                existingInstallation,
                remoteSyncState: null,
                OfflinePackageLifecycleStatus.Deleting,
                canReachServer: _state.CanReachServer,
                errorMessage: string.Empty));

            await _storageService.DeleteInstallationAsync(cancellationToken);

            var deletedState = BuildState(
                installation: null,
                remoteSyncState: null,
                OfflinePackageLifecycleStatus.NotInstalled,
                canReachServer: _state.CanReachServer,
                errorMessage: string.Empty);

            PublishState(deletedState);
            return deletedState;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Offline package delete failed.");
            var existingInstallation = await SafeLoadInstallationAsync();
            var errorState = BuildState(
                existingInstallation,
                remoteSyncState: null,
                OfflinePackageLifecycleStatus.Error,
                canReachServer: _state.CanReachServer,
                errorMessage: "Không thể xóa gói dữ liệu ngoại tuyến.");

            PublishState(errorState);
            return errorState;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public long? TryGetAvailableFreeSpaceBytes()
        => _storageService.TryGetAvailableFreeSpaceBytes();

    private void ReplaceOperationCancellationSource(CancellationTokenSource? nextSource)
    {
        _operationCancellationSource?.Dispose();
        _operationCancellationSource = nextSource;
    }

    private async Task<string> DownloadBootstrapEnvelopeAsync(CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken)
                     ?? throw new InvalidOperationException("The mobile app has no API base URL configured.");
        var languageCode = AppLanguage.NormalizeCode(_languageService.CurrentLanguage);
        return await GetStringWithRetryAsync(client, BuildBootstrapEndpoint(languageCode), cancellationToken);
    }

    private async Task<byte[]> DownloadFileBytesAsync(string requestUri, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken)
                     ?? throw new InvalidOperationException("The mobile app has no API base URL configured.");

        Exception? lastException = null;
        foreach (var delayMilliseconds in new[] { 0, 450, 1200 })
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (delayMilliseconds > 0)
            {
                await Task.Delay(delayMilliseconds, cancellationToken);
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                lastException = exception;
                _logger.LogDebug(exception, "Retrying offline asset download for {RequestUri}.", requestUri);
            }
        }

        throw lastException ?? new InvalidOperationException("Offline asset download failed.");
    }

    private async Task<RemoteSyncState?> TryFetchSyncStateAsync(CancellationToken cancellationToken)
    {
        if (!HasAnyNetworkAccess())
        {
            return null;
        }

        try
        {
            var client = await GetClientAsync(cancellationToken);
            if (client is null)
            {
                return null;
            }

            using var response = await client.GetAsync(SyncStateEndpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(content);
            if (!TryReadDataElement(document.RootElement, out var dataElement))
            {
                return null;
            }

            return new RemoteSyncState(
                GetString(dataElement, "version"),
                GetDateTimeOffset(dataElement, "generatedAt"),
                GetNullableDateTimeOffset(dataElement, "lastChangedAt"));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogDebug(exception, "Unable to reach sync-state for offline package status.");
            return null;
        }
    }

    private static OfflinePackageState BuildState(
        OfflinePackageInstallation? installation,
        RemoteSyncState? remoteSyncState,
        OfflinePackageLifecycleStatus status,
        bool canReachServer,
        long downloadedBytes = 0,
        long totalBytes = 0,
        int downloadedFileCount = 0,
        int totalFileCount = 0,
        string errorMessage = "")
    {
        var metadata = installation?.Metadata;
        var installedVersion = metadata?.Version ?? string.Empty;
        var remoteVersion = remoteSyncState?.Version ?? string.Empty;

        return new OfflinePackageState
        {
            Status = status,
            IsInstalled = installation is not null,
            IsUpdateAvailable =
                installation is not null &&
                !string.IsNullOrWhiteSpace(remoteVersion) &&
                !string.Equals(installedVersion, remoteVersion, StringComparison.OrdinalIgnoreCase),
            CanReachServer = canReachServer,
            InstalledVersion = installedVersion,
            RemoteVersion = remoteVersion,
            DownloadedBytes = downloadedBytes,
            TotalBytes = totalBytes,
            DownloadedFileCount = downloadedFileCount,
            TotalFileCount = totalFileCount,
            ErrorMessage = errorMessage,
            Metadata = metadata
        };
    }

    private void PublishState(OfflinePackageState nextState)
    {
        _state = nextState;
        StateChanged?.Invoke(this, nextState);
    }

    private async Task<HttpClient?> GetClientAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nextBaseUrl = MobileApiEndpointHelper.EnsureTrailingSlash(await _apiBaseUrlService.GetBaseUrlAsync());
        if (string.IsNullOrWhiteSpace(nextBaseUrl))
        {
            return null;
        }

        if (_httpClient is not null &&
            string.Equals(_resolvedBaseUrl, nextBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return _httpClient;
        }

        _httpClient?.Dispose();
        _httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            BaseAddress = new Uri(nextBaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _resolvedBaseUrl = nextBaseUrl;
        return _httpClient;
    }

    private async Task<MobileRuntimeAppSettings> LoadRuntimeSettingsAsync(CancellationToken cancellationToken)
    {
        if (_runtimeSettings is not null)
        {
            return _runtimeSettings;
        }

        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(AppSettingsFileName);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync(cancellationToken);
            _runtimeSettings = JsonSerializer.Deserialize<MobileRuntimeAppSettings>(content, _jsonOptions)
                               ?? new MobileRuntimeAppSettings();
        }
        catch
        {
            _runtimeSettings = new MobileRuntimeAppSettings();
        }

        return _runtimeSettings;
    }

    private async Task<string> GetStringWithRetryAsync(HttpClient client, string relativeUrl, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        foreach (var delayMilliseconds in new[] { 0, 350, 1000 })
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (delayMilliseconds > 0)
            {
                await Task.Delay(delayMilliseconds, cancellationToken);
            }

            try
            {
                using var response = await client.GetAsync(relativeUrl, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                lastException = exception;
                _logger.LogDebug(exception, "Retrying offline bootstrap request {RelativeUrl}.", relativeUrl);
            }
        }

        throw lastException ?? new InvalidOperationException("Offline bootstrap request failed.");
    }

    private PackageDraft ParsePackageDraft(string bootstrapEnvelopeJson)
    {
        using var document = JsonDocument.Parse(bootstrapEnvelopeJson);
        if (!TryReadDataElement(document.RootElement, out var dataElement))
        {
            throw new InvalidOperationException("Offline bootstrap payload does not contain a valid data node.");
        }

        var fileEntries = new Dictionary<string, OfflinePackageFileEntry>(StringComparer.OrdinalIgnoreCase);

        if (dataElement.TryGetProperty("mediaAssets", out var mediaAssetsElement) &&
            mediaAssetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var assetElement in mediaAssetsElement.EnumerateArray())
            {
                var url = GetString(assetElement, "url");
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var type = GetString(assetElement, "type");
                if (!string.IsNullOrWhiteSpace(type) &&
                    !type.Contains("image", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddFileEntry(
                    fileEntries,
                    url,
                    kind: "image",
                    entityType: GetString(assetElement, "entityType"),
                    entityId: GetString(assetElement, "entityId"),
                    languageCode: string.Empty);
            }
        }

        if (dataElement.TryGetProperty("foodItems", out var foodItemsElement) &&
            foodItemsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var foodItemElement in foodItemsElement.EnumerateArray())
            {
                var url = GetString(foodItemElement, "imageUrl");
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                AddFileEntry(
                    fileEntries,
                    url,
                    kind: "image",
                    entityType: "food_item",
                    entityId: GetString(foodItemElement, "id"),
                    languageCode: string.Empty);
            }
        }

        if (dataElement.TryGetProperty("routes", out var routesElement) &&
            routesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var routeElement in routesElement.EnumerateArray())
            {
                var url = GetString(routeElement, "coverImageUrl");
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                AddFileEntry(
                    fileEntries,
                    url,
                    kind: "image",
                    entityType: "route",
                    entityId: GetString(routeElement, "id"),
                    languageCode: string.Empty);
            }
        }

        if (dataElement.TryGetProperty("audioGuides", out var audioGuidesElement) &&
            audioGuidesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var audioGuideElement in audioGuidesElement.EnumerateArray())
            {
                var url = GetString(audioGuideElement, "audioUrl");
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                if (!IsPlayableOfflineAudioGuide(audioGuideElement))
                {
                    continue;
                }

                AddFileEntry(
                    fileEntries,
                    url,
                    kind: "audio",
                    entityType: GetString(audioGuideElement, "entityType"),
                    entityId: GetString(audioGuideElement, "entityId"),
                    languageCode: GetString(audioGuideElement, "languageCode"));
            }
        }

        var metadata = new OfflinePackageMetadata
        {
            Version = GetNestedString(dataElement, "syncState", "version"),
            GeneratedAtUtc = GetNestedDateTimeOffset(dataElement, "syncState", "generatedAt"),
            ServerLastChangedAtUtc = GetNestedNullableDateTimeOffset(dataElement, "syncState", "lastChangedAt"),
            PoiCount = GetArrayCount(dataElement, "pois"),
            AudioCount = fileEntries.Values.Count(item => string.Equals(item.Kind, "audio", StringComparison.OrdinalIgnoreCase)),
            ImageCount = fileEntries.Values.Count(item => string.Equals(item.Kind, "image", StringComparison.OrdinalIgnoreCase)),
            TourCount = GetArrayCount(dataElement, "routes"),
            LanguageCount = GetArrayCount(dataElement, "settings", "supportedLanguages"),
            FileCount = fileEntries.Count
        };

        if (metadata.GeneratedAtUtc == default)
        {
            metadata.GeneratedAtUtc = DateTimeOffset.UtcNow;
        }

        var manifest = new OfflinePackageManifest
        {
            Version = metadata.Version,
            GeneratedAtUtc = metadata.GeneratedAtUtc,
            Files = fileEntries.Values
                .OrderBy(item => item.Kind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        return new PackageDraft(metadata, manifest);
    }

    private static void AddFileEntry(
        IDictionary<string, OfflinePackageFileEntry> fileEntries,
        string remoteUrl,
        string kind,
        string entityType,
        string entityId,
        string languageCode)
    {
        var normalizedUrl = remoteUrl.Trim();
        if (fileEntries.ContainsKey(normalizedUrl))
        {
            return;
        }

        var extension = ResolveFileExtension(normalizedUrl, kind);
        var fileName = $"{CreateHash(normalizedUrl)}{extension}";
        var relativePath = $"assets/{kind}/{fileName}";

        fileEntries[normalizedUrl] = new OfflinePackageFileEntry
        {
            Key = normalizedUrl,
            Kind = kind,
            RelativePath = relativePath,
            EntityType = entityType?.Trim() ?? string.Empty,
            EntityId = entityId?.Trim() ?? string.Empty,
            LanguageCode = AppLanguage.NormalizeCode(languageCode)
        };
    }

    private static string ResolveFileExtension(string remoteUrl, string kind)
    {
        try
        {
            var path = Uri.TryCreate(remoteUrl, UriKind.Absolute, out var absoluteUri)
                ? absoluteUri.AbsolutePath
                : remoteUrl;
            var extension = Path.GetExtension(path);
            if (!string.IsNullOrWhiteSpace(extension) && extension.Length <= 8)
            {
                return extension;
            }
        }
        catch
        {
            // Fall back to default extension below.
        }

        return string.Equals(kind, "audio", StringComparison.OrdinalIgnoreCase)
            ? ".mp3"
            : ".jpg";
    }

    private static string CreateHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool HasAnyNetworkAccess()
        => Connectivity.Current.NetworkAccess is not NetworkAccess.None;

    private static string BuildBootstrapEndpoint(string languageCode)
        => $"{BootstrapEndpoint}?languageCode={Uri.EscapeDataString(AppLanguage.NormalizeCode(languageCode))}";

    private async Task<OfflinePackageInstallation?> SafeLoadInstallationAsync()
    {
        try
        {
            return await _storageService.LoadInstallationAsync();
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadDataElement(JsonElement rootElement, out JsonElement dataElement)
    {
        if (rootElement.ValueKind == JsonValueKind.Object &&
            rootElement.TryGetProperty("data", out dataElement) &&
            dataElement.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        dataElement = default;
        return false;
    }

    private static string GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var propertyValue) &&
           propertyValue.ValueKind == JsonValueKind.String
            ? propertyValue.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static DateTimeOffset GetDateTimeOffset(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var propertyValue) &&
           propertyValue.ValueKind == JsonValueKind.String &&
           DateTimeOffset.TryParse(propertyValue.GetString(), out var parsed)
            ? parsed
            : default;

    private static DateTimeOffset? GetNullableDateTimeOffset(JsonElement element, string propertyName)
    {
        var parsed = GetDateTimeOffset(element, propertyName);
        return parsed == default ? null : parsed;
    }

    private static string GetNestedString(JsonElement element, string objectPropertyName, string valuePropertyName)
        => element.TryGetProperty(objectPropertyName, out var objectElement) &&
           objectElement.ValueKind == JsonValueKind.Object
            ? GetString(objectElement, valuePropertyName)
            : string.Empty;

    private static DateTimeOffset GetNestedDateTimeOffset(JsonElement element, string objectPropertyName, string valuePropertyName)
        => element.TryGetProperty(objectPropertyName, out var objectElement) &&
           objectElement.ValueKind == JsonValueKind.Object
            ? GetDateTimeOffset(objectElement, valuePropertyName)
            : default;

    private static DateTimeOffset? GetNestedNullableDateTimeOffset(JsonElement element, string objectPropertyName, string valuePropertyName)
        => element.TryGetProperty(objectPropertyName, out var objectElement) &&
           objectElement.ValueKind == JsonValueKind.Object
            ? GetNullableDateTimeOffset(objectElement, valuePropertyName)
            : null;

    private static bool IsPlayableOfflineAudioGuide(JsonElement audioGuideElement)
    {
        var audioUrl = GetString(audioGuideElement, "audioUrl");
        if (string.IsNullOrWhiteSpace(audioUrl) ||
            !string.Equals(GetString(audioGuideElement, "status"), "ready", StringComparison.OrdinalIgnoreCase) ||
            GetBoolean(audioGuideElement, "isOutdated"))
        {
            return false;
        }

        var sourceType = GetString(audioGuideElement, "sourceType");
        var generationStatus = NormalizeGenerationStatus(GetString(audioGuideElement, "generationStatus"));
        if (generationStatus is "failed" or "outdated" or "pending")
        {
            return false;
        }

        return !string.Equals(sourceType, "generated", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(generationStatus, "success", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeGenerationStatus(string? generationStatus)
        => string.IsNullOrWhiteSpace(generationStatus)
            ? "none"
            : generationStatus.Trim().ToLowerInvariant();

    private static bool GetBoolean(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var propertyValue) &&
           propertyValue.ValueKind is JsonValueKind.True or JsonValueKind.False &&
           propertyValue.GetBoolean();

    private static int GetArrayCount(JsonElement element, string arrayPropertyName)
        => element.TryGetProperty(arrayPropertyName, out var arrayElement) &&
           arrayElement.ValueKind == JsonValueKind.Array
            ? arrayElement.GetArrayLength()
            : 0;

    private static int GetArrayCount(JsonElement element, string objectPropertyName, string arrayPropertyName)
    {
        if (!element.TryGetProperty(objectPropertyName, out var objectElement) ||
            objectElement.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        return GetArrayCount(objectElement, arrayPropertyName);
    }

    private sealed record PackageDraft(OfflinePackageMetadata Metadata, OfflinePackageManifest Manifest);

    private sealed record RemoteSyncState(
        string Version,
        DateTimeOffset GeneratedAtUtc,
        DateTimeOffset? LastChangedAtUtc);

    private sealed class MobileRuntimeAppSettings
    {
        public string? ApiBaseUrl { get; set; }
        public Dictionary<string, string> PlatformApiBaseUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
