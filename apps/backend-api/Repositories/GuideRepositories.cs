using Microsoft.EntityFrameworkCore;
using VinhKhanh.BackendApi.Domain.Entities;
using VinhKhanh.BackendApi.DTOs;
using VinhKhanh.BackendApi.Infrastructure.Persistence;
using VinhKhanh.BackendApi.Mappings;

namespace VinhKhanh.BackendApi.Repositories.Interfaces
{
    public interface IAuthRepository
    {
        Task<GuideAdminUser?> GetAdminByEmailAsync(string email, CancellationToken cancellationToken);
        Task<GuideAdminUser?> GetAdminByIdAsync(string userId, CancellationToken cancellationToken);
        Task SaveRefreshSessionAsync(GuideRefreshSession session, CancellationToken cancellationToken);
        Task<GuideRefreshSession?> GetRefreshSessionAsync(string refreshToken, CancellationToken cancellationToken);
        Task DeleteRefreshSessionAsync(string refreshToken, CancellationToken cancellationToken);
        Task UpdateLastLoginAsync(string userId, DateTimeOffset lastLoginAt, CancellationToken cancellationToken);
    }

    public interface IPoiRepository
    {
        Task<PagedResult<GuidePoi>> SearchAsync(PoiSearchCriteria criteria, CancellationToken cancellationToken);
        Task<GuidePoi?> GetByIdAsync(string poiId, CancellationToken cancellationToken);
        Task<GuidePoi?> GetBySlugAsync(string slug, CancellationToken cancellationToken);
        Task<GuidePoi?> GetByQrCodeAsync(string qrCode, CancellationToken cancellationToken);
        Task<IReadOnlyList<GuidePoi>> GetNearbyAsync(double latitude, double longitude, double radiusMeters, CancellationToken cancellationToken);
        Task<IReadOnlyList<GuideTourRoute>> GetFeaturedRoutesAsync(CancellationToken cancellationToken);
        Task<GuideSystemSetting?> GetSettingsAsync(CancellationToken cancellationToken);
        Task<GuidePoi> SavePoiAsync(string? poiId, AdminPoiUpsertRequestDto request, CancellationToken cancellationToken);
        Task<GuidePoiTranslation> SaveTranslationAsync(string poiId, AdminPoiTranslationUpsertRequestDto request, CancellationToken cancellationToken);
        Task<GuidePoiAudioGuide> SaveAudioGuideAsync(string poiId, AdminPoiAudioUpsertRequestDto request, CancellationToken cancellationToken);
        Task<GuideFoodItem> SaveFoodItemAsync(string poiId, string? foodItemId, AdminFoodItemUpsertRequestDto request, CancellationToken cancellationToken);
        Task<GuideMediaAsset> SaveMediaAssetAsync(string poiId, string? mediaAssetId, AdminMediaAssetUpsertRequestDto request, CancellationToken cancellationToken);
        Task<bool> DeletePoiAsync(string poiId, CancellationToken cancellationToken);
        Task<bool> DeleteTranslationAsync(string poiId, string languageCode, CancellationToken cancellationToken);
        Task<bool> DeleteAudioGuideAsync(string poiId, string languageCode, CancellationToken cancellationToken);
        Task<bool> DeleteFoodItemAsync(string poiId, string foodItemId, CancellationToken cancellationToken);
        Task<bool> DeleteMediaAssetAsync(string poiId, string mediaAssetId, CancellationToken cancellationToken);
    }

    public interface IAnalyticsRepository
    {
        Task AddViewAsync(GuideViewLog viewLog, CancellationToken cancellationToken);
        Task AddAudioPlayAsync(GuideAudioListenLog audioListenLog, CancellationToken cancellationToken);
        Task<IReadOnlyList<GuideViewLog>> GetViewLogsAsync(CancellationToken cancellationToken);
        Task<IReadOnlyList<GuideAudioListenLog>> GetAudioListenLogsAsync(CancellationToken cancellationToken);
    }
}

namespace VinhKhanh.BackendApi.Repositories
{
using VinhKhanh.BackendApi.Repositories.Interfaces;

public sealed class AuthRepository(VinhKhanhFoodGuideDbContext dbContext) : IAuthRepository
{
    public Task<GuideAdminUser?> GetAdminByEmailAsync(string email, CancellationToken cancellationToken)
    {
        return dbContext.AdminUsers.AsNoTracking().FirstOrDefaultAsync(item => item.Email == email, cancellationToken);
    }

    public Task<GuideAdminUser?> GetAdminByIdAsync(string userId, CancellationToken cancellationToken)
    {
        return dbContext.AdminUsers.AsNoTracking().FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
    }

    public async Task SaveRefreshSessionAsync(GuideRefreshSession session, CancellationToken cancellationToken)
    {
        var existing = await dbContext.RefreshSessions.FirstOrDefaultAsync(item => item.RefreshToken == session.RefreshToken, cancellationToken);
        if (existing is null)
        {
            await dbContext.RefreshSessions.AddAsync(session, cancellationToken);
        }
        else
        {
            existing.UserId = session.UserId;
            existing.ExpiresAt = session.ExpiresAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<GuideRefreshSession?> GetRefreshSessionAsync(string refreshToken, CancellationToken cancellationToken)
    {
        return dbContext.RefreshSessions
            .AsNoTracking()
            .Include(item => item.User)
            .FirstOrDefaultAsync(item => item.RefreshToken == refreshToken, cancellationToken);
    }

    public async Task DeleteRefreshSessionAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var session = await dbContext.RefreshSessions.FirstOrDefaultAsync(item => item.RefreshToken == refreshToken, cancellationToken);
        if (session is null)
        {
            return;
        }

        dbContext.RefreshSessions.Remove(session);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateLastLoginAsync(string userId, DateTimeOffset lastLoginAt, CancellationToken cancellationToken)
    {
        var user = await dbContext.AdminUsers.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null)
        {
            return;
        }

        user.LastLoginAt = lastLoginAt;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class PoiRepository(VinhKhanhFoodGuideDbContext dbContext) : IPoiRepository
{
    public async Task<PagedResult<GuidePoi>> SearchAsync(PoiSearchCriteria criteria, CancellationToken cancellationToken)
    {
        var query = BuildPoiQuery();

        if (!string.IsNullOrWhiteSpace(criteria.CategoryId))
        {
            query = query.Where(item => item.CategoryId == criteria.CategoryId);
        }

        if (criteria.Featured.HasValue)
        {
            query = query.Where(item => item.IsFeatured == criteria.Featured.Value);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Area))
        {
            query = query.Where(item => item.District.Contains(criteria.Area) || item.Ward.Contains(criteria.Area));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Dish))
        {
            query = query.Where(item => item.FoodItems.Any(food =>
                food.Name.Contains(criteria.Dish) ||
                food.Description.Contains(criteria.Dish)));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            query = query.Where(item =>
                item.Slug.Contains(criteria.Search) ||
                item.AddressLine.Contains(criteria.Search) ||
                item.Translations.Any(translation =>
                    translation.Title.Contains(criteria.Search) ||
                    translation.ShortText.Contains(criteria.Search) ||
                    translation.FullText.Contains(criteria.Search)) ||
                item.Tags.Any(tag => tag.TagValue.Contains(criteria.Search)));
        }

        query = query.Where(item => item.Status == "published");

        var page = criteria.Page <= 0 ? 1 : criteria.Page;
        var pageSize = criteria.PageSize <= 0 ? 20 : criteria.PageSize;
        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await query
            .OrderByDescending(item => item.IsFeatured)
            .ThenByDescending(item => item.PopularityScore)
            .ThenBy(item => item.Slug)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<GuidePoi>(items, page, pageSize, totalItems, totalPages);
    }

    public Task<GuidePoi?> GetByIdAsync(string poiId, CancellationToken cancellationToken)
        => BuildPoiQuery().FirstOrDefaultAsync(item => item.Id == poiId, cancellationToken);

    public Task<GuidePoi?> GetBySlugAsync(string slug, CancellationToken cancellationToken)
        => BuildPoiQuery().FirstOrDefaultAsync(item => item.Slug == slug, cancellationToken);

    public Task<GuidePoi?> GetByQrCodeAsync(string qrCode, CancellationToken cancellationToken)
        => BuildPoiQuery().FirstOrDefaultAsync(item => item.QrCode == qrCode || item.Slug == qrCode, cancellationToken);

    public async Task<IReadOnlyList<GuidePoi>> GetNearbyAsync(double latitude, double longitude, double radiusMeters, CancellationToken cancellationToken)
    {
        var pois = await BuildPoiQuery()
            .Where(item => item.Status == "published")
            .ToListAsync(cancellationToken);

        return pois
            .Where(item =>
                GuideMappingExtensions.CalculateDistanceMeters(
                    latitude,
                    longitude,
                    Convert.ToDouble(item.Latitude),
                    Convert.ToDouble(item.Longitude)) <= radiusMeters)
            .OrderBy(item =>
                GuideMappingExtensions.CalculateDistanceMeters(
                    latitude,
                    longitude,
                    Convert.ToDouble(item.Latitude),
                    Convert.ToDouble(item.Longitude)))
            .ToList();
    }

    public async Task<IReadOnlyList<GuideTourRoute>> GetFeaturedRoutesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Routes
            .AsNoTracking()
            .Include(item => item.Stops)
                .ThenInclude(item => item.Poi)!
                    .ThenInclude(item => item!.Translations)
            .Where(item => item.IsFeatured)
            .OrderBy(item => item.DurationMinutes)
            .ToListAsync(cancellationToken);
    }

    public Task<GuideSystemSetting?> GetSettingsAsync(CancellationToken cancellationToken)
    {
        return dbContext.SystemSettings
            .AsNoTracking()
            .Include(item => item.Languages)
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<GuidePoi> SavePoiAsync(string? poiId, AdminPoiUpsertRequestDto request, CancellationToken cancellationToken)
    {
        var entity = poiId is null
            ? null
            : await dbContext.Pois.Include(item => item.Tags).FirstOrDefaultAsync(item => item.Id == poiId, cancellationToken);

        if (entity is null)
        {
            entity = new GuidePoi
            {
                Id = poiId ?? $"poi-{Guid.NewGuid():N}"[..18],
                CreatedAt = DateTimeOffset.UtcNow
            };
            await dbContext.Pois.AddAsync(entity, cancellationToken);
        }

        entity.Slug = request.Slug;
        entity.AddressLine = request.Address;
        entity.Latitude = Convert.ToDecimal(request.Latitude);
        entity.Longitude = Convert.ToDecimal(request.Longitude);
        entity.CategoryId = request.CategoryId;
        entity.Status = request.Status;
        entity.IsFeatured = request.IsFeatured;
        entity.DefaultLanguageCode = request.DefaultLanguageCode;
        entity.District = request.District;
        entity.Ward = request.Ward;
        entity.PriceRange = request.PriceRange;
        entity.AverageVisitDurationMinutes = request.AverageVisitDurationMinutes;
        entity.PopularityScore = request.PopularityScore;
        entity.UpdatedBy = request.UpdatedBy;
        entity.OwnerUserId = request.OwnerUserId;
        entity.QrCode = string.IsNullOrWhiteSpace(request.QrCode) ? request.Slug : request.QrCode;
        entity.OpeningHours = string.IsNullOrWhiteSpace(request.OpeningHours) ? "17:00 - 23:30" : request.OpeningHours;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        entity.Tags.Clear();
        foreach (var tag in request.Tags.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            entity.Tags.Add(new GuidePoiTag { PoiId = entity.Id, TagValue = tag.Trim() });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(entity.Id, cancellationToken)
            ?? throw new InvalidOperationException("Khong the tai lai POI vua luu.");
    }

    public async Task<GuidePoiTranslation> SaveTranslationAsync(string poiId, AdminPoiTranslationUpsertRequestDto request, CancellationToken cancellationToken)
    {
        var translation = await dbContext.PoiTranslations.FirstOrDefaultAsync(item =>
            item.EntityId == poiId &&
            item.EntityType == "poi" &&
            item.LanguageCode == request.LanguageCode, cancellationToken);

        if (translation is null)
        {
            translation = new GuidePoiTranslation
            {
                Id = $"trans-{Guid.NewGuid():N}"[..18],
                EntityId = poiId,
                EntityType = "poi"
            };
            await dbContext.PoiTranslations.AddAsync(translation, cancellationToken);
        }

        translation.LanguageCode = request.LanguageCode;
        translation.Title = request.Title;
        translation.ShortText = request.ShortText;
        translation.FullText = request.FullText;
        translation.SeoTitle = request.SeoTitle;
        translation.SeoDescription = request.SeoDescription;
        translation.IsPremium = request.IsPremium;
        translation.UpdatedBy = request.UpdatedBy;
        translation.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return translation;
    }

    public async Task<GuidePoiAudioGuide> SaveAudioGuideAsync(string poiId, AdminPoiAudioUpsertRequestDto request, CancellationToken cancellationToken)
    {
        var audioGuide = await dbContext.AudioGuides.FirstOrDefaultAsync(item =>
            item.EntityId == poiId &&
            item.EntityType == "poi" &&
            item.LanguageCode == request.LanguageCode, cancellationToken);

        if (audioGuide is null)
        {
            audioGuide = new GuidePoiAudioGuide
            {
                Id = $"audio-{Guid.NewGuid():N}"[..18],
                EntityId = poiId,
                EntityType = "poi"
            };
            await dbContext.AudioGuides.AddAsync(audioGuide, cancellationToken);
        }

        audioGuide.LanguageCode = request.LanguageCode;
        audioGuide.AudioUrl = request.AudioUrl;
        audioGuide.VoiceType = request.VoiceType;
        audioGuide.SourceType = request.SourceType;
        audioGuide.Status = request.Status;
        audioGuide.UpdatedBy = request.UpdatedBy;
        audioGuide.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return audioGuide;
    }

    public async Task<GuideFoodItem> SaveFoodItemAsync(string poiId, string? foodItemId, AdminFoodItemUpsertRequestDto request, CancellationToken cancellationToken)
    {
        var foodItem = foodItemId is null
            ? null
            : await dbContext.FoodItems.FirstOrDefaultAsync(item => item.Id == foodItemId && item.PoiId == poiId, cancellationToken);

        if (foodItem is null)
        {
            foodItem = new GuideFoodItem
            {
                Id = foodItemId ?? $"food-{Guid.NewGuid():N}"[..18],
                PoiId = poiId
            };
            await dbContext.FoodItems.AddAsync(foodItem, cancellationToken);
        }

        foodItem.Name = request.Name;
        foodItem.Description = request.Description;
        foodItem.PriceRange = request.PriceRange;
        foodItem.ImageUrl = request.ImageUrl;
        foodItem.SpicyLevel = request.SpicyLevel;

        await dbContext.SaveChangesAsync(cancellationToken);
        return foodItem;
    }

    public async Task<GuideMediaAsset> SaveMediaAssetAsync(string poiId, string? mediaAssetId, AdminMediaAssetUpsertRequestDto request, CancellationToken cancellationToken)
    {
        var media = mediaAssetId is null
            ? null
            : await dbContext.MediaAssets.FirstOrDefaultAsync(item => item.Id == mediaAssetId && item.EntityId == poiId, cancellationToken);

        if (media is null)
        {
            media = new GuideMediaAsset
            {
                Id = mediaAssetId ?? $"media-{Guid.NewGuid():N}"[..18],
                EntityId = poiId,
                EntityType = "poi",
                CreatedAt = DateTimeOffset.UtcNow
            };
            await dbContext.MediaAssets.AddAsync(media, cancellationToken);
        }

        media.MediaType = request.MediaType;
        media.Url = request.Url;
        media.AltText = request.AltText;

        await dbContext.SaveChangesAsync(cancellationToken);
        return media;
    }

    public async Task<bool> DeletePoiAsync(string poiId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Pois.FirstOrDefaultAsync(item => item.Id == poiId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        dbContext.Pois.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteTranslationAsync(string poiId, string languageCode, CancellationToken cancellationToken)
    {
        var entity = await dbContext.PoiTranslations.FirstOrDefaultAsync(item =>
            item.EntityId == poiId &&
            item.EntityType == "poi" &&
            item.LanguageCode == languageCode, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        dbContext.PoiTranslations.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAudioGuideAsync(string poiId, string languageCode, CancellationToken cancellationToken)
    {
        var entity = await dbContext.AudioGuides.FirstOrDefaultAsync(item =>
            item.EntityId == poiId &&
            item.EntityType == "poi" &&
            item.LanguageCode == languageCode, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        dbContext.AudioGuides.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteFoodItemAsync(string poiId, string foodItemId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.FoodItems.FirstOrDefaultAsync(item => item.Id == foodItemId && item.PoiId == poiId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        dbContext.FoodItems.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteMediaAssetAsync(string poiId, string mediaAssetId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.MediaAssets.FirstOrDefaultAsync(item => item.Id == mediaAssetId && item.EntityId == poiId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        dbContext.MediaAssets.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private IQueryable<GuidePoi> BuildPoiQuery()
    {
        return dbContext.Pois
            .AsNoTracking()
            .Include(item => item.Category)
            .Include(item => item.Tags)
            .Include(item => item.Translations)
            .Include(item => item.AudioGuides)
            .Include(item => item.MediaAssets)
            .Include(item => item.FoodItems);
    }
}

public sealed class AnalyticsRepository(VinhKhanhFoodGuideDbContext dbContext) : IAnalyticsRepository
{
    public async Task AddViewAsync(GuideViewLog viewLog, CancellationToken cancellationToken)
    {
        await dbContext.ViewLogs.AddAsync(viewLog, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAudioPlayAsync(GuideAudioListenLog audioListenLog, CancellationToken cancellationToken)
    {
        await dbContext.AudioListenLogs.AddAsync(audioListenLog, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GuideViewLog>> GetViewLogsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.ViewLogs.AsNoTracking().OrderByDescending(item => item.ViewedAt).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GuideAudioListenLog>> GetAudioListenLogsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.AudioListenLogs.AsNoTracking().OrderByDescending(item => item.ListenedAt).ToListAsync(cancellationToken);
    }
}
}
