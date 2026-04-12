namespace VinhKhanh.MobileApp.Models;

public sealed class VirtualLocationPoint
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}

public sealed class PoiProximitySnapshot
{
    public VirtualLocationPoint Location { get; init; } = new();
    public PoiLocation? NearestPoi { get; init; }
    public double? NearestPoiDistanceMeters { get; init; }
    public PoiLocation? ActivePoi { get; init; }
    public double? ActivePoiDistanceMeters { get; init; }
    public double ActivationRadiusMeters { get; init; }

    public bool IsInsideActivationRadius => ActivePoi is not null;
}

public sealed class MapVirtualUserState
{
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string? ActivePoiId { get; init; }
    public string PopupTitle { get; init; } = string.Empty;
    public string CoordinatesLabel { get; init; } = string.Empty;
    public string CoordinatesText { get; init; } = string.Empty;
    public string StatusLabel { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string NearestPoiLabel { get; init; } = string.Empty;
    public string NearestPoiText { get; init; } = string.Empty;
    public string NearestDistanceLabel { get; init; } = string.Empty;
    public string NearestDistanceText { get; init; } = string.Empty;
}
