using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Helpers;

namespace VinhKhanh.MobileApp.Services;

public interface IAppLanguageService
{
    string CurrentLanguage { get; }
    CultureInfo CurrentCulture { get; }
    bool HasSavedLanguageSelection { get; }
    IReadOnlyList<AppLanguageDefinition> SupportedLanguages { get; }
    event EventHandler? LanguageChanged;
    Task InitializeAsync();
    Task SetLanguageAsync(string languageCode);
    string GetText(string key);
    AppLanguageDefinition GetLanguageDefinition(string? languageCode);
}

public sealed class AppLanguageService : IAppLanguageService
{
    private const string PreferenceKey = "vkfood.language.code";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyDictionary<string, string> SeedEnglishTexts =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["app_title"] = "Vinh Khanh Food Street",
            ["brand_title"] = "Vinh Khanh\nFood Street",
            ["language_selection_title"] = "Choose your language",
            ["language_selection_subtitle"] = "Select a language before signing in",
            ["language_selection_pending_label"] = "QR",
            ["qr_success_title"] = "QR scan successful!",
            ["qr_choose_language"] = "Choose your language",
            ["qr_continue"] = "Continue",
            ["login_portal_subtitle"] = "Login / Sign Up",
            ["login_tab"] = "Login",
            ["signup_tab"] = "Sign Up",
            ["login_identifier_placeholder"] = "Email / Phone number",
            ["login_password_placeholder"] = "Password",
            ["login_forgot_password"] = "Forgot password?",
            ["login_button"] = "Login",
            ["signup_button"] = "Sign Up",
            ["login_google"] = "Continue with Google",
            ["login_facebook"] = "Facebook",
            ["login_apple"] = "Apple",
            ["login_create_account"] = "Create a new account",
            ["home_search_placeholder"] = "Search restaurants or dishes...",
            ["home_poi_chip"] = "POI",
            ["home_layer"] = "Layer",
            ["home_default_title"] = "Vinh Khanh Food Street",
            ["home_default_description"] = "Tap a marker or use the search bar to explore popular food spots.",
            ["home_default_address"] = "Khanh Hoi Ward, District 4, Ho Chi Minh City",
            ["tour_title"] = "Tour itinerary",
            ["tour_create"] = "Create New Tour",
            ["tour_checkpoints"] = "Visited checkpoints",
            ["settings_title"] = "Settings",
            ["settings_account"] = "Account",
            ["settings_language_title"] = "Language",
            ["settings_user_name"] = "User name",
            ["settings_contact"] = "Email / Phone number",
            ["settings_logout"] = "Log out",
            ["settings_notifications"] = "Notifications",
            ["settings_cards"] = "Card Management",
            ["settings_privacy"] = "Privacy",
            ["settings_support"] = "Support",
            ["bottom_qr"] = "Scan QR",
            ["bottom_settings"] = "Settings",
            ["bottom_poi"] = "Find POI",
            ["bottom_tour"] = "My Tour",
            ["poi_detail_listen"] = "Listen",
            ["poi_detail_directions"] = "Directions",
            ["poi_detail_save"] = "Save to tour",
            ["poi_detail_saved"] = "Saved",
            ["poi_detail_loading"] = "Loading details...",
            ["poi_detail_featured"] = "Featured",
            ["poi_detail_reviews"] = "reviews",
            ["poi_detail_no_selection"] = "Select a place on the map",
            ["poi_detail_address"] = "Address",
            ["poi_detail_price_range"] = "Price range",
            ["poi_detail_food_items"] = "Dishes",
            ["poi_detail_promotions"] = "Promotions",
            ["poi_detail_opening_hours"] = "Opening hours",
            ["poi_detail_tags"] = "Tags",
            ["poi_detail_no_food_items"] = "No dishes are available for this place yet.",
            ["poi_detail_no_promotions"] = "No promotions are available right now.",
            ["poi_detail_spicy_mild"] = "Mild",
            ["poi_detail_spicy_medium"] = "Medium",
            ["poi_detail_spicy_hot"] = "Hot",
            ["poi_detail_status_active"] = "Active",
            ["poi_detail_status_upcoming"] = "Upcoming",
            ["poi_detail_status_expired"] = "Expired",
            ["poi_detail_status_info"] = "Info",
            ["poi_detail_period_from"] = "From",
            ["poi_detail_period_until"] = "Until",
            ["qr_scanner_title"] = "Scan QR",
            ["qr_scanner_instruction"] = "Place the QR code inside the frame to choose a language and continue your experience.",
            ["qr_scanner_manual_title"] = "Enter code manually",
            ["qr_scanner_manual_description"] = "Use this for quick testing with a POI id, for example: poi-bbq-night",
            ["qr_camera_permission_title"] = "QR",
            ["qr_camera_permission_message"] = "Camera permission is required to scan QR codes.",
            ["common_ok"] = "OK",
            ["qr_manual_prompt_title"] = "QR code",
            ["qr_manual_prompt_message"] = "Enter a QR code or POI id for testing",
            ["qr_manual_prompt_accept"] = "Open",
            ["qr_manual_prompt_cancel"] = "Cancel",
            ["qr_manual_prompt_placeholder"] = "e.g. poi-bbq-night"
        };

    private readonly WeakEventManager _eventManager = new();
    private readonly ILogger<AppLanguageService>? _logger;
    private readonly HashSet<string> _missingKeyLogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _fallbackKeyLogs = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _fallbackTexts = new(SeedEnglishTexts, StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _currentTexts = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;

    public AppLanguageService(ILogger<AppLanguageService>? logger = null)
    {
        _logger = logger;
        CurrentLanguage = AppLanguage.NormalizeCode(Preferences.Default.Get(PreferenceKey, AppLanguage.DefaultLanguage));
        CurrentCulture = AppLanguage.CreateCulture(CurrentLanguage);
        HasSavedLanguageSelection = Preferences.Default.ContainsKey(PreferenceKey);
    }

    public string CurrentLanguage { get; private set; }

    public CultureInfo CurrentCulture { get; private set; }

    public bool HasSavedLanguageSelection { get; private set; }

    public IReadOnlyList<AppLanguageDefinition> SupportedLanguages => AppLanguage.SupportedLanguages;

    public event EventHandler? LanguageChanged
    {
        add => _eventManager.AddEventHandler(value);
        remove => _eventManager.RemoveEventHandler(value);
    }

    public async Task InitializeAsync()
    {
        await ApplyLanguageAsync(CurrentLanguage, persistSelection: false);
        _initialized = true;
    }

    public Task SetLanguageAsync(string languageCode)
        => ApplyLanguageAsync(languageCode, persistSelection: true);

    public string GetText(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (_currentTexts.TryGetValue(key, out var currentValue) && !string.IsNullOrWhiteSpace(currentValue))
        {
            return TextEncodingHelper.NormalizeDisplayText(currentValue);
        }

        if (_fallbackTexts.TryGetValue(key, out var fallbackValue) && !string.IsNullOrWhiteSpace(fallbackValue))
        {
            LogFallbackKey(key);
            return TextEncodingHelper.NormalizeDisplayText(fallbackValue);
        }

        LogMissingKey(key);
        return key;
    }

    public AppLanguageDefinition GetLanguageDefinition(string? languageCode)
        => AppLanguage.GetDefinition(languageCode);

    private async Task ApplyLanguageAsync(string languageCode, bool persistSelection)
    {
        var normalizedCode = AppLanguage.NormalizeCode(languageCode);
        var nextFallbackTexts = new Dictionary<string, string>(SeedEnglishTexts, StringComparer.OrdinalIgnoreCase);
        MergeTexts(nextFallbackTexts, await LoadFromFileAsync(AppLanguage.FallbackLanguage));

        var nextCurrentTexts = string.Equals(normalizedCode, AppLanguage.FallbackLanguage, StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, string>(nextFallbackTexts, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(normalizedCode, AppLanguage.FallbackLanguage, StringComparison.OrdinalIgnoreCase))
        {
            MergeTexts(nextCurrentTexts, await LoadFromFileAsync(normalizedCode));
        }

        if (nextCurrentTexts.Count == 0 && !string.Equals(normalizedCode, AppLanguage.FallbackLanguage, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning("No translation file found for language '{LanguageCode}'. UI will use English fallback.", normalizedCode);
        }

        _fallbackTexts = nextFallbackTexts;
        _currentTexts = nextCurrentTexts;
        _missingKeyLogs.Clear();
        _fallbackKeyLogs.Clear();

        CurrentLanguage = normalizedCode;
        CurrentCulture = AppLanguage.CreateCulture(normalizedCode);
        ApplyCurrentCulture(CurrentCulture);

        if (persistSelection)
        {
            Preferences.Default.Set(PreferenceKey, normalizedCode);
            HasSavedLanguageSelection = true;
        }
        else
        {
            HasSavedLanguageSelection = Preferences.Default.ContainsKey(PreferenceKey);
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
            _eventManager.HandleEvent(this, EventArgs.Empty, nameof(LanguageChanged)));
    }

    private void LogMissingKey(string key)
    {
        var token = $"{CurrentLanguage}:{key}";
        if (_missingKeyLogs.Add(token))
        {
            _logger?.LogWarning("Missing translation key '{Key}' for language '{LanguageCode}'.", key, CurrentLanguage);
        }
    }

    private void LogFallbackKey(string key)
    {
        if (!_initialized)
        {
            return;
        }

        var token = $"{CurrentLanguage}:{key}";
        if (_fallbackKeyLogs.Add(token))
        {
            _logger?.LogInformation(
                "Using English fallback for translation key '{Key}' in language '{LanguageCode}'.",
                key,
                CurrentLanguage);
        }
    }

    private static async Task<Dictionary<string, string>> LoadFromFileAsync(string languageCode)
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync($"Localization/{languageCode}.json");
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(content, JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            return values.ToDictionary(
                pair => pair.Key,
                pair => TextEncodingHelper.NormalizeDisplayText(pair.Value),
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void MergeTexts(IDictionary<string, string> destination, IReadOnlyDictionary<string, string> source)
    {
        foreach (var pair in source)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                destination[pair.Key] = TextEncodingHelper.NormalizeDisplayText(pair.Value);
            }
        }
    }

    private static void ApplyCurrentCulture(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
