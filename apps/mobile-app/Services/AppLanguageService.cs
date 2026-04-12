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
            ["language_status_ready"] = "Ready to use",
            ["language_status_premium"] = "Included in Premium",
            ["language_status_locked"] = "Upgrade to Premium to use this language",
            ["qr_success_title"] = "QR scan successful!",
            ["qr_choose_language"] = "Choose your language",
            ["qr_continue"] = "Continue",
            ["login_portal_subtitle"] = "Login / Sign Up",
            ["login_tab"] = "Login",
            ["signup_tab"] = "Sign Up",
            ["login_identifier_placeholder"] = "Username / Email / Phone number",
            ["signup_name_placeholder"] = "Full name",
            ["signup_username_placeholder"] = "Username",
            ["signup_email_placeholder"] = "Email",
            ["signup_phone_placeholder"] = "Phone number",
            ["login_password_placeholder"] = "Password",
            ["signup_confirm_password_placeholder"] = "Confirm password",
            ["login_forgot_password"] = "Forgot password?",
            ["login_identifier_not_found"] = "We couldn't find a customer profile matching that identifier.",
            ["login_validation_required"] = "Please enter your username, email, or phone number and password.",
            ["login_invalid_credentials"] = "The username, email, phone number, or password is incorrect.",
            ["login_backend_unavailable"] = "The app could not reach the backend to verify this customer profile. On a real phone APK, do not use localhost; point ApiBaseUrl to the computer running the backend on the same network.",
            ["login_button"] = "Login",
            ["signup_button"] = "Sign Up",
            ["signup_success_message"] = "Your account has been created successfully.",
            ["signup_validation_name"] = "Please enter your full name.",
            ["signup_validation_username"] = "Username must be at least 3 characters and use only letters, numbers, dots, hyphens, or underscores.",
            ["signup_validation_email"] = "Please enter a valid email address.",
            ["signup_validation_phone"] = "Please enter a valid phone number.",
            ["signup_validation_password"] = "Password must contain at least 6 characters.",
            ["signup_validation_confirm_password"] = "Password confirmation does not match. Please re-enter it.",
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
            ["virtual_location_title"] = "Virtual user",
            ["virtual_location_coordinates_label"] = "Coordinates",
            ["virtual_location_status_label"] = "Status",
            ["virtual_location_status_idle"] = "Not inside the 10 m range of any POI",
            ["virtual_location_status_near_poi"] = "Near {0}",
            ["virtual_location_nearest_poi_label"] = "Nearest POI",
            ["virtual_location_nearest_distance_label"] = "Nearest distance",
            ["virtual_location_no_nearest_poi"] = "No nearby POI",
            ["virtual_location_distance_unknown"] = "Unknown",
            ["tour_title"] = "Tour itinerary",
            ["tour_create"] = "Create New Tour",
            ["tour_checkpoints"] = "Visited checkpoints",
            ["settings_title"] = "Settings",
            ["settings_account"] = "Account",
            ["settings_language_title"] = "Language",
            ["settings_full_name"] = "Full name",
            ["settings_user_name"] = "User name",
            ["settings_contact"] = "Email / Phone number",
            ["settings_profile_edit"] = "Edit profile",
            ["settings_profile_save"] = "Save changes",
            ["settings_profile_cancel"] = "Cancel",
            ["settings_profile_saved"] = "Your profile has been updated.",
            ["settings_profile_name_placeholder"] = "Enter your full name",
            ["settings_profile_username_placeholder"] = "Enter your username",
            ["settings_profile_email_placeholder"] = "Enter your email",
            ["settings_profile_phone_placeholder"] = "Enter your phone number",
            ["settings_premium_title"] = "Premium",
            ["settings_premium_status_active"] = "Premium active",
            ["settings_premium_status_free"] = "Free plan",
            ["settings_premium_description_free"] = "Unlock Chinese, Korean, and Japanese with Premium.",
            ["settings_premium_description_active"] = "Your account can already use all supported languages.",
            ["settings_premium_language_list_title"] = "Premium languages",
            ["settings_premium_price_label"] = "Current price",
            ["settings_premium_price_value"] = "{0} USD",
            ["settings_premium_buy_button"] = "Buy Premium for {0} USD",
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
            ["common_cancel"] = "Cancel",
            ["premium_badge"] = "Premium",
            ["premium_upgrade_required_title"] = "Premium required",
            ["premium_upgrade_required_message"] = "{0} requires Premium. Upgrade now for {1} USD?",
            ["premium_purchase_title"] = "Premium purchase",
            ["premium_purchase_confirm"] = "Confirm demo payment of {0} USD to activate Premium for this account?",
            ["premium_purchase_success"] = "Premium is now active. Charged {0} USD.",
            ["premium_login_required"] = "Please sign in to a customer account before purchasing Premium.",
            ["premium_purchase_error"] = "We could not activate Premium. Please try again.",
            ["premium_checkout_title"] = "Premium checkout",
            ["premium_checkout_subtitle"] = "Complete payment before Premium is activated.",
            ["premium_checkout_summary_title"] = "Order summary",
            ["premium_checkout_methods_title"] = "Payment method",
            ["premium_checkout_note"] = "Demo payment only. No real money will be charged in this environment.",
            ["premium_checkout_card_section"] = "Card information",
            ["premium_checkout_cardholder"] = "Cardholder name",
            ["premium_checkout_card_number"] = "Card number",
            ["premium_checkout_card_expiry"] = "Expiry date",
            ["premium_checkout_card_cvv"] = "CVV",
            ["premium_checkout_wallet_section"] = "E-wallet information",
            ["premium_checkout_wallet_provider"] = "Wallet provider",
            ["premium_checkout_wallet_account"] = "Wallet account",
            ["premium_checkout_wallet_pin"] = "Verification PIN",
            ["premium_checkout_submit_button"] = "Pay {0} USD",
            ["premium_checkout_cardholder_placeholder"] = "e.g. Nguyen Bao Vy",
            ["premium_checkout_card_number_placeholder"] = "4242 4242 4242 4242",
            ["premium_checkout_expiry_month_placeholder"] = "MM",
            ["premium_checkout_expiry_year_placeholder"] = "YYYY",
            ["premium_checkout_card_cvv_placeholder"] = "123",
            ["premium_checkout_wallet_account_placeholder"] = "Phone number or email",
            ["premium_checkout_wallet_pin_placeholder"] = "4-6 digits",
            ["premium_checkout_wallet_momo"] = "MoMo",
            ["premium_checkout_wallet_zalopay"] = "ZaloPay",
            ["premium_checkout_method_card"] = "Bank card",
            ["premium_checkout_method_card_description"] = "Pay with a local or international bank card.",
            ["premium_checkout_method_wallet"] = "E-wallet",
            ["premium_checkout_method_wallet_description"] = "Pay with MoMo or ZaloPay in the demo flow.",
            ["premium_checkout_success_message"] = "Premium upgrade successful",
            ["premium_checkout_already_active"] = "This account is already using Premium.",
            ["premium_checkout_validation_cardholder"] = "Please enter the cardholder name.",
            ["premium_checkout_validation_card_number"] = "Please enter a valid card number.",
            ["premium_checkout_validation_expiry"] = "Please enter a valid expiry date.",
            ["premium_checkout_validation_cvv"] = "Please enter a valid CVV.",
            ["premium_checkout_validation_wallet_account"] = "Please enter the wallet account.",
            ["premium_checkout_validation_wallet_pin"] = "Please enter a valid wallet PIN.",
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
