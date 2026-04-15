namespace VinhKhanh.MobileApp.Models;

public enum SimulationMode
{
    Manual,
    Auto
}

public enum SimulationRunState
{
    Idle,
    Ready,
    Running,
    Paused,
    Completed
}

public enum SimulationContextKind
{
    None,
    Poi,
    Tour
}

public sealed class RouteComputationResult
{
    public UserLocationPoint StartLocation { get; init; } = new();
    public UserLocationPoint EndLocation { get; init; } = new();
    public IReadOnlyList<UserLocationPoint> Points { get; init; } = Array.Empty<UserLocationPoint>();
    public double DistanceMeters { get; init; }
    public double DurationSeconds { get; init; }
    public string Provider { get; init; } = string.Empty;
    public bool IsFallback { get; init; }
}

public sealed class RouteEligiblePoi
{
    public PoiLocation Poi { get; init; } = new();
    public bool IsDestination { get; init; }
    public double DistanceToRouteMeters { get; init; }
    public int ClosestSegmentIndex { get; init; }
}

public sealed class RouteNarrationPlan
{
    public SimulationContextKind ContextKind { get; init; }
    public string ContextId { get; init; } = string.Empty;
    public string ContextTitle { get; init; } = string.Empty;
    public PoiLocation DestinationPoi { get; init; } = new();
    public RouteComputationResult Route { get; init; } = new();
    public IReadOnlyList<RouteEligiblePoi> EligiblePois { get; init; } = Array.Empty<RouteEligiblePoi>();
    public double ActivationRadiusMeters { get; init; } = 30d;
    public double RouteSnapRadiusMeters { get; init; } = 25d;
}

public sealed class MapRouteSimulationState
{
    public string ContextKind { get; init; } = string.Empty;
    public string ContextId { get; init; } = string.Empty;
    public string ContextTitle { get; init; } = string.Empty;
    public string? DestinationPoiId { get; init; }
    public string Mode { get; init; } = string.Empty;
    public string RunState { get; init; } = string.Empty;
    public string SummaryText { get; init; } = string.Empty;
    public string ProviderText { get; init; } = string.Empty;
    public List<UserLocationPoint> Points { get; init; } = [];
    public List<string> EligiblePoiIds { get; init; } = [];
}

public sealed class SimulationStateChangedEventArgs : EventArgs
{
    public SimulationMode Mode { get; init; }
    public SimulationRunState RunState { get; init; }
    public bool HasRoute { get; init; }
    public UserLocationPoint? CurrentLocation { get; init; }
}
