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

    public AudioGuide Normalize(AudioGuide audioGuide)
    {
        var absoluteUrl = ToAbsoluteUrl(ResolveAudioGuideResponseUrl(audioGuide));
        if (string.Equals(absoluteUrl, audioGuide.AudioUrl, StringComparison.Ordinal))
        {
            return audioGuide;
        }

        return new AudioGuide
        {
            Id = audioGuide.Id,
            EntityType = audioGuide.EntityType,
            EntityId = audioGuide.EntityId,
            LanguageCode = audioGuide.LanguageCode,
            TranscriptText = audioGuide.TranscriptText,
            AudioUrl = absoluteUrl,
            AudioFilePath = audioGuide.AudioFilePath,
            AudioFileName = audioGuide.AudioFileName,
            VoiceType = audioGuide.VoiceType,
            SourceType = audioGuide.SourceType,
            Provider = audioGuide.Provider,
            VoiceId = audioGuide.VoiceId,
            ModelId = audioGuide.ModelId,
            OutputFormat = audioGuide.OutputFormat,
            DurationInSeconds = audioGuide.DurationInSeconds,
            FileSizeBytes = audioGuide.FileSizeBytes,
            TextHash = audioGuide.TextHash,
            ContentVersion = audioGuide.ContentVersion,
            GeneratedAt = audioGuide.GeneratedAt,
            GenerationStatus = audioGuide.GenerationStatus,
            ErrorMessage = audioGuide.ErrorMessage,
            IsOutdated = audioGuide.IsOutdated,
            Status = audioGuide.Status,
            UpdatedBy = audioGuide.UpdatedBy,
            UpdatedAt = audioGuide.UpdatedAt
        };
    }

    public PoiNarrationResponse Normalize(PoiNarrationResponse response)
        => response with
        {
            AudioGuide = response.AudioGuide is null ? null : Normalize(response.AudioGuide)
        };

    public PoiAudioGenerationResult Normalize(PoiAudioGenerationResult response)
        => response with
        {
            AudioGuide = response.AudioGuide is null ? null : Normalize(response.AudioGuide)
        };

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
            ImageUrl = absoluteUrl
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
            IsSystemRoute = route.IsSystemRoute,
            OwnerUserId = route.OwnerUserId,
            UpdatedBy = route.UpdatedBy,
            UpdatedAt = route.UpdatedAt
        };
    }

    public AdminBootstrapResponse Normalize(AdminBootstrapResponse response)
        => response with
        {
            AudioGuides = response.AudioGuides.Select(Normalize).ToList(),
            MediaAssets = response.MediaAssets.Select(Normalize).ToList(),
            FoodItems = response.FoodItems.Select(Normalize).ToList(),
            Routes = response.Routes.Select(Normalize).ToList()
        };

    public PoiDetailResponse Normalize(PoiDetailResponse response)
        => response with
        {
            AudioGuides = response.AudioGuides.Select(Normalize).ToList(),
            FoodItems = response.FoodItems.Select(Normalize).ToList(),
            MediaAssets = response.MediaAssets.Select(Normalize).ToList()
        };

    private string ResolveAudioGuideResponseUrl(AudioGuide audioGuide)
    {
        // Audio elements do not send the admin Bearer token, so expose the concrete
        // static asset URL when it exists instead of an authenticated API stream.
        return ResolveAudioGuideUrl(audioGuide.AudioUrl, audioGuide.AudioFilePath);
    }

    private string ResolveAudioGuideUrl(string? audioUrl, string? audioFilePath)
    {
        var normalizedAudioFilePath = audioFilePath?.Trim().Replace('\\', '/').TrimStart('/') ?? string.Empty;
        var normalizedAudioUrl = audioUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAudioFilePath))
        {
            return normalizedAudioUrl;
        }

        if (string.IsNullOrWhiteSpace(normalizedAudioUrl) ||
            ShouldPreferAudioFilePath(normalizedAudioUrl, normalizedAudioFilePath))
        {
            return normalizedAudioFilePath;
        }

        return normalizedAudioUrl;
    }

    private bool ShouldPreferAudioFilePath(string audioUrl, string normalizedAudioFilePath)
    {
        if (!Uri.TryCreate(audioUrl, UriKind.Absolute, out var absoluteUri))
        {
            if (!audioUrl.StartsWith("/", StringComparison.Ordinal) &&
                !audioUrl.Contains('/') &&
                !audioUrl.Contains('\\'))
            {
                return true;
            }

            return HasMissingStoragePrefix(audioUrl, normalizedAudioFilePath);
        }

        var request = httpContextAccessor.HttpContext?.Request;
        if (request is not null &&
            !string.IsNullOrWhiteSpace(request.Host.Value) &&
            DoesRequestHostDiffer(absoluteUri, request) &&
            MatchesStorageAssetPath(absoluteUri.AbsolutePath, normalizedAudioFilePath))
        {
            return true;
        }

        return HasMissingStoragePrefix(absoluteUri.AbsolutePath, normalizedAudioFilePath);
    }

    private static bool HasMissingStoragePrefix(string candidatePath, string normalizedAudioFilePath)
    {
        var normalizedCandidatePath = candidatePath.Trim().Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalizedCandidatePath) ||
            string.Equals(normalizedCandidatePath, normalizedAudioFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return MatchesStorageAssetPath(normalizedCandidatePath, normalizedAudioFilePath);
    }

    private static bool MatchesStorageAssetPath(string candidatePath, string normalizedAudioFilePath)
    {
        var normalizedCandidatePath = candidatePath.Trim().Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalizedCandidatePath) ||
            string.IsNullOrWhiteSpace(normalizedAudioFilePath))
        {
            return false;
        }

        if (string.Equals(normalizedCandidatePath, normalizedAudioFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        const string storageRootPrefix = "storage/audio/";
        if (!normalizedAudioFilePath.StartsWith(storageRootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedSuffix = normalizedAudioFilePath[storageRootPrefix.Length..];
        return string.Equals(normalizedCandidatePath, expectedSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DoesRequestHostDiffer(Uri absoluteUri, HttpRequest request)
    {
        var requestPort = request.Host.Port ?? GetDefaultPort(request.Scheme);
        var absolutePort = absoluteUri.IsDefaultPort ? GetDefaultPort(absoluteUri.Scheme) : absoluteUri.Port;

        return !string.Equals(absoluteUri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(absoluteUri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase) ||
               absolutePort != requestPort;
    }

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
