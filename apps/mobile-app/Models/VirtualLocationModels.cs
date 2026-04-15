namespace VinhKhanh.MobileApp.Models;

public sealed class UserLocationPoint
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}

public sealed class PoiProximitySnapshot
{
    public UserLocationPoint Location { get; init; } = new();
    public PoiLocation? NearestPoi { get; init; }
    public double? NearestPoiDistanceMeters { get; init; }
    public PoiLocation? ActivePoi { get; init; }
    public double? ActivePoiDistanceMeters { get; init; }
    public double ActivationRadiusMeters { get; init; }

    public bool IsInsideActivationRadius => ActivePoi is not null;
}

public sealed class MapUserLocationState
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
    public string SourceLabel { get; init; } = string.Empty;
    public string SourceText { get; init; } = string.Empty;
}

public sealed class UserLocationChangedEventArgs : EventArgs
{
    public UserLocationPoint Location { get; init; } = new();
    public bool IsMock { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum AutoNarrationDecision
{
    None,
    Played,
    Queued,
    Disabled,
    Busy,
    Cooldown,
    NoNearbyPoi,
    NoRoute
}

public sealed class AutoNarrationResult
{
    public PoiProximitySnapshot Snapshot { get; init; } = new();
    public PoiLocation? TriggeredPoi { get; init; }
    public PoiExperienceDetail? TriggeredDetail { get; init; }
    public AutoNarrationDecision Decision { get; init; }
    public bool IsMockLocation { get; init; }

    public bool PlaybackStarted => Decision == AutoNarrationDecision.Played;
}
