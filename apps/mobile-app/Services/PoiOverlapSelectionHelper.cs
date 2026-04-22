using System.Globalization;
using VinhKhanh.Core.Pois;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

internal readonly record struct PoiOverlapCandidate(
    PoiLocation Poi,
    double DistanceToUserMeters,
    double TriggerRadiusMeters,
    bool IsInsideTriggerRadius);

internal static class PoiOverlapSelectionHelper
{
    private const double MinimumTriggerRadiusMeters = 20d;

    public static List<PoiOverlapCandidate> BuildCandidates(
        UserLocationPoint location,
        IEnumerable<PoiLocation> pois,
        Func<double, double, double, double, double> distanceCalculator,
        double? fallbackTriggerRadiusMeters = null)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(pois);
        ArgumentNullException.ThrowIfNull(distanceCalculator);

        var candidates = new List<PoiOverlapCandidate>();
        foreach (var poi in pois)
        {
            var distanceMeters = distanceCalculator(
                location.Latitude,
                location.Longitude,
                poi.Latitude,
                poi.Longitude);
            var triggerRadiusMeters = ResolveTriggerRadius(poi, fallbackTriggerRadiusMeters);
            candidates.Add(new PoiOverlapCandidate(
                poi,
                distanceMeters,
                triggerRadiusMeters,
                distanceMeters <= triggerRadiusMeters));
        }

        return candidates;
    }

    // Keep one deterministic place-selection rule for every location flow:
    // Premium first, then higher priority, then nearer distance, then smaller id.
    public static PoiOverlapCandidate? SelectBestCandidate(IEnumerable<PoiOverlapCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        PoiOverlapCandidate? best = null;
        foreach (var candidate in candidates)
        {
            if (!candidate.IsInsideTriggerRadius)
            {
                continue;
            }

            if (best is null || CompareByBusinessPriority(candidate, best.Value) < 0)
            {
                best = candidate;
            }
        }

        return best;
    }

    public static PoiProximitySnapshot BuildSnapshot(
        UserLocationPoint location,
        IReadOnlyList<PoiOverlapCandidate> candidates,
        PoiOverlapCandidate? activeCandidate = null)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(candidates);

        PoiOverlapCandidate? nearestCandidate = null;
        if (candidates.Count > 0)
        {
            nearestCandidate = candidates
                .OrderBy(candidate => candidate.DistanceToUserMeters)
                .ThenBy(candidate => candidate.Poi.Id, StringComparer.Ordinal)
                .First();
        }

        return new PoiProximitySnapshot
        {
            Location = location,
            NearestPoi = nearestCandidate?.Poi,
            NearestPoiDistanceMeters = nearestCandidate?.DistanceToUserMeters,
            ActivePoi = activeCandidate?.Poi,
            ActivePoiDistanceMeters = activeCandidate?.DistanceToUserMeters,
            ActivationRadiusMeters = activeCandidate?.TriggerRadiusMeters ?? 0d
        };
    }

    public static string DescribeCandidates(IReadOnlyList<PoiOverlapCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return "none";
        }

        return string.Join(
            "; ",
            candidates
                .OrderBy(candidate => candidate.IsInsideTriggerRadius ? 0 : 1)
                .ThenBy(candidate => candidate, CandidateSelectionComparer.Instance)
                .Select(candidate => DescribeCandidate(candidate)));
    }

    public static string DescribeCandidate(PoiOverlapCandidate? candidate)
    {
        if (candidate is not PoiOverlapCandidate value)
        {
            return "none";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}|tier={1}|priority={2}|distance={3:0.0}|radius={4:0.0}|inside={5}",
            value.Poi.Id,
            PoiPlaceTierCatalog.Normalize(value.Poi.PlaceTier),
            Math.Max(0, value.Poi.Priority),
            value.DistanceToUserMeters,
            value.TriggerRadiusMeters,
            value.IsInsideTriggerRadius);
    }

    public static double ResolveTriggerRadius(PoiLocation poi, double? fallbackTriggerRadiusMeters = null)
    {
        ArgumentNullException.ThrowIfNull(poi);

        if (double.IsFinite(poi.TriggerRadius) && poi.TriggerRadius >= MinimumTriggerRadiusMeters)
        {
            return poi.TriggerRadius;
        }

        return fallbackTriggerRadiusMeters is double fallback && double.IsFinite(fallback) && fallback >= MinimumTriggerRadiusMeters
            ? fallback
            : MinimumTriggerRadiusMeters;
    }

    private static int CompareByBusinessPriority(PoiOverlapCandidate left, PoiOverlapCandidate right)
    {
        var leftTier = (int)PoiPlaceTierCatalog.Normalize(left.Poi.PlaceTier);
        var rightTier = (int)PoiPlaceTierCatalog.Normalize(right.Poi.PlaceTier);
        var byTier = rightTier.CompareTo(leftTier);
        if (byTier != 0)
        {
            return byTier;
        }

        var byPriority = Math.Max(0, right.Poi.Priority).CompareTo(Math.Max(0, left.Poi.Priority));
        if (byPriority != 0)
        {
            return byPriority;
        }

        var byDistance = left.DistanceToUserMeters.CompareTo(right.DistanceToUserMeters);
        if (byDistance != 0)
        {
            return byDistance;
        }

        return string.CompareOrdinal(left.Poi.Id, right.Poi.Id);
    }

    private sealed class CandidateSelectionComparer : IComparer<PoiOverlapCandidate>
    {
        public static CandidateSelectionComparer Instance { get; } = new();

        public int Compare(PoiOverlapCandidate x, PoiOverlapCandidate y)
            => CompareByBusinessPriority(x, y);
    }
}
