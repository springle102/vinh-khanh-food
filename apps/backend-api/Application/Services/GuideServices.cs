using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VinhKhanh.BackendApi.Authentication;
using VinhKhanh.BackendApi.Domain.Entities;
using VinhKhanh.BackendApi.DTOs;
using VinhKhanh.BackendApi.Mappings;
using VinhKhanh.BackendApi.Repositories.Interfaces;

namespace VinhKhanh.BackendApi.Application.Interfaces
{
    public interface IAuthService
    {
        Task<TokenResponseDto?> LoginAsync(AdminLoginRequestDto request, CancellationToken cancellationToken);
        Task<TokenResponseDto?> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
        Task LogoutAsync(string refreshToken, CancellationToken cancellationToken);
    }

    public interface ISettingsService
    {
        Task<MobileSettingsDto> GetMobileSettingsAsync(CancellationToken cancellationToken);
    }

    public interface IAnalyticsService
    {
        Task TrackViewAsync(string poiId, TrackPoiViewRequestDto request, CancellationToken cancellationToken);
        Task TrackAudioPlayAsync(string poiId, TrackPoiAudioRequestDto request, CancellationToken cancellationToken);
        Task<AnalyticsOverviewDto> GetOverviewAsync(string languageCode, CancellationToken cancellationToken);
    }

    public interface IPoiService
    {
        Task<PagedResult<PoiSummaryDto>> GetPoisAsync(PoiSearchCriteria criteria, CancellationToken cancellationToken);
        Task<IReadOnlyList<PoiSummaryDto>> GetNearbyPoisAsync(string languageCode, double latitude, double longitude, double radiusMeters, CancellationToken cancellationToken);
        Task<PoiDetailDto?> GetPoiByIdAsync(string poiId, string languageCode, CancellationToken cancellationToken);
        Task<PoiDetailDto?> GetPoiBySlugAsync(string slug, string languageCode, CancellationToken cancellationToken);
        Task<PoiDetailDto?> GetPoiByQrCodeAsync(string qrCode, string languageCode, CancellationToken cancellationToken);
        Task<IReadOnlyList<TourRouteDto>> GetFeaturedRoutesAsync(string languageCode, CancellationToken cancellationToken);
        Task<PoiDetailDto> SavePoiAsync(string? poiId, AdminPoiUpsertRequestDto request, CancellationToken cancellationToken);
        Task<PoiNarrationDto> SaveTranslationAsync(string poiId, AdminPoiTranslationUpsertRequestDto request, CancellationToken cancellationToken);
        Task<PoiNarrationDto> SaveAudioGuideAsync(string poiId, AdminPoiAudioUpsertRequestDto request, CancellationToken cancellationToken);
        Task<FoodItemDto> SaveFoodItemAsync(string poiId, string? foodItemId, AdminFoodItemUpsertRequestDto request, CancellationToken cancellationToken);
        Task<MediaAssetDto> SaveMediaAssetAsync(string poiId, string? mediaAssetId, AdminMediaAssetUpsertRequestDto request, CancellationToken cancellationToken);
        Task<bool> DeletePoiAsync(string poiId, CancellationToken cancellationToken);
        Task<bool> DeleteTranslationAsync(string poiId, string languageCode, CancellationToken cancellationToken);
        Task<bool> DeleteAudioGuideAsync(string poiId, string languageCode, CancellationToken cancellationToken);
        Task<bool> DeleteFoodItemAsync(string poiId, string foodItemId, CancellationToken cancellationToken);
        Task<bool> DeleteMediaAssetAsync(string poiId, string mediaAssetId, CancellationToken cancellationToken);
    }
}

namespace VinhKhanh.BackendApi.Application.Services
{
    using VinhKhanh.BackendApi.Application.Interfaces;

    public sealed class SettingsService(IConfiguration configuration, IPoiRepository poiRepository) : ISettingsService
    {
        public async Task<MobileSettingsDto> GetMobileSettingsAsync(CancellationToken cancellationToken)
        {
            var setting = await poiRepository.GetSettingsAsync(cancellationToken)
                ?? throw new InvalidOperationException("Khong tim thay SystemSettings.");

            var languages = setting.Languages
                .Select(item => new LanguageOptionDto(
                    item.LanguageCode,
                    item.LanguageCode switch
                    {
                        "vi" => "Tiếng Việt",
                        "en" => "English",
                        "zh-CN" => "中文",
                        "ko" => "한국어",
                        "ja" => "日本語",
                        _ => item.LanguageCode
                    },
                    item.LanguageType.Equals("premium", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(item => item.IsPremium)
                .ThenBy(item => item.Code)
                .ToList();

            var cooldownMinutes = configuration.GetValue<int?>("PublicExperience:GeofenceCooldownMinutes") ?? 20;

            return new MobileSettingsDto(
                setting.AppName,
                setting.SupportEmail,
                setting.DefaultLanguage,
                setting.FallbackLanguage,
                setting.MapProvider,
                setting.TtsProvider,
                setting.GeofenceRadiusMeters,
                cooldownMinutes,
                languages);
        }
    }

    public sealed class AuthService(
        IAuthRepository authRepository,
        IOptions<JwtOptions> jwtOptions,
        TimeProvider timeProvider) : IAuthService
    {
        private readonly JwtOptions _jwtOptions = jwtOptions.Value;

        public async Task<TokenResponseDto?> LoginAsync(AdminLoginRequestDto request, CancellationToken cancellationToken)
        {
            var user = await authRepository.GetAdminByEmailAsync(request.Email, cancellationToken);
            if (user is null ||
                !user.Status.Equals("active", StringComparison.OrdinalIgnoreCase) ||
                !user.Password.Equals(request.Password, StringComparison.Ordinal))
            {
                return null;
            }

            var now = timeProvider.GetUtcNow();
            await authRepository.UpdateLastLoginAsync(user.Id, now, cancellationToken);
            return await BuildTokenResponseAsync(user, now, cancellationToken);
        }

        public async Task<TokenResponseDto?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        {
            var session = await authRepository.GetRefreshSessionAsync(refreshToken, cancellationToken);
            if (session?.User is null || session.ExpiresAt <= timeProvider.GetUtcNow())
            {
                return null;
            }

            return await BuildTokenResponseAsync(session.User, timeProvider.GetUtcNow(), cancellationToken, refreshToken);
        }

        public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
            => authRepository.DeleteRefreshSessionAsync(refreshToken, cancellationToken);

        private async Task<TokenResponseDto> BuildTokenResponseAsync(
            GuideAdminUser user,
            DateTimeOffset now,
            CancellationToken cancellationToken,
            string? existingRefreshToken = null)
        {
            var accessExpiresAt = now.AddMinutes(_jwtOptions.AccessTokenMinutes);
            var refreshToken = existingRefreshToken ?? Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Guid.NewGuid().ToString("N");

            var signingCredentials = new SigningCredentials(
                new SymmetricSecurityKey(_jwtOptions.GetSigningKeyBytes()),
                SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id),
                new(ClaimTypes.NameIdentifier, user.Id),
                new(JwtRegisteredClaimNames.Email, user.Email),
                new(ClaimTypes.Name, user.Name),
                new(ClaimTypes.Role, user.Role),
                new("managed_poi_id", user.ManagedPoiId ?? string.Empty)
            };

            var token = new JwtSecurityToken(
                issuer: _jwtOptions.Issuer,
                audience: _jwtOptions.Audience,
                claims: claims,
                notBefore: now.UtcDateTime,
                expires: accessExpiresAt.UtcDateTime,
                signingCredentials: signingCredentials);

            await authRepository.SaveRefreshSessionAsync(new GuideRefreshSession
            {
                RefreshToken = refreshToken,
                UserId = user.Id,
                ExpiresAt = now.AddDays(_jwtOptions.RefreshTokenDays)
            }, cancellationToken);

            return new TokenResponseDto(
                user.Id,
                user.Name,
                user.Email,
                user.Role,
                user.ManagedPoiId,
                new JwtSecurityTokenHandler().WriteToken(token),
                refreshToken,
                accessExpiresAt);
        }
    }

    public sealed class AnalyticsService(
        IAnalyticsRepository analyticsRepository,
        IPoiRepository poiRepository,
        ISettingsService settingsService) : IAnalyticsService
    {
        public async Task TrackViewAsync(string poiId, TrackPoiViewRequestDto request, CancellationToken cancellationToken)
        {
            await analyticsRepository.AddViewAsync(new GuideViewLog
            {
                Id = $"view-{Guid.NewGuid():N}"[..18],
                PoiId = poiId,
                LanguageCode = request.LanguageCode,
                DeviceType = request.DeviceType,
                ViewedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
        }

        public async Task TrackAudioPlayAsync(string poiId, TrackPoiAudioRequestDto request, CancellationToken cancellationToken)
        {
            await analyticsRepository.AddAudioPlayAsync(new GuideAudioListenLog
            {
                Id = $"listen-{Guid.NewGuid():N}"[..18],
                PoiId = poiId,
                LanguageCode = request.LanguageCode,
                DurationInSeconds = request.DurationInSeconds,
                ListenedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
        }

        public async Task<AnalyticsOverviewDto> GetOverviewAsync(string languageCode, CancellationToken cancellationToken)
        {
            var settings = await settingsService.GetMobileSettingsAsync(cancellationToken);
            var views = await analyticsRepository.GetViewLogsAsync(cancellationToken);
            var audioPlays = await analyticsRepository.GetAudioListenLogsAsync(cancellationToken);
            var poiPage = await poiRepository.SearchAsync(new PoiSearchCriteria(languageCode, null, null, null, null, null, 1, 100), cancellationToken);

            var topPois = poiPage.Items
                .Select(poi => new PoiTrafficDto(
                    poi.Id,
                    poi.SelectTranslation(languageCode, settings.FallbackLanguage)?.Title ?? poi.Slug,
                    views.Count(item => item.PoiId == poi.Id),
                    audioPlays.Count(item => item.PoiId == poi.Id)))
                .OrderByDescending(item => item.Views + item.AudioPlays)
                .Take(5)
                .ToList();

            return new AnalyticsOverviewDto(
                poiPage.TotalItems,
                views.Count,
                audioPlays.Count,
                topPois);
        }
    }

    public sealed class PoiService(
        IConfiguration configuration,
        IPoiRepository poiRepository,
        ISettingsService settingsService) : IPoiService
    {
        public async Task<PagedResult<PoiSummaryDto>> GetPoisAsync(PoiSearchCriteria criteria, CancellationToken cancellationToken)
        {
            var settings = await settingsService.GetMobileSettingsAsync(cancellationToken);
            var result = await poiRepository.SearchAsync(criteria, cancellationToken);
            var items = result.Items
                .Select(item => item.ToSummaryDto(criteria.LanguageCode, settings.FallbackLanguage, GetDeepLinkScheme()))
                .ToList();

            return new PagedResult<PoiSummaryDto>(
                items,
                result.Page,
                result.PageSize,
                result.TotalItems,
                result.TotalPages);
        }

        public async Task<IReadOnlyList<PoiSummaryDto>> GetNearbyPoisAsync(
            string languageCode,
            double latitude,
            double longitude,
            double radiusMeters,
            CancellationToken cancellationToken)
        {
            var settings = await settingsService.GetMobileSettingsAsync(cancellationToken);
            var pois = await poiRepository.GetNearbyAsync(latitude, longitude, radiusMeters, cancellationToken);

            return pois
                .Select(item =>
                {
                    var distance = GuideMappingExtensions.CalculateDistanceMeters(
                        latitude,
                        longitude,
                        Convert.ToDouble(item.Latitude),
                        Convert.ToDouble(item.Longitude));

                    return item.ToSummaryDto(languageCode, settings.FallbackLanguage, GetDeepLinkScheme(), distance);
                })
                .OrderBy(item => item.DistanceMeters)
                .ToList();
        }

        public async Task<PoiDetailDto?> GetPoiByIdAsync(string poiId, string languageCode, CancellationToken cancellationToken)
        {
            var settings = await settingsService.GetMobileSettingsAsync(cancellationToken);
            var poi = await poiRepository.GetByIdAsync(poiId, cancellationToken);
            if (poi is null)
            {
                return null;
            }

            var routes = await poiRepository.GetFeaturedRoutesAsync(cancellationToken);
            return poi.ToDetailDto(languageCode, settings.FallbackLanguage, GetDeepLinkScheme(), routes);
        }

        public async Task<PoiDetailDto?> GetPoiBySlugAsync(string slug, string languageCode, CancellationToken cancellationToken)
        {
            var settings = await settingsService.GetMobileSettingsAsync(cancellationToken);
            var poi = await poiRepository.GetBySlugAsync(slug, cancellationToken);
            if (poi is null)
            {
                return null;
            }

            var routes = await poiRepository.GetFeaturedRoutesAsync(cancellationToken);
            return poi.ToDetailDto(languageCode, settings.FallbackLanguage, GetDeepLinkScheme(), routes);
        }

        public async Task<PoiDetailDto?> GetPoiByQrCodeAsync(string qrCode, string languageCode, CancellationToken cancellationToken)
        {
            var settings = await settingsService.GetMobileSettingsAsync(cancellationToken);
            var poi = await poiRepository.GetByQrCodeAsync(qrCode, cancellationToken);
            if (poi is null)
            {
                return null;
            }

            var routes = await poiRepository.GetFeaturedRoutesAsync(cancellationToken);
            return poi.ToDetailDto(languageCode, settings.FallbackLanguage, GetDeepLinkScheme(), routes);
        }

        public async Task<IReadOnlyList<TourRouteDto>> GetFeaturedRoutesAsync(string languageCode, CancellationToken cancellationToken)
        {
            var settings = await settingsService.GetMobileSettingsAsync(cancellationToken);
            var routes = await poiRepository.GetFeaturedRoutesAsync(cancellationToken);

            return routes.Select(route => new TourRouteDto(
                    route.Id,
                    route.Name,
                    route.Description,
                    route.DurationMinutes,
                    route.Difficulty,
                    route.IsFeatured,
                    route.Stops
                        .OrderBy(item => item.StopOrder)
                        .Select(stop => new TourRouteStopDto(
                            stop.PoiId,
                            stop.Poi?.SelectTranslation(languageCode, settings.FallbackLanguage)?.Title ?? stop.Poi?.Slug ?? stop.PoiId,
                            Convert.ToDouble(stop.Poi?.Latitude ?? 0),
                            Convert.ToDouble(stop.Poi?.Longitude ?? 0),
                            stop.StopOrder))
                        .ToList()))
                .ToList();
        }

        public async Task<PoiDetailDto> SavePoiAsync(string? poiId, AdminPoiUpsertRequestDto request, CancellationToken cancellationToken)
        {
            var settings = await settingsService.GetMobileSettingsAsync(cancellationToken);
            var poi = await poiRepository.SavePoiAsync(poiId, request, cancellationToken);
            var routes = await poiRepository.GetFeaturedRoutesAsync(cancellationToken);
            return poi.ToDetailDto(request.DefaultLanguageCode, settings.FallbackLanguage, GetDeepLinkScheme(), routes);
        }

        public async Task<PoiNarrationDto> SaveTranslationAsync(string poiId, AdminPoiTranslationUpsertRequestDto request, CancellationToken cancellationToken)
        {
            var translation = await poiRepository.SaveTranslationAsync(poiId, request, cancellationToken);
            return new PoiNarrationDto(
                translation.LanguageCode,
                translation.Title,
                translation.ShortText,
                translation.FullText,
                translation.FullText,
                null,
                "standard",
                true);
        }

        public async Task<PoiNarrationDto> SaveAudioGuideAsync(string poiId, AdminPoiAudioUpsertRequestDto request, CancellationToken cancellationToken)
        {
            var audioGuide = await poiRepository.SaveAudioGuideAsync(poiId, request, cancellationToken);
            var poi = await poiRepository.GetByIdAsync(poiId, cancellationToken)
                ?? throw new InvalidOperationException("Khong tim thay POI.");
            var settings = await settingsService.GetMobileSettingsAsync(cancellationToken);
            var translation = poi.SelectTranslation(request.LanguageCode, settings.FallbackLanguage);

            return new PoiNarrationDto(
                request.LanguageCode,
                translation?.Title ?? poi.Slug,
                translation?.ShortText ?? string.Empty,
                translation?.FullText ?? string.Empty,
                translation?.FullText ?? string.Empty,
                string.IsNullOrWhiteSpace(audioGuide.AudioUrl) ? null : audioGuide.AudioUrl,
                audioGuide.VoiceType,
                string.IsNullOrWhiteSpace(audioGuide.AudioUrl));
        }

        public async Task<FoodItemDto> SaveFoodItemAsync(string poiId, string? foodItemId, AdminFoodItemUpsertRequestDto request, CancellationToken cancellationToken)
        {
            var item = await poiRepository.SaveFoodItemAsync(poiId, foodItemId, request, cancellationToken);
            return new FoodItemDto(item.Id, item.Name, item.Description, item.PriceRange, item.ImageUrl, item.SpicyLevel);
        }

        public async Task<MediaAssetDto> SaveMediaAssetAsync(string poiId, string? mediaAssetId, AdminMediaAssetUpsertRequestDto request, CancellationToken cancellationToken)
        {
            var item = await poiRepository.SaveMediaAssetAsync(poiId, mediaAssetId, request, cancellationToken);
            return new MediaAssetDto(item.Id, item.MediaType, item.Url, item.AltText);
        }

        public Task<bool> DeletePoiAsync(string poiId, CancellationToken cancellationToken) => poiRepository.DeletePoiAsync(poiId, cancellationToken);
        public Task<bool> DeleteTranslationAsync(string poiId, string languageCode, CancellationToken cancellationToken) => poiRepository.DeleteTranslationAsync(poiId, languageCode, cancellationToken);
        public Task<bool> DeleteAudioGuideAsync(string poiId, string languageCode, CancellationToken cancellationToken) => poiRepository.DeleteAudioGuideAsync(poiId, languageCode, cancellationToken);
        public Task<bool> DeleteFoodItemAsync(string poiId, string foodItemId, CancellationToken cancellationToken) => poiRepository.DeleteFoodItemAsync(poiId, foodItemId, cancellationToken);
        public Task<bool> DeleteMediaAssetAsync(string poiId, string mediaAssetId, CancellationToken cancellationToken) => poiRepository.DeleteMediaAssetAsync(poiId, mediaAssetId, cancellationToken);

        private string GetDeepLinkScheme() => configuration.GetValue<string>("PublicExperience:DeepLinkScheme") ?? "vinhkhanhfoodguide";
    }
}
