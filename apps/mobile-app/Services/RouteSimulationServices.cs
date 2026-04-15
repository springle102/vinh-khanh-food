using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public interface IRouteService
{
    Task<RouteComputationResult> BuildRouteAsync(
        UserLocationPoint origin,
        PoiLocation destination,
        CancellationToken cancellationToken = default);

    Task<RouteComputationResult> BuildRouteAsync(
        UserLocationPoint origin,
        IReadOnlyList<PoiLocation> destinations,
        CancellationToken cancellationToken = default);
}

public interface IRoutePoiFilterService
{
    IReadOnlyList<RouteEligiblePoi> FilterEligiblePois(
        IReadOnlyList<PoiLocation> pois,
        RouteComputationResult route,
        string destinationPoiId,
        double maxDistanceFromRouteMeters);
}

public interface ISimulationService
{
    event EventHandler<UserLocationChangedEventArgs>? LocationChanged;
    event EventHandler<SimulationStateChangedEventArgs>? StateChanged;

    UserLocationPoint? InitialLocation { get; }
    UserLocationPoint? CurrentLocation { get; }
    SimulationMode Mode { get; }
    SimulationRunState RunState { get; }
    RouteComputationResult? CurrentRoute { get; }
    bool HasRoute { get; }

    Task EnsureInitializedAsync(IReadOnlyList<PoiLocation> pois, CancellationToken cancellationToken = default);
    Task SetCurrentLocationAsync(
        double latitude,
        double longitude,
        bool fromUserInteraction = true,
        CancellationToken cancellationToken = default);
    Task SetModeAsync(SimulationMode mode, CancellationToken cancellationToken = default);
    Task LoadRouteAsync(RouteComputationResult route, CancellationToken cancellationToken = default);
    Task StartAsync(TimeSpan? interval = null, CancellationToken cancellationToken = default);
    Task PauseAsync();
    Task StopAsync(bool keepRoute = true);
    Task ResetAsync(CancellationToken cancellationToken = default);
}

public sealed class HttpRouteService : IRouteService
{
    private const string AppSettingsFileName = "appsettings.json";
    private const string DefaultRoutingBaseUrl = "https://router.project-osrm.org/";
    private const string DefaultRoutingProfile = "driving";
    private const double WalkingSpeedMetersPerSecond = 1.35d;

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private HttpClient? _client;
    private string? _resolvedRoutingBaseUrl;
    private RoutingRuntimeSettings? _runtimeSettings;

    public async Task<RouteComputationResult> BuildRouteAsync(
        UserLocationPoint origin,
        PoiLocation destination,
        CancellationToken cancellationToken = default)
        => await BuildRouteLegAsync(origin, destination, cancellationToken);

    public async Task<RouteComputationResult> BuildRouteAsync(
        UserLocationPoint origin,
        IReadOnlyList<PoiLocation> destinations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(destinations);

        if (destinations.Count == 0)
        {
            return new RouteComputationResult
            {
                StartLocation = origin,
                EndLocation = origin,
                Points = [origin],
                Provider = string.Empty
            };
        }

        var currentOrigin = origin;
        var allPoints = new List<UserLocationPoint>();
        var totalDistanceMeters = 0d;
        var totalDurationSeconds = 0d;
        var isFallback = false;
        string? provider = null;

        foreach (var destination in destinations)
        {
            var legRoute = await BuildRouteLegAsync(currentOrigin, destination, cancellationToken);
            if (legRoute.Points.Count > 0)
            {
                if (allPoints.Count == 0)
                {
                    allPoints.AddRange(legRoute.Points);
                }
                else
                {
                    allPoints.AddRange(legRoute.Points.Skip(1));
                }
            }

            totalDistanceMeters += legRoute.DistanceMeters;
            totalDurationSeconds += legRoute.DurationSeconds;
            isFallback |= legRoute.IsFallback;
            provider = string.IsNullOrWhiteSpace(provider)
                ? legRoute.Provider
                : string.Equals(provider, legRoute.Provider, StringComparison.OrdinalIgnoreCase)
                    ? provider
                    : "Composite";
            currentOrigin = legRoute.EndLocation;
        }

        return new RouteComputationResult
        {
            StartLocation = origin,
            EndLocation = currentOrigin,
            Points = allPoints.Count > 0 ? allPoints : [origin, currentOrigin],
            DistanceMeters = totalDistanceMeters,
            DurationSeconds = totalDurationSeconds,
            Provider = string.IsNullOrWhiteSpace(provider) ? "Composite" : provider,
            IsFallback = isFallback
        };
    }

    private async Task<RouteComputationResult> BuildRouteLegAsync(
        UserLocationPoint origin,
        PoiLocation destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(destination);

        try
        {
            var client = await GetClientAsync();
            if (client is not null)
            {
                var runtimeSettings = await LoadRuntimeSettingsAsync();
                var profile = ResolveRoutingProfile(runtimeSettings);
                var requestUri =
                    $"route/v1/{profile}/" +
                    $"{origin.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{origin.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)};" +
                    $"{destination.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{destination.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                    "?alternatives=false&steps=false&overview=full&geometries=geojson";

                var response = await client.GetFromJsonAsync<OsrmRouteResponse>(requestUri, _jsonOptions, cancellationToken);
                var bestRoute = response?.Routes?.FirstOrDefault();
                if (bestRoute?.Geometry?.Coordinates?.Count > 1)
                {
                    return new RouteComputationResult
                    {
                        StartLocation = origin,
                        EndLocation = new UserLocationPoint
                        {
                            Latitude = destination.Latitude,
                            Longitude = destination.Longitude
                        },
                        Points = bestRoute.Geometry.Coordinates
                            .Where(item => item.Count >= 2)
                            .Select(item => new UserLocationPoint
                            {
                                Latitude = item[1],
                                Longitude = item[0]
                            })
                            .ToList(),
                        DistanceMeters = bestRoute.Distance,
                        DurationSeconds = bestRoute.Duration,
                        Provider = "OSRM",
                        IsFallback = false
                    };
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Fall back to a straight-line route so the emulator demo still works offline.
        }

        return CreateFallbackRoute(origin, destination);
    }

    private async Task<HttpClient?> GetClientAsync()
    {
        var runtimeSettings = await LoadRuntimeSettingsAsync();
        var nextBaseUrl = MobileApiEndpointHelper.EnsureTrailingSlash(ResolveRoutingBaseUrl(runtimeSettings));
        if (string.IsNullOrWhiteSpace(nextBaseUrl))
        {
            return null;
        }

        if (_client is not null &&
            string.Equals(_resolvedRoutingBaseUrl, nextBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return _client;
        }

        _client?.Dispose();
        _client = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            BaseAddress = new Uri(nextBaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(8)
        };
        _resolvedRoutingBaseUrl = nextBaseUrl;
        return _client;
    }

    private async Task<RoutingRuntimeSettings> LoadRuntimeSettingsAsync()
    {
        if (_runtimeSettings is not null)
        {
            return _runtimeSettings;
        }

        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(AppSettingsFileName);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            _runtimeSettings = JsonSerializer.Deserialize<RoutingRuntimeSettings>(content, _jsonOptions)
                ?? new RoutingRuntimeSettings();
        }
        catch
        {
            _runtimeSettings = new RoutingRuntimeSettings();
        }

        return _runtimeSettings;
    }

    private static string ResolveRoutingBaseUrl(RoutingRuntimeSettings runtimeSettings)
        => string.IsNullOrWhiteSpace(runtimeSettings.RoutingBaseUrl)
            ? DefaultRoutingBaseUrl
            : runtimeSettings.RoutingBaseUrl.Trim();

    private static string ResolveRoutingProfile(RoutingRuntimeSettings runtimeSettings)
        => string.IsNullOrWhiteSpace(runtimeSettings.RoutingProfile)
            ? DefaultRoutingProfile
            : runtimeSettings.RoutingProfile.Trim();

    private static RouteComputationResult CreateFallbackRoute(UserLocationPoint origin, PoiLocation destination)
    {
        var destinationPoint = new UserLocationPoint
        {
            Latitude = destination.Latitude,
            Longitude = destination.Longitude
        };
        var distanceMeters = CalculateDistanceMeters(origin, destinationPoint);
        var interpolatedPoints = new List<UserLocationPoint>();
        var steps = Math.Max(8, (int)Math.Ceiling(distanceMeters / 20d));
        for (var index = 0; index <= steps; index++)
        {
            var progress = steps == 0 ? 1d : index / (double)steps;
            interpolatedPoints.Add(Interpolate(origin, destinationPoint, progress));
        }

        return new RouteComputationResult
        {
            StartLocation = origin,
            EndLocation = destinationPoint,
            Points = interpolatedPoints,
            DistanceMeters = distanceMeters,
            DurationSeconds = distanceMeters / WalkingSpeedMetersPerSecond,
            Provider = "Fallback",
            IsFallback = true
        };
    }

    private static UserLocationPoint Interpolate(UserLocationPoint start, UserLocationPoint end, double progress)
        => new()
        {
            Latitude = start.Latitude + ((end.Latitude - start.Latitude) * progress),
            Longitude = start.Longitude + ((end.Longitude - start.Longitude) * progress)
        };

    private static double CalculateDistanceMeters(UserLocationPoint start, UserLocationPoint end)
    {
        const double earthRadiusMeters = 6_371_000d;
        var latitudeDelta = DegreesToRadians(end.Latitude - start.Latitude);
        var longitudeDelta = DegreesToRadians(end.Longitude - start.Longitude);
        var latitudeStart = DegreesToRadians(start.Latitude);
        var latitudeEnd = DegreesToRadians(end.Latitude);

        var haversine =
            Math.Pow(Math.Sin(latitudeDelta / 2d), 2d) +
            Math.Cos(latitudeStart) *
            Math.Cos(latitudeEnd) *
            Math.Pow(Math.Sin(longitudeDelta / 2d), 2d);
        var centralAngle = 2d * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1d - haversine));
        return earthRadiusMeters * centralAngle;
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180d);

    private sealed class OsrmRouteResponse
    {
        public List<OsrmRouteDto> Routes { get; set; } = [];
    }

    private sealed class RoutingRuntimeSettings
    {
        public string? RoutingBaseUrl { get; set; }
        public string? RoutingProfile { get; set; }
    }

    private sealed class OsrmRouteDto
    {
        public double Distance { get; set; }
        public double Duration { get; set; }
        public OsrmGeometryDto? Geometry { get; set; }
    }

    private sealed class OsrmGeometryDto
    {
        public List<List<double>> Coordinates { get; set; } = [];
    }
}

public sealed class RoutePoiFilterService : IRoutePoiFilterService
{
    public IReadOnlyList<RouteEligiblePoi> FilterEligiblePois(
        IReadOnlyList<PoiLocation> pois,
        RouteComputationResult route,
        string destinationPoiId,
        double maxDistanceFromRouteMeters)
    {
        ArgumentNullException.ThrowIfNull(pois);
        ArgumentNullException.ThrowIfNull(route);

        if (route.Points.Count == 0)
        {
            return Array.Empty<RouteEligiblePoi>();
        }

        var eligiblePois = new List<RouteEligiblePoi>();
        foreach (var poi in pois)
        {
            var metrics = GetClosestRouteMetrics(route.Points, poi.Latitude, poi.Longitude);
            var isDestination = string.Equals(poi.Id, destinationPoiId, StringComparison.OrdinalIgnoreCase);
            if (!isDestination && metrics.DistanceMeters > maxDistanceFromRouteMeters)
            {
                continue;
            }

            eligiblePois.Add(new RouteEligiblePoi
            {
                Poi = poi,
                IsDestination = isDestination,
                DistanceToRouteMeters = metrics.DistanceMeters,
                ClosestSegmentIndex = metrics.SegmentIndex
            });
        }

        return eligiblePois
            .OrderBy(item => item.ClosestSegmentIndex)
            .ThenBy(item => item.DistanceToRouteMeters)
            .ToList();
    }

    private static (double DistanceMeters, int SegmentIndex) GetClosestRouteMetrics(
        IReadOnlyList<UserLocationPoint> routePoints,
        double latitude,
        double longitude)
    {
        if (routePoints.Count == 1)
        {
            var singlePointDistance = CalculateDistanceMeters(routePoints[0], latitude, longitude);
            return (singlePointDistance, 0);
        }

        var minDistance = double.MaxValue;
        var closestSegmentIndex = 0;
        for (var index = 0; index < routePoints.Count - 1; index++)
        {
            var segmentDistance = CalculateDistanceToSegmentMeters(
                routePoints[index],
                routePoints[index + 1],
                latitude,
                longitude);
            if (segmentDistance >= minDistance)
            {
                continue;
            }

            minDistance = segmentDistance;
            closestSegmentIndex = index;
        }

        return (minDistance, closestSegmentIndex);
    }

    private static double CalculateDistanceToSegmentMeters(
        UserLocationPoint segmentStart,
        UserLocationPoint segmentEnd,
        double latitude,
        double longitude)
    {
        var origin = new UserLocationPoint
        {
            Latitude = (segmentStart.Latitude + segmentEnd.Latitude + latitude) / 3d,
            Longitude = (segmentStart.Longitude + segmentEnd.Longitude + longitude) / 3d
        };

        var start = ProjectToMeters(origin, segmentStart.Latitude, segmentStart.Longitude);
        var end = ProjectToMeters(origin, segmentEnd.Latitude, segmentEnd.Longitude);
        var point = ProjectToMeters(origin, latitude, longitude);

        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        if (Math.Abs(deltaX) < double.Epsilon && Math.Abs(deltaY) < double.Epsilon)
        {
            return Math.Sqrt(Math.Pow(point.X - start.X, 2d) + Math.Pow(point.Y - start.Y, 2d));
        }

        var projection = ((point.X - start.X) * deltaX + (point.Y - start.Y) * deltaY) /
                         ((deltaX * deltaX) + (deltaY * deltaY));
        var clampedProjection = Math.Clamp(projection, 0d, 1d);
        var projectedX = start.X + (clampedProjection * deltaX);
        var projectedY = start.Y + (clampedProjection * deltaY);

        return Math.Sqrt(Math.Pow(point.X - projectedX, 2d) + Math.Pow(point.Y - projectedY, 2d));
    }

    private static double CalculateDistanceMeters(UserLocationPoint point, double latitude, double longitude)
    {
        const double earthRadiusMeters = 6_371_000d;
        var latitudeDelta = DegreesToRadians(latitude - point.Latitude);
        var longitudeDelta = DegreesToRadians(longitude - point.Longitude);
        var latitudeStart = DegreesToRadians(point.Latitude);
        var latitudeEnd = DegreesToRadians(latitude);
        var haversine =
            Math.Pow(Math.Sin(latitudeDelta / 2d), 2d) +
            Math.Cos(latitudeStart) *
            Math.Cos(latitudeEnd) *
            Math.Pow(Math.Sin(longitudeDelta / 2d), 2d);
        var centralAngle = 2d * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1d - haversine));
        return earthRadiusMeters * centralAngle;
    }

    private static (double X, double Y) ProjectToMeters(UserLocationPoint origin, double latitude, double longitude)
    {
        const double earthRadiusMeters = 6_371_000d;
        var averageLatitude = DegreesToRadians((origin.Latitude + latitude) / 2d);
        var x = DegreesToRadians(longitude - origin.Longitude) * Math.Cos(averageLatitude) * earthRadiusMeters;
        var y = DegreesToRadians(latitude - origin.Latitude) * earthRadiusMeters;
        return (x, y);
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180d);
}

public sealed class SimulationService : ISimulationService
{
    private const double SimulationStepMeters = 2.5d;
    private static readonly TimeSpan DefaultTickInterval = TimeSpan.FromMilliseconds(450);

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private CancellationTokenSource? _runCancellationSource;
    private Task? _runTask;
    private UserLocationPoint? _initialLocation;
    private UserLocationPoint? _currentLocation;
    private RouteComputationResult? _currentRoute;
    private List<UserLocationPoint> _routePlaybackPoints = [];
    private int _routePlaybackIndex;

    public event EventHandler<UserLocationChangedEventArgs>? LocationChanged;
    public event EventHandler<SimulationStateChangedEventArgs>? StateChanged;

    public UserLocationPoint? InitialLocation => Clone(_initialLocation);

    public UserLocationPoint? CurrentLocation => Clone(_currentLocation);

    public SimulationMode Mode { get; private set; } = SimulationMode.Manual;

    public SimulationRunState RunState { get; private set; } = SimulationRunState.Idle;

    public RouteComputationResult? CurrentRoute => _currentRoute;

    public bool HasRoute => _currentRoute is not null && _routePlaybackPoints.Count > 1;

    public async Task EnsureInitializedAsync(IReadOnlyList<PoiLocation> pois, CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_currentLocation is not null)
            {
                return;
            }

            var initialLocation = ResolveInitialLocation(pois);
            _initialLocation = initialLocation;
            _currentLocation = Clone(initialLocation);
            PublishStateChanged();
        }
        finally
        {
            _stateLock.Release();
        }

        PublishLocationChanged(_currentLocation!);
    }

    public async Task SetCurrentLocationAsync(
        double latitude,
        double longitude,
        bool fromUserInteraction = true,
        CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (fromUserInteraction && RunState == SimulationRunState.Running)
            {
                CancelRunnerLocked();
                RunState = SimulationRunState.Paused;
            }

            if (fromUserInteraction)
            {
                Mode = SimulationMode.Manual;
            }

            _currentLocation = new UserLocationPoint
            {
                Latitude = latitude,
                Longitude = longitude
            };
            if (_routePlaybackPoints.Count > 0)
            {
                _routePlaybackIndex = FindClosestRouteIndex(_currentLocation, _routePlaybackPoints);
            }

            PublishStateChanged();
        }
        finally
        {
            _stateLock.Release();
        }

        PublishLocationChanged(_currentLocation!);
    }

    public async Task SetModeAsync(SimulationMode mode, CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (Mode == mode)
            {
                return;
            }

            Mode = mode;
            if (mode == SimulationMode.Manual && RunState == SimulationRunState.Running)
            {
                CancelRunnerLocked();
                RunState = SimulationRunState.Paused;
            }

            PublishStateChanged();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task LoadRouteAsync(RouteComputationResult route, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            CancelRunnerLocked();
            _currentRoute = route;
            _routePlaybackPoints = DensifyRoute(route.Points, SimulationStepMeters);
            _routePlaybackIndex = 0;

            if (_routePlaybackPoints.Count > 0)
            {
                _currentLocation = Clone(_routePlaybackPoints[0]);
                _initialLocation ??= Clone(_currentLocation);
            }

            RunState = _routePlaybackPoints.Count > 1
                ? SimulationRunState.Ready
                : SimulationRunState.Completed;
            PublishStateChanged();
        }
        finally
        {
            _stateLock.Release();
        }

        if (_currentLocation is not null)
        {
            PublishLocationChanged(_currentLocation);
        }
    }

    public async Task StartAsync(TimeSpan? interval = null, CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_routePlaybackPoints.Count <= 1)
            {
                return;
            }

            if (_runTask is { IsCompleted: false })
            {
                return;
            }

            Mode = SimulationMode.Auto;
            if (_routePlaybackIndex >= _routePlaybackPoints.Count - 1)
            {
                _routePlaybackIndex = 0;
                _currentLocation = Clone(_routePlaybackPoints[0]);
            }

            _runCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            RunState = SimulationRunState.Running;
            _runTask = RunSimulationLoopAsync(interval ?? DefaultTickInterval, _runCancellationSource.Token);
            PublishStateChanged();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task PauseAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (RunState != SimulationRunState.Running)
            {
                return;
            }

            CancelRunnerLocked();
            RunState = SimulationRunState.Paused;
            PublishStateChanged();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task StopAsync(bool keepRoute = true)
    {
        await _stateLock.WaitAsync();
        try
        {
            CancelRunnerLocked();
            if (!keepRoute)
            {
                _currentRoute = null;
                _routePlaybackPoints = [];
                _routePlaybackIndex = 0;
                RunState = SimulationRunState.Idle;
            }
            else
            {
                RunState = _routePlaybackPoints.Count > 1
                    ? SimulationRunState.Ready
                    : SimulationRunState.Idle;
            }

            PublishStateChanged();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            CancelRunnerLocked();
            _currentRoute = null;
            _routePlaybackPoints = [];
            _routePlaybackIndex = 0;
            _currentLocation = Clone(_initialLocation);
            Mode = SimulationMode.Manual;
            RunState = SimulationRunState.Idle;
            PublishStateChanged();
        }
        finally
        {
            _stateLock.Release();
        }

        if (_currentLocation is not null)
        {
            PublishLocationChanged(_currentLocation);
        }
    }

    private async Task RunSimulationLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                UserLocationPoint? nextLocation;
                bool completed = false;

                await _stateLock.WaitAsync(cancellationToken);
                try
                {
                    if (_routePlaybackPoints.Count <= 1)
                    {
                        RunState = SimulationRunState.Completed;
                        PublishStateChanged();
                        return;
                    }

                    if (_routePlaybackIndex >= _routePlaybackPoints.Count - 1)
                    {
                        RunState = SimulationRunState.Completed;
                        CancelRunnerLocked();
                        PublishStateChanged();
                        completed = true;
                        nextLocation = Clone(_currentLocation);
                    }
                    else
                    {
                        _routePlaybackIndex++;
                        _currentLocation = Clone(_routePlaybackPoints[_routePlaybackIndex]);
                        nextLocation = Clone(_currentLocation);

                        if (_routePlaybackIndex >= _routePlaybackPoints.Count - 1)
                        {
                            RunState = SimulationRunState.Completed;
                            CancelRunnerLocked();
                            completed = true;
                        }

                        PublishStateChanged();
                    }
                }
                finally
                {
                    _stateLock.Release();
                }

                if (nextLocation is not null)
                {
                    PublishLocationChanged(nextLocation);
                }

                if (completed)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void CancelRunnerLocked()
    {
        _runCancellationSource?.Cancel();
        _runCancellationSource?.Dispose();
        _runCancellationSource = null;
        _runTask = null;
    }

    private void PublishLocationChanged(UserLocationPoint location)
    {
        LocationChanged?.Invoke(this, new UserLocationChangedEventArgs
        {
            Location = location,
            IsMock = true,
            CapturedAt = DateTimeOffset.UtcNow
        });
    }

    private void PublishStateChanged()
        => StateChanged?.Invoke(this, new SimulationStateChangedEventArgs
        {
            Mode = Mode,
            RunState = RunState,
            HasRoute = HasRoute,
            CurrentLocation = Clone(_currentLocation)
        });

    private static UserLocationPoint ResolveInitialLocation(IReadOnlyList<PoiLocation> pois)
    {
        if (pois.Count == 0)
        {
            return new UserLocationPoint
            {
                Latitude = 10.7578,
                Longitude = 106.7033
            };
        }

        var averageLatitude = pois.Average(item => item.Latitude);
        var averageLongitude = pois.Average(item => item.Longitude);
        return new UserLocationPoint
        {
            Latitude = averageLatitude - 0.00075d,
            Longitude = averageLongitude - 0.00035d
        };
    }

    private static List<UserLocationPoint> DensifyRoute(
        IReadOnlyList<UserLocationPoint> points,
        double maxSegmentMeters)
    {
        if (points.Count <= 1)
        {
            return points.ToList();
        }

        var densified = new List<UserLocationPoint> { Clone(points[0])! };
        for (var index = 0; index < points.Count - 1; index++)
        {
            var start = points[index];
            var end = points[index + 1];
            var distanceMeters = CalculateDistanceMeters(start, end);
            var steps = Math.Max(1, (int)Math.Ceiling(distanceMeters / maxSegmentMeters));
            for (var step = 1; step <= steps; step++)
            {
                var progress = step / (double)steps;
                densified.Add(new UserLocationPoint
                {
                    Latitude = start.Latitude + ((end.Latitude - start.Latitude) * progress),
                    Longitude = start.Longitude + ((end.Longitude - start.Longitude) * progress)
                });
            }
        }

        return densified;
    }

    private static int FindClosestRouteIndex(UserLocationPoint currentLocation, IReadOnlyList<UserLocationPoint> routePoints)
    {
        if (routePoints.Count == 0)
        {
            return 0;
        }

        var closestIndex = 0;
        var minDistance = double.MaxValue;
        for (var index = 0; index < routePoints.Count; index++)
        {
            var distance = CalculateDistanceMeters(currentLocation, routePoints[index]);
            if (distance >= minDistance)
            {
                continue;
            }

            minDistance = distance;
            closestIndex = index;
        }

        return closestIndex;
    }

    private static double CalculateDistanceMeters(UserLocationPoint start, UserLocationPoint end)
    {
        const double earthRadiusMeters = 6_371_000d;
        var latitudeDelta = DegreesToRadians(end.Latitude - start.Latitude);
        var longitudeDelta = DegreesToRadians(end.Longitude - start.Longitude);
        var latitudeStart = DegreesToRadians(start.Latitude);
        var latitudeEnd = DegreesToRadians(end.Latitude);

        var haversine =
            Math.Pow(Math.Sin(latitudeDelta / 2d), 2d) +
            Math.Cos(latitudeStart) *
            Math.Cos(latitudeEnd) *
            Math.Pow(Math.Sin(longitudeDelta / 2d), 2d);
        var centralAngle = 2d * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1d - haversine));
        return earthRadiusMeters * centralAngle;
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180d);

    private static UserLocationPoint? Clone(UserLocationPoint? source)
        => source is null
            ? null
            : new UserLocationPoint
            {
                Latitude = source.Latitude,
                Longitude = source.Longitude
            };
}
