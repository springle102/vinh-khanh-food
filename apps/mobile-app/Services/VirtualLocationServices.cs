using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public interface IVirtualLocationService
{
    VirtualLocationPoint? CurrentLocation { get; }
    bool HasLocation { get; }
    void Initialize(double latitude, double longitude);
    void SetLocation(double latitude, double longitude);
    void Clear();
}

public interface IPoiProximityService
{
    PoiProximitySnapshot Evaluate(VirtualLocationPoint location, IEnumerable<PoiLocation> pois, double activationRadiusMeters);
    double CalculateDistanceMeters(double latitude1, double longitude1, double latitude2, double longitude2);
}

public sealed class VirtualLocationService : IVirtualLocationService
{
    private readonly object _syncRoot = new();
    private VirtualLocationPoint? _currentLocation;

    public VirtualLocationPoint? CurrentLocation
    {
        get
        {
            lock (_syncRoot)
            {
                return _currentLocation;
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

    public void Initialize(double latitude, double longitude)
        => SetLocation(latitude, longitude);

    public void SetLocation(double latitude, double longitude)
    {
        var nextLocation = new VirtualLocationPoint
        {
            Latitude = latitude,
            Longitude = longitude
        };

        lock (_syncRoot)
        {
            _currentLocation = nextLocation;
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _currentLocation = null;
        }
    }
}

public sealed class PoiProximityService : IPoiProximityService
{
    private const double EarthRadiusMeters = 6_371_000d;

    public PoiProximitySnapshot Evaluate(VirtualLocationPoint location, IEnumerable<PoiLocation> pois, double activationRadiusMeters)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(pois);

        PoiLocation? nearestPoi = null;
        PoiLocation? activePoi = null;
        double? nearestDistanceMeters = null;
        double? activeDistanceMeters = null;

        foreach (var poi in pois)
        {
            var distanceMeters = CalculateDistanceMeters(
                location.Latitude,
                location.Longitude,
                poi.Latitude,
                poi.Longitude);

            if (nearestDistanceMeters is null || distanceMeters < nearestDistanceMeters.Value)
            {
                nearestPoi = poi;
                nearestDistanceMeters = distanceMeters;
            }

            if (distanceMeters > activationRadiusMeters)
            {
                continue;
            }

            if (activeDistanceMeters is null || distanceMeters < activeDistanceMeters.Value)
            {
                activePoi = poi;
                activeDistanceMeters = distanceMeters;
            }
        }

        return new PoiProximitySnapshot
        {
            Location = location,
            NearestPoi = nearestPoi,
            NearestPoiDistanceMeters = nearestDistanceMeters,
            ActivePoi = activePoi,
            ActivePoiDistanceMeters = activeDistanceMeters,
            ActivationRadiusMeters = activationRadiusMeters
        };
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
