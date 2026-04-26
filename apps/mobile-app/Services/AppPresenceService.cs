using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Helpers;

namespace VinhKhanh.MobileApp.Services;

public sealed class AppPresenceService(
    IMobileApiBaseUrlService apiBaseUrlService,
    ILogger<AppPresenceService> logger) : IAppLifecycleAwareService
{
    private const string AnonymousClientIdKey = "vinh-khanh-mobile:anonymous-client-id";
    private const string HeartbeatEndpoint = "api/app-presence/heartbeat";
    private const string OfflineEndpoint = "api/app-presence/offline";
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan PresenceRequestTimeout = TimeSpan.FromSeconds(8);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private HttpClient? _httpClient;
    private string? _resolvedBaseUrl;
    private CancellationTokenSource? _heartbeatCancellation;
    private Task? _heartbeatTask;

    public async Task HandleAppResumedAsync(CancellationToken cancellationToken = default)
        => await StartHeartbeatAsync(cancellationToken);

    public async Task HandleAppStoppedAsync(CancellationToken cancellationToken = default)
        => await StopHeartbeatAsync(sendOffline: true, cancellationToken);

    private async Task StartHeartbeatAsync(CancellationToken cancellationToken)
    {
        var shouldStartLoop = false;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_heartbeatCancellation is null || _heartbeatCancellation.IsCancellationRequested)
            {
                _heartbeatCancellation = new CancellationTokenSource();
                shouldStartLoop = true;
                _heartbeatTask = Task.Run(
                    () => RunHeartbeatLoopAsync(_heartbeatCancellation.Token),
                    CancellationToken.None);
            }
        }
        finally
        {
            _stateLock.Release();
        }

        await SendPresenceAsync(HeartbeatEndpoint, "heartbeat", cancellationToken);

        if (shouldStartLoop)
        {
            logger.LogInformation("[Presence] Mobile heartbeat loop started. intervalSeconds={IntervalSeconds}", HeartbeatInterval.TotalSeconds);
        }
    }

    private async Task StopHeartbeatAsync(bool sendOffline, CancellationToken cancellationToken)
    {
        Task? heartbeatTask;
        CancellationTokenSource? heartbeatCancellation;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            heartbeatTask = _heartbeatTask;
            heartbeatCancellation = _heartbeatCancellation;
            _heartbeatTask = null;
            _heartbeatCancellation = null;
            heartbeatCancellation?.Cancel();
        }
        finally
        {
            _stateLock.Release();
        }

        if (heartbeatTask is not null)
        {
            try
            {
                await heartbeatTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The loop is expected to stop through cancellation.
            }
            catch (TimeoutException)
            {
                logger.LogDebug("[Presence] Heartbeat loop did not stop before timeout.");
            }
        }

        heartbeatCancellation?.Dispose();

        if (!sendOffline)
        {
            return;
        }

        using var offlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        offlineCancellation.CancelAfter(PresenceRequestTimeout);
        await SendPresenceAsync(OfflineEndpoint, "offline", offlineCancellation.Token);
        logger.LogInformation("[Presence] Mobile heartbeat loop stopped and offline event was attempted.");
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(HeartbeatInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await SendPresenceAsync(HeartbeatEndpoint, "heartbeat", cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal lifecycle transition.
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "[Presence] Heartbeat loop stopped unexpectedly.");
        }
    }

    private async Task SendPresenceAsync(
        string endpoint,
        string action,
        CancellationToken cancellationToken)
    {
        if (Connectivity.Current.NetworkAccess is NetworkAccess.None or NetworkAccess.Unknown)
        {
            logger.LogDebug("[Presence] Skipping {Action}; network is unavailable.", action);
            return;
        }

        try
        {
            var client = await GetClientAsync(cancellationToken);
            if (client is null)
            {
                return;
            }

            using var response = await client.PostAsJsonAsync(
                endpoint,
                new AppPresenceRequest(
                    GetAnonymousClientId(),
                    ResolvePlatformCode(),
                    ResolveAppVersion()),
                JsonOptions,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogDebug("[Presence] {Action} sent successfully.", action);
                return;
            }

            logger.LogDebug(
                "[Presence] {Action} returned status {StatusCode}.",
                action,
                (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "[Presence] Unable to send {Action}.", action);
        }
    }

    private async Task<HttpClient?> GetClientAsync(CancellationToken cancellationToken)
    {
        var configuredBaseUrl = await apiBaseUrlService.GetBaseUrlAsync(cancellationToken);
        var nextBaseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? string.Empty
            : MobileApiEndpointHelper.EnsureTrailingSlash(configuredBaseUrl);
        if (string.IsNullOrWhiteSpace(nextBaseUrl))
        {
            logger.LogDebug("[Presence] No mobile API base URL configured.");
            return null;
        }

        if (_httpClient is not null &&
            string.Equals(_resolvedBaseUrl, nextBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return _httpClient;
        }

        _httpClient?.Dispose();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(nextBaseUrl, UriKind.Absolute),
            Timeout = PresenceRequestTimeout
        };
        _resolvedBaseUrl = nextBaseUrl;
        return _httpClient;
    }

    private static string GetAnonymousClientId()
    {
        var existing = Preferences.Default.Get(AnonymousClientIdKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var clientId = $"mobile-{Guid.NewGuid():N}";
        Preferences.Default.Set(AnonymousClientIdKey, clientId);
        return clientId;
    }

    private static string ResolvePlatformCode()
    {
        var platform = DeviceInfo.Current.Platform.ToString();
        return string.IsNullOrWhiteSpace(platform)
            ? "android"
            : platform.Trim().ToLowerInvariant();
    }

    private static string ResolveAppVersion()
    {
        try
        {
            return AppInfo.Current.VersionString ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed record AppPresenceRequest(
        string ClientId,
        string Platform,
        string AppVersion);
}
