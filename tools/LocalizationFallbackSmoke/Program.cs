using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;
using VinhKhanh.MobileApp.Helpers;

var failures = new List<string>();

void AssertEqual(string name, string actual, string expected)
{
    if (!string.Equals(actual, expected, StringComparison.Ordinal))
    {
        failures.Add($"{name}: expected '{expected}' but got '{actual}'");
    }
}

void AssertDoesNotContain(string name, IEnumerable<string> values, string forbiddenValue)
{
    if (values.Contains(forbiddenValue, StringComparer.OrdinalIgnoreCase))
    {
        failures.Add($"{name}: fallback candidates must not contain '{forbiddenValue}'");
    }
}

string ResolveText(IReadOnlyDictionary<string, string> values, string languageCode)
{
    foreach (var candidate in LocalizationFallbackPolicy.GetDisplayTextFallbackCandidates(languageCode))
    {
        if (values.TryGetValue(candidate, out var value) &&
            LocalizationFallbackPolicy.IsUsableTextForLanguage(value, candidate))
        {
            return value;
        }
    }

    return string.Empty;
}

Dictionary<string, string> LoadLocalization(string languageCode)
{
    var path = FindRepoFile(Path.Combine("apps", "mobile-app", "Resources", "Localization", $"{languageCode}.json"));
    using var stream = File.OpenRead(path);
    return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

string FindRepoFile(string relativePath)
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        var candidate = Path.Combine(current.FullName, relativePath);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        current = current.Parent;
    }

    throw new FileNotFoundException($"Could not find repo file '{relativePath}'.");
}

var english = LoadLocalization("en");
var missingFrenchPremiumTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["vi"] = "Premium",
    ["en"] = english["settings_premium_title"]
};

AssertEqual(
    "missing UI key falls back to English, not Vietnamese",
    ResolveText(missingFrenchPremiumTitle, "fr"),
    english["settings_premium_title"]);

var popupTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["vi"] = "Quán Ốc Vĩnh Khánh Signature",
    ["en"] = "Vinh Khanh Signature Snail House"
};
AssertEqual(
    "POI popup falls back to English",
    ResolveText(popupTexts, "zh-CN"),
    "Vinh Khanh Signature Snail House");

var mislabeledPoiText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["zh-CN"] = "Quảng Trường Ẩm thực BBQ Night",
    ["en"] = "BBQ Night Culinary Square",
    ["vi"] = "Quảng Trường Ẩm thực BBQ Night"
};
AssertEqual(
    "mislabeled non-Vietnamese POI text is skipped",
    ResolveText(mislabeledPoiText, "zh-CN"),
    "BBQ Night Culinary Square");

var vietnameseOnlyPoiText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["vi"] = "Quán ốc đặc trưng với thực đơn đa dạng"
};
AssertEqual(
    "missing POI translation does not fall back to Vietnamese",
    ResolveText(vietnameseOnlyPoiText, "en"),
    string.Empty);

var zhCandidates = LocalizationFallbackPolicy.GetDisplayTextFallbackCandidates("zh-CN");
AssertDoesNotContain("non-Vietnamese fallback chain", zhCandidates, "vi");
AssertEqual("persisted culture normalization", AppLanguage.NormalizeCode("en-US"), "en");
AssertEqual("POI narration candidate uses English fallback", ResolveText(popupTexts, "ko"), "Vinh Khanh Signature Snail House");
AssertEqual(
    "Vietnamese diacritics in mislabeled English text are rejected",
    LocalizationFallbackPolicy.IsUsableTextForLanguage("Combo BBQ nhóm 4 người", "en").ToString(),
    bool.FalseString);
AssertEqual(
    "Vietnamese promotion description in mislabeled Japanese text is rejected",
    LocalizationFallbackPolicy.IsUsableTextForLanguage("Tặng nước uống cho khách check-in POI trong khung giờ 18h-20h.", "ja").ToString(),
    bool.FalseString);

var translator = new FakeTranslationClient();
var bootstrapLocalization = new BootstrapLocalizationService(
    translator,
    NullLogger<BootstrapLocalizationService>.Instance);

var englishBootstrap = await bootstrapLocalization.ApplyAutoTranslationsAsync(
    CreateBootstrap(
        isPremiumCustomer: false,
        new Translation
        {
            Id = "promo-1-en",
            EntityType = "promotion",
            EntityId = "promo-1",
            LanguageCode = "en",
            Title = "Combo BBQ nhóm 4 người",
            FullText = "Tặng nước uống cho khách check-in POI trong khung giờ 18h-20h."
        }),
    "en",
    CancellationToken.None);
var englishPromotionTranslation = englishBootstrap.Translations.Single(item =>
    item.EntityType == "promotion" &&
    item.EntityId == "promo-1" &&
    item.LanguageCode == "en");
AssertEqual(
    "bootstrap auto-translation replaces mislabeled English promotion title",
    englishPromotionTranslation.Title,
    "en translation 1");
AssertEqual(
    "bootstrap auto-translation replaces mislabeled English promotion description",
    englishPromotionTranslation.FullText,
    "en translation 2");

foreach (var targetLanguageCode in new[] { "zh-CN", "ko", "ja" })
{
    var localizedBootstrap = await bootstrapLocalization.ApplyAutoTranslationsAsync(
        CreateBootstrap(isPremiumCustomer: true),
        targetLanguageCode,
        CancellationToken.None);
    var promotionTranslation = localizedBootstrap.Translations.Single(item =>
        item.EntityType == "promotion" &&
        item.EntityId == "promo-1" &&
        item.LanguageCode == targetLanguageCode);
    AssertEqual(
        $"bootstrap auto-translation creates {targetLanguageCode} promotion title",
        promotionTranslation.Title,
        $"{targetLanguageCode} translation 1");
    AssertEqual(
        $"bootstrap auto-translation creates {targetLanguageCode} promotion description",
        promotionTranslation.FullText,
        $"{targetLanguageCode} translation 2");
}

var lockedBootstrap = await bootstrapLocalization.ApplyAutoTranslationsAsync(
    CreateBootstrap(isPremiumCustomer: false),
    "zh-CN",
    CancellationToken.None);
if (lockedBootstrap.Translations.Any(item => item.LanguageCode == "zh-CN"))
{
    failures.Add("locked premium language must not be auto-translated for non-premium customer");
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("LocalizationFallbackSmoke failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    Environment.Exit(1);
}

Console.WriteLine("LocalizationFallbackSmoke passed.");

AdminBootstrapResponse CreateBootstrap(bool isPremiumCustomer, params Translation[] translations)
{
    var settings = new SystemSetting
    {
        DefaultLanguage = "vi",
        FallbackLanguage = "en",
        FreeLanguages = ["vi", "en"],
        PremiumLanguages = ["zh-CN", "ko", "ja"],
        PremiumUnlockPriceUsd = 10
    };

    return new AdminBootstrapResponse(
        Users: [],
        CustomerUsers:
        [
            new CustomerUser
            {
                Id = "customer-1",
                IsPremium = isPremiumCustomer,
                PreferredLanguage = isPremiumCustomer ? "zh-CN" : "en"
            }
        ],
        Categories: [],
        Pois:
        [
            new Poi
            {
                Id = "poi-1",
                Slug = "quan-bbq",
                Status = "published",
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ],
        Translations: translations.ToList(),
        AudioGuides: [],
        MediaAssets: [],
        FoodItems: [],
        Routes: [],
        Promotions:
        [
            new Promotion
            {
                Id = "promo-1",
                PoiId = "poi-1",
                Title = "Combo BBQ nhóm 4 người",
                Description = "Tặng nước uống cho khách check-in POI trong khung giờ 18h-20h.",
                Status = "active",
                StartAt = new DateTimeOffset(2026, 3, 18, 0, 0, 0, TimeSpan.Zero),
                EndAt = new DateTimeOffset(2026, 3, 25, 0, 0, 0, TimeSpan.Zero)
            }
        ],
        Reviews: [],
        ViewLogs: [],
        AudioListenLogs: [],
        AuditLogs: [],
        Settings: settings,
        SyncState: null);
}

sealed class FakeTranslationClient : ITextTranslationClient
{
    public Task<TextTranslationResponse> TranslateAsync(
        TextTranslationRequest request,
        CancellationToken cancellationToken)
    {
        var texts = request.Texts
            .Select((_, index) => $"{request.TargetLanguageCode} translation {index + 1}")
            .ToList();

        return Task.FromResult(new TextTranslationResponse(
            request.TargetLanguageCode,
            request.SourceLanguageCode,
            texts,
            "fake"));
    }
}
