using System.Text.Json;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public interface ITourStateService
{
    Task<TourSessionState?> GetActiveTourAsync();
    Task<TourSessionState> StartTourAsync(string tourId);
    Task<TourSessionState?> MarkCheckpointVisitedAsync(string poiId);
    Task ClearActiveTourAsync();
}

public sealed class TourStateService : ITourStateService
{
    private const string ActiveTourPreferenceKey = "vkfood.tour.active";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public Task<TourSessionState?> GetActiveTourAsync()
    {
        var json = Preferences.Default.Get(ActiveTourPreferenceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Task.FromResult<TourSessionState?>(null);
        }

        try
        {
            var state = JsonSerializer.Deserialize<TourSessionState>(json, JsonOptions);
            if (state is null || string.IsNullOrWhiteSpace(state.TourId))
            {
                Preferences.Default.Remove(ActiveTourPreferenceKey);
                return Task.FromResult<TourSessionState?>(null);
            }

            state.CompletedPoiIds = state.CompletedPoiIds
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Task.FromResult<TourSessionState?>(state);
        }
        catch
        {
            Preferences.Default.Remove(ActiveTourPreferenceKey);
            return Task.FromResult<TourSessionState?>(null);
        }
    }

    public async Task<TourSessionState> StartTourAsync(string tourId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tourId);

        var existing = await GetActiveTourAsync();
        var now = DateTimeOffset.UtcNow;

        if (existing is not null && string.Equals(existing.TourId, tourId, StringComparison.OrdinalIgnoreCase))
        {
            existing.UpdatedAt = now;
            await SaveAsync(existing);
            return existing;
        }

        var nextState = new TourSessionState
        {
            TourId = tourId.Trim(),
            CompletedPoiIds = [],
            StartedAt = now,
            UpdatedAt = now
        };

        await SaveAsync(nextState);
        return nextState;
    }

    public async Task<TourSessionState?> MarkCheckpointVisitedAsync(string poiId)
    {
        if (string.IsNullOrWhiteSpace(poiId))
        {
            return await GetActiveTourAsync();
        }

        var state = await GetActiveTourAsync();
        if (state is null)
        {
            return null;
        }

        if (!state.CompletedPoiIds.Contains(poiId, StringComparer.OrdinalIgnoreCase))
        {
            state.CompletedPoiIds.Add(poiId.Trim());
        }

        state.CompletedPoiIds = state.CompletedPoiIds
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        state.UpdatedAt = DateTimeOffset.UtcNow;

        await SaveAsync(state);
        return state;
    }

    public Task ClearActiveTourAsync()
    {
        Preferences.Default.Remove(ActiveTourPreferenceKey);
        return Task.CompletedTask;
    }

    private Task SaveAsync(TourSessionState state)
    {
        Preferences.Default.Set(ActiveTourPreferenceKey, JsonSerializer.Serialize(state, JsonOptions));
        return Task.CompletedTask;
    }
}
