using Microsoft.AspNetCore.Http;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class ResponseUrlNormalizer(IHttpContextAccessor httpContextAccessor)
{
    public string ToAbsoluteUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out _))
        {
            return normalized;
        }

        var request = httpContextAccessor.HttpContext?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.Host.Value))
        {
            return normalized;
        }

        var relativePath = normalized.StartsWith("/", StringComparison.Ordinal)
            ? normalized
            : $"/{normalized}";
        var pathBase = request.PathBase.HasValue ? request.PathBase.Value : string.Empty;
        return $"{request.Scheme}://{request.Host}{pathBase}{relativePath}";
    }

    public StoredFileResponse Normalize(StoredFileResponse response)
        => response with
        {
            Url = ToAbsoluteUrl(response.Url)
        };

    public MediaAsset Normalize(MediaAsset asset)
    {
        var absoluteUrl = ToAbsoluteUrl(asset.Url);
        if (string.Equals(absoluteUrl, asset.Url, StringComparison.Ordinal))
        {
            return asset;
        }

        return new MediaAsset
        {
            Id = asset.Id,
            EntityType = asset.EntityType,
            EntityId = asset.EntityId,
            Type = asset.Type,
            Url = absoluteUrl,
            AltText = asset.AltText,
            CreatedAt = asset.CreatedAt
        };
    }

    public FoodItem Normalize(FoodItem item)
    {
        var absoluteUrl = ToAbsoluteUrl(item.ImageUrl);
        if (string.Equals(absoluteUrl, item.ImageUrl, StringComparison.Ordinal))
        {
            return item;
        }

        return new FoodItem
        {
            Id = item.Id,
            PoiId = item.PoiId,
            Name = item.Name,
            Description = item.Description,
            PriceRange = item.PriceRange,
            ImageUrl = absoluteUrl,
            SpicyLevel = item.SpicyLevel
        };
    }

    public TourRoute Normalize(TourRoute route)
    {
        var absoluteUrl = ToAbsoluteUrl(route.CoverImageUrl);
        if (string.Equals(absoluteUrl, route.CoverImageUrl, StringComparison.Ordinal))
        {
            return route;
        }

        return new TourRoute
        {
            Id = route.Id,
            Name = route.Name,
            Theme = route.Theme,
            Description = route.Description,
            DurationMinutes = route.DurationMinutes,
            Difficulty = route.Difficulty,
            CoverImageUrl = absoluteUrl,
            IsFeatured = route.IsFeatured,
            StopPoiIds = [.. route.StopPoiIds],
            IsActive = route.IsActive,
            UpdatedBy = route.UpdatedBy,
            UpdatedAt = route.UpdatedAt
        };
    }

    public AdminBootstrapResponse Normalize(AdminBootstrapResponse response)
        => response with
        {
            MediaAssets = response.MediaAssets.Select(Normalize).ToList(),
            FoodItems = response.FoodItems.Select(Normalize).ToList(),
            Routes = response.Routes.Select(Normalize).ToList()
        };
}
