using VinhKhanh.MobileApp.Helpers;

namespace VinhKhanh.MobileApp.Models;

public sealed class LanguageOption : ObservableObject
{
    private bool _isSelected;

    public string Code { get; set; } = string.Empty;
    public string Flag { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class LocalizedTextSet
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Values => _values;

    public void Set(string languageCode, string? value)
    {
        if (string.IsNullOrWhiteSpace(languageCode) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _values[languageCode.Trim()] = value.Trim();
    }
}

public sealed class PoiLocation
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string PriceRange { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool IsFeatured { get; set; }
    public double HeatIntensity { get; set; }
    public string DistanceText { get; set; } = string.Empty;
}

public sealed class PoiExperienceDetail
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Rating { get; set; }
    public int ReviewCount { get; set; }
    public bool IsFeatured { get; set; }
    public LocalizedTextSet Name { get; } = new();
    public LocalizedTextSet Summary { get; } = new();
    public LocalizedTextSet Description { get; } = new();
    public LocalizedTextSet AudioUrls { get; } = new();
    public List<string> Images { get; set; } = [];
}

public sealed class MapHeatPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Intensity { get; set; }
}

public sealed class TourStop
{
    public string PoiId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string DistanceText { get; set; } = string.Empty;
}

public sealed class TourCheckpoint
{
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public string DistanceText { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
}

public sealed class TourPlan
{
    public string Title { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double ProgressValue { get; set; }
    public string ProgressText { get; set; } = string.Empty;
    public string SummaryText { get; set; } = string.Empty;
    public IReadOnlyList<TourStop> Stops { get; set; } = Array.Empty<TourStop>();
    public IReadOnlyList<TourCheckpoint> Checkpoints { get; set; } = Array.Empty<TourCheckpoint>();
}

public sealed class UserProfileCard
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string AvatarInitials { get; set; } = string.Empty;
    public string MetaLine { get; set; } = string.Empty;
}

public sealed class SettingsMenuItem
{
    public string Icon { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}
