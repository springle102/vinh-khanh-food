using System.Collections.ObjectModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class LanguageSelectionViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;
    private readonly AsyncCommand<LanguageOption> _selectLanguageCommand;
    private readonly AsyncCommand _buyPremiumCommand;
    private string? _pendingQrCode;
    private UserProfileCard? _profile;
    private PremiumPurchaseOffer _premiumOffer = new();
    private bool _isPurchasingPremium;

    public LanguageSelectionViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService)
        : base(languageService)
    {
        _dataService = dataService;
        _selectLanguageCommand = new(SelectLanguageAsync);
        _buyPremiumCommand = new(() => BuyPremiumAsync(null), () => CanPurchasePremium && !IsPurchasingPremium);
    }

    public ObservableCollection<LanguageOption> Languages { get; } = [];

    public string BackgroundImageUrl => _dataService.GetBackdropImageUrl();
    public string BrandTitleText => LanguageService.GetText("brand_title");
    public string ScanSuccessText => HasPendingQrCode
        ? LanguageService.GetText("qr_success_title")
        : LanguageService.GetText("language_selection_title");
    public string ChooseLanguageText => HasPendingQrCode
        ? LanguageService.GetText("qr_choose_language")
        : LanguageService.GetText("language_selection_subtitle");
    public string PremiumTitleText => LanguageService.GetText("settings_premium_title");
    public string PremiumBadgeText => LanguageService.GetText("premium_badge");
    public string PremiumStatusText => LanguageService.GetText(IsPremiumActive
        ? "settings_premium_status_active"
        : "settings_premium_status_free");
    public string PremiumDescriptionText => LanguageService.GetText(IsPremiumActive
        ? "settings_premium_description_active"
        : "settings_premium_description_free");
    public string PremiumPriceText => string.Format(LanguageService.CurrentCulture, LanguageService.GetText("settings_premium_price_value"), PremiumOffer.PriceUsd);
    public string PremiumActionText => string.Format(LanguageService.CurrentCulture, LanguageService.GetText("settings_premium_buy_button"), PremiumOffer.PriceUsd);
    public string ContinueText => LanguageService.GetText("qr_continue");
    public string PendingLabelText => LanguageService.GetText("language_selection_pending_label");
    public bool HasPendingQrCode => !string.IsNullOrWhiteSpace(_pendingQrCode);
    public string PendingQrCodeText => _pendingQrCode ?? string.Empty;
    public bool IsPremiumActive => _profile?.IsPremium == true;
    public bool CanPurchasePremium => (_profile?.HasResolvedAccount == true) && !IsPremiumActive;

    private PremiumPurchaseOffer PremiumOffer
    {
        get => _premiumOffer;
        set
        {
            _premiumOffer = value ?? new PremiumPurchaseOffer();
            OnPropertyChanged(nameof(PremiumPriceText));
            OnPropertyChanged(nameof(PremiumActionText));
        }
    }

    public bool IsPurchasingPremium
    {
        get => _isPurchasingPremium;
        private set
        {
            if (SetProperty(ref _isPurchasingPremium, value))
            {
                OnPropertyChanged(nameof(CanPurchasePremium));
                _buyPremiumCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AsyncCommand<LanguageOption> SelectLanguageCommand => _selectLanguageCommand;
    public AsyncCommand BuyPremiumCommand => _buyPremiumCommand;
    public AsyncCommand ContinueCommand => new(ContinueAsync);

    public async Task LoadAsync()
    {
        await RefreshAsync();
        OnPropertyChanged(nameof(BackgroundImageUrl));
    }

    public void SetPendingQrCode(string? qrCode)
    {
        _pendingQrCode = string.IsNullOrWhiteSpace(qrCode)
            ? null
            : Uri.UnescapeDataString(qrCode.Trim());
        RefreshLocalizedBindings();
    }

    private async Task RefreshAsync()
    {
        await _dataService.EnsureAllowedLanguageSelectionAsync();
        _profile = await _dataService.GetUserProfileAsync();
        PremiumOffer = await _dataService.GetPremiumOfferAsync();
        Languages.ReplaceRange(await _dataService.GetLanguagesAsync());
        OnPropertyChanged(nameof(IsPremiumActive));
        OnPropertyChanged(nameof(CanPurchasePremium));
        OnPropertyChanged(nameof(PremiumStatusText));
        OnPropertyChanged(nameof(PremiumDescriptionText));
        RefreshLocalizedBindings();
        _buyPremiumCommand.NotifyCanExecuteChanged();
    }

    private async Task SelectLanguageAsync(LanguageOption? language)
    {
        if (language is null)
        {
            return;
        }

        if (language.IsLocked)
        {
            var canContinue = await EnsurePurchasableAccountAsync();
            if (!canContinue)
            {
                return;
            }

            var upgradeMessage = string.Format(
                LanguageService.CurrentCulture,
                LanguageService.GetText("premium_upgrade_required_message"),
                language.DisplayName,
                PremiumOffer.PriceUsd);
            var shouldPurchase = await Shell.Current.DisplayAlertAsync(
                LanguageService.GetText("premium_upgrade_required_title"),
                upgradeMessage,
                PremiumActionText,
                LanguageService.GetText("common_cancel"));
            if (shouldPurchase)
            {
                await BuyPremiumAsync(language.Code);
            }

            return;
        }

        foreach (var item in Languages)
        {
            item.IsSelected = string.Equals(item.Code, language.Code, StringComparison.OrdinalIgnoreCase);
        }

        await LanguageService.SetLanguageAsync(language.Code);
    }

    private async Task BuyPremiumAsync(string? preferredLanguageCode)
    {
        var canContinue = await EnsurePurchasableAccountAsync();
        if (!canContinue || IsPremiumActive || IsPurchasingPremium)
        {
            return;
        }

        try
        {
            IsPurchasingPremium = true;
            var queryParts = new List<string> { "source=language" };
            if (!string.IsNullOrWhiteSpace(preferredLanguageCode))
            {
                queryParts.Add($"preferredLanguageCode={Uri.EscapeDataString(preferredLanguageCode)}");
            }

            var pendingPoiId = ResolvePoiId(_pendingQrCode);
            if (!string.IsNullOrWhiteSpace(pendingPoiId))
            {
                queryParts.Add($"pendingPoiId={Uri.EscapeDataString(pendingPoiId)}");
            }

            await Shell.Current.GoToAsync($"{AppRoutes.PremiumCheckout}?{string.Join("&", queryParts)}");
        }
        catch (Exception exception)
        {
            await Shell.Current.DisplayAlertAsync(
                LanguageService.GetText("premium_purchase_title"),
                string.IsNullOrWhiteSpace(exception.Message)
                    ? LanguageService.GetText("premium_purchase_error")
                    : exception.Message,
                LanguageService.GetText("common_ok"));
        }
        finally
        {
            IsPurchasingPremium = false;
        }
    }

    private async Task<bool> EnsurePurchasableAccountAsync()
    {
        if (_profile?.HasResolvedAccount == true)
        {
            return true;
        }

        _profile = await _dataService.GetUserProfileAsync();
        if (_profile?.HasResolvedAccount == true)
        {
            OnPropertyChanged(nameof(CanPurchasePremium));
            return true;
        }

        await Shell.Current.DisplayAlertAsync(
            LanguageService.GetText("premium_upgrade_required_title"),
            LanguageService.GetText("premium_login_required"),
            LanguageService.GetText("common_ok"));
        await Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.Login));
        return false;
    }

    private async Task ContinueAsync()
    {
        if (!LanguageService.HasSavedLanguageSelection)
        {
            var selectedLanguageCode = Languages.FirstOrDefault(item => item.IsSelected)?.Code ?? LanguageService.CurrentLanguage;
            await LanguageService.SetLanguageAsync(selectedLanguageCode);
        }

        var route = AppRoutes.Root(AppRoutes.Login);
        var poiId = ResolvePoiId(_pendingQrCode);
        if (!string.IsNullOrWhiteSpace(poiId))
        {
            route = $"{AppRoutes.Root(AppRoutes.HomeMap)}?poiId={Uri.EscapeDataString(poiId)}";
        }

        await Shell.Current.GoToAsync(route);
    }

    protected override Task ReloadLocalizedStateAsync()
    {
        SyncSelectedLanguage();
        OnPropertyChanged(nameof(IsPremiumActive));
        OnPropertyChanged(nameof(CanPurchasePremium));
        OnPropertyChanged(nameof(PremiumStatusText));
        OnPropertyChanged(nameof(PremiumDescriptionText));
        _buyPremiumCommand.NotifyCanExecuteChanged();
        return Task.CompletedTask;
    }

    private void SyncSelectedLanguage()
    {
        var currentLanguageCode = AppLanguage.NormalizeCode(LanguageService.CurrentLanguage);
        foreach (var language in Languages)
        {
            language.IsSelected = string.Equals(
                AppLanguage.NormalizeCode(language.Code),
                currentLanguageCode,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string? ResolvePoiId(string? qrCode)
    {
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            return null;
        }

        var trimmed = qrCode.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .FirstOrDefault(part => part.Length == 2 && string.Equals(part[0], "poiId", StringComparison.OrdinalIgnoreCase));
        if (query is not null)
        {
            return Uri.UnescapeDataString(query[1]);
        }

        var lastSegment = uri.Segments.LastOrDefault()?.Trim('/');
        return string.IsNullOrWhiteSpace(lastSegment) ? trimmed : lastSegment;
    }
}
