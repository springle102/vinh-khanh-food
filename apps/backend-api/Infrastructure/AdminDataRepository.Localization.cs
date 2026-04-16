using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private TranslationSourceSnapshot? ResolveTranslationSourceSnapshot(
        SqlConnection connection,
        SqlTransaction? transaction,
        string entityType,
        string entityId,
        string defaultLanguageCode,
        string fallbackLanguageCode,
        Translation? pendingTranslation = null)
    {
        var normalizedEntityType = NormalizeEntityType(entityType);
        var translations = GetTranslations(connection, transaction)
            .Where(item =>
                string.Equals(NormalizeEntityType(item.EntityType), normalizedEntityType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.EntityId, entityId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pendingTranslation is not null)
        {
            translations.RemoveAll(item => string.Equals(item.Id, pendingTranslation.Id, StringComparison.OrdinalIgnoreCase));
            translations.RemoveAll(item =>
                string.Equals(NormalizeEntityType(item.EntityType), normalizedEntityType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.EntityId, entityId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    PremiumAccessCatalog.NormalizeLanguageCode(item.LanguageCode),
                    PremiumAccessCatalog.NormalizeLanguageCode(pendingTranslation.LanguageCode),
                    StringComparison.OrdinalIgnoreCase));
            translations.Add(pendingTranslation);
        }

        var fallback = ResolveTranslationEntityFallback(connection, transaction, normalizedEntityType, entityId);
        return TranslationSourceVersioning.ResolveCurrentSource(
            normalizedEntityType,
            entityId,
            translations,
            defaultLanguageCode,
            fallbackLanguageCode,
            fallback.Title,
            fallback.ShortText,
            fallback.FullText,
            fallback.UpdatedAt);
    }

    private (string? Title, string? ShortText, string? FullText, DateTimeOffset UpdatedAt) ResolveTranslationEntityFallback(
        SqlConnection connection,
        SqlTransaction? transaction,
        string entityType,
        string entityId)
    {
        switch (NormalizeEntityType(entityType))
        {
            case "poi":
            {
                var poi = GetPoiById(connection, transaction, entityId);
                return (
                    poi?.Slug,
                    null,
                    null,
                    poi?.UpdatedAt ?? DateTimeOffset.MinValue);
            }
            case "food_item":
            {
                var foodItem = GetFoodItemById(connection, transaction, entityId);
                return (
                    foodItem?.Name,
                    null,
                    foodItem?.Description,
                    DateTimeOffset.MinValue);
            }
            case "promotion":
            {
                var promotion = GetPromotionById(connection, transaction, entityId);
                return (
                    promotion?.Title,
                    null,
                    promotion?.Description,
                    DateTimeOffset.MinValue);
            }
            case "route":
            {
                var route = GetRouteById(connection, transaction, entityId);
                return (
                    route?.Name,
                    route?.Theme,
                    route?.Description,
                    route?.UpdatedAt ?? DateTimeOffset.MinValue);
            }
            default:
                return (null, null, null, DateTimeOffset.MinValue);
        }
    }

    private void InvalidateDependentTranslations(
        SqlConnection connection,
        SqlTransaction transaction,
        string entityType,
        string entityId,
        string sourceLanguageCode)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.PoiTranslations
            SET SourceLanguageCode = NULL,
                SourceHash = NULL,
                SourceUpdatedAt = NULL
            WHERE EntityId = ?
              AND (
                    EntityType = ? OR
                    (? = N'poi' AND EntityType = N'place')
              )
              AND LOWER(LTRIM(RTRIM(LanguageCode))) <> LOWER(LTRIM(RTRIM(?)));
            """,
            entityId,
            entityType,
            entityType,
            sourceLanguageCode);
    }
}
