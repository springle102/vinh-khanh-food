using System.Net;
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
        var request = httpContextAccessor.HttpContext?.Request;
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
        {
            if (request is not null &&
                !string.IsNullOrWhiteSpace(request.Host.Value) &&
                ShouldRewriteToRequestHost(absoluteUri, request))
            {
                return BuildRequestAbsoluteUrl(request, absoluteUri.PathAndQuery);
            }

            return normalized;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Host.Value))
        {
            return normalized;
        }

        return BuildRequestAbsoluteUrl(request, normalized);
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

    private static bool ShouldRewriteToRequestHost(Uri absoluteUri, HttpRequest request)
    {
        if (!IsLoopbackHost(absoluteUri.Host))
        {
            return false;
        }

        var requestPort = request.Host.Port ?? GetDefaultPort(request.Scheme);
        var absolutePort = absoluteUri.IsDefaultPort ? GetDefaultPort(absoluteUri.Scheme) : absoluteUri.Port;

        return !string.Equals(absoluteUri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(absoluteUri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase) ||
               absolutePort != requestPort;
    }

    private static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
               (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
    }

    private static int GetDefaultPort(string? scheme)
        => string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80;

    private static string BuildRequestAbsoluteUrl(HttpRequest request, string pathOrPathAndQuery)
    {
        var normalizedPath = pathOrPathAndQuery.StartsWith("/", StringComparison.Ordinal)
            ? pathOrPathAndQuery
            : $"/{pathOrPathAndQuery}";
        var pathBase = request.PathBase.HasValue ? request.PathBase.Value ?? string.Empty : string.Empty;

        if (!string.IsNullOrWhiteSpace(pathBase) &&
            !normalizedPath.StartsWith($"{pathBase}/", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalizedPath, pathBase, StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = $"{pathBase}{normalizedPath}";
        }

        return $"{request.Scheme}://{request.Host}{normalizedPath}";
    }
}
