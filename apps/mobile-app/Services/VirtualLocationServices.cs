using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public interface ILocationService
{
    event EventHandler<UserLocationChangedEventArgs>? LocationChanged;

    UserLocationPoint? CurrentLocation { get; }
    bool HasLocation { get; }
    bool IsUsingMockLocation { get; }

    Task<bool> EnsurePermissionAsync();
    Task StartAsync(TimeSpan interval, CancellationToken cancellationToken = default);
    Task StopAsync();
    Task<UserLocationPoint?> GetCurrentLocationAsync(CancellationToken cancellationToken = default);
    Task SetMockLocationAsync(double latitude, double longitude);
    Task ClearMockLocationAsync();
}

public interface IPoiProximityService
{
    PoiProximitySnapshot Evaluate(UserLocationPoint location, IEnumerable<PoiLocation> pois, double activationRadiusMeters);
    double CalculateDistanceMeters(double latitude1, double longitude1, double latitude2, double longitude2);
}

public sealed class DeviceLocationService : ILocationService
{
    private readonly SemaphoreSlim _loopLock = new(1, 1);
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _pollingCancellationSource;
    private Task? _pollingTask;
    private UserLocationPoint? _currentLocation;
    private UserLocationPoint? _mockLocation;

    public event EventHandler<UserLocationChangedEventArgs>? LocationChanged;

    public UserLocationPoint? CurrentLocation
    {
        get
        {
            lock (_syncRoot)
            {
                return Clone(_currentLocation);
            }
        }
    }

    public bool HasLocation
    {
        get
        {
            lock (_syncRoot)
            {
                return _currentLocation is not null;
            }
        }
    }

    public bool IsUsingMockLocation
    {
        get
        {
            lock (_syncRoot)
            {
                return _mockLocation is not null;
            }
        }
    }

    public async Task<bool> EnsurePermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status == PermissionStatus.Granted)
        {
            return true;
        }

        status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        return status == PermissionStatus.Granted;
    }

    public async Task StartAsync(TimeSpan interval, CancellationToken cancellationToken = default)
    {
        var normalizedInterval = interval <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(4)
            : interval;

        await _loopLock.WaitAsync(cancellationToken);
        try
        {
            if (_pollingTask is { IsCompleted: false })
            {
                return;
            }

            _pollingCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pollingTask = RunPollingLoopAsync(normalizedInterval, _pollingCancellationSource.Token);
        }
        finally
        {
            _loopLock.Release();
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellationSource;
        Task? pollingTask;

        await _loopLock.WaitAsync();
        try
        {
            cancellationSource = _pollingCancellationSource;
            pollingTask = _pollingTask;
            _pollingCancellationSource = null;
            _pollingTask = null;
        }
        finally
        {
            _loopLock.Release();
        }

        cancellationSource?.Cancel();
        if (pollingTask is not null)
        {
            try
            {
                await pollingTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellationSource?.Dispose();
    }

    public async Task<UserLocationPoint?> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        var mockLocation = GetMockLocation();
        if (mockLocation is not null)
        {
            UpdateCurrentLocation(mockLocation);
            return mockLocation;
        }

        if (!await HasLocationPermissionAsync())
        {
            return null;
        }

        var currentLocation = await ResolveDeviceLocationAsync(cancellationToken);
        if (currentLocation is not null)
        {
            UpdateCurrentLocation(currentLocation);
        }

        return currentLocation;
    }

    public Task SetMockLocationAsync(double latitude, double longitude)
    {
        var nextLocation = new UserLocationPoint
        {
            Latitude = latitude,
            Longitude = longitude
        };

        lock (_syncRoot)
        {
            _mockLocation = nextLocation;
            _currentLocation = nextLocation;
        }

        PublishLocationChanged(nextLocation, isMock: true);
        return Task.CompletedTask;
    }

    public Task ClearMockLocationAsync()
    {
        lock (_syncRoot)
        {
            _mockLocation = null;
        }

        _ = PublishCurrentLocationAsync(allowPermissionRequest: false, CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunPollingLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            await PublishCurrentLocationAsync(allowPermissionRequest: true, cancellationToken);

            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await PublishCurrentLocationAsync(allowPermissionRequest: false, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task PublishCurrentLocationAsync(bool allowPermissionRequest, CancellationToken cancellationToken)
    {
        var mockLocation = GetMockLocation();
        if (mockLocation is not null)
        {
            UpdateCurrentLocation(mockLocation);
            PublishLocationChanged(mockLocation, isMock: true);
            return;
        }

        var hasPermission = allowPermissionRequest
            ? await EnsurePermissionAsync()
            : await HasLocationPermissionAsync();
        if (!hasPermission)
        {
            return;
        }

        var currentLocation = await ResolveDeviceLocationAsync(cancellationToken);
        if (currentLocation is null)
        {
            return;
        }

        UpdateCurrentLocation(currentLocation);
        PublishLocationChanged(currentLocation, isMock: false);
    }

    private async Task<bool> HasLocationPermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        return status == PermissionStatus.Granted;
    }

    private static async Task<UserLocationPoint?> ResolveDeviceLocationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(3));
            var location = await Geolocation.Default.GetLocationAsync(request, cancellationToken)
                ?? await Geolocation.Default.GetLastKnownLocationAsync();

            return location is null
                ? null
                : new UserLocationPoint
                {
                    Latitude = location.Latitude,
                    Longitude = location.Longitude
                };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FeatureNotSupportedException)
        {
            return null;
        }
        catch (FeatureNotEnabledException)
        {
            return null;
        }
        catch (PermissionException)
        {
            return null;
        }
    }

    private UserLocationPoint? GetMockLocation()
    {
        lock (_syncRoot)
        {
            return Clone(_mockLocation);
        }
    }

    private void UpdateCurrentLocation(UserLocationPoint location)
    {
        lock (_syncRoot)
        {
            _currentLocation = location;
        }
    }

    private void PublishLocationChanged(UserLocationPoint location, bool isMock)
    {
        LocationChanged?.Invoke(this, new UserLocationChangedEventArgs
        {
            Location = location,
            IsMock = isMock,
            CapturedAt = DateTimeOffset.UtcNow
        });
    }

    private static UserLocationPoint? Clone(UserLocationPoint? location)
        => location is null
            ? null
            : new UserLocationPoint
            {
                Latitude = location.Latitude,
                Longitude = location.Longitude
            };
}

public sealed class PoiProximityService : IPoiProximityService
{
    private const double EarthRadiusMeters = 6_371_000d;

    public PoiProximitySnapshot Evaluate(UserLocationPoint location, IEnumerable<PoiLocation> pois, double activationRadiusMeters)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(pois);

        var candidates = PoiOverlapSelectionHelper.BuildCandidates(
            location,
            pois,
            CalculateDistanceMeters,
            activationRadiusMeters);
        var activeCandidate = PoiOverlapSelectionHelper.SelectBestCandidate(candidates);
        return PoiOverlapSelectionHelper.BuildSnapshot(location, candidates, activeCandidate);
    }

    public double CalculateDistanceMeters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        var deltaLatitude = ToRadians(latitude2 - latitude1);
        var deltaLongitude = ToRadians(longitude2 - longitude1);
        var startLatitude = ToRadians(latitude1);
        var endLatitude = ToRadians(latitude2);

        var a =
            Math.Sin(deltaLatitude / 2) * Math.Sin(deltaLatitude / 2) +
            Math.Cos(startLatitude) *
            Math.Cos(endLatitude) *
            Math.Sin(deltaLongitude / 2) *
            Math.Sin(deltaLongitude / 2);

        var arc = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMeters * arc;
    }

    private static double ToRadians(double degrees)
        => degrees * Math.PI / 180d;
}
