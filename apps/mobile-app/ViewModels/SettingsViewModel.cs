using System.Collections.ObjectModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class SettingsViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;
    private readonly AsyncCommand _startEditProfileCommand;
    private readonly AsyncCommand _saveProfileCommand;
    private readonly AsyncCommand _cancelEditProfileCommand;
    private readonly AsyncCommand _buyPremiumCommand;
    private readonly AsyncCommand<LanguageOption> _selectLanguageCommand;
    private UserProfileCard? _profile;
    private PremiumPurchaseOffer _premiumOffer = new();
    private string _fullName = string.Empty;
    private string _username = string.Empty;
    private string _email = string.Empty;
    private string _phone = string.Empty;
    private bool _isEditingProfile;
    private bool _isSavingProfile;
    private bool _isPurchasingPremium;
    private string _profileMessage = string.Empty;
    private bool _isProfileMessageError;
    private string _premiumMessage = string.Empty;
    private bool _isPremiumMessageError;

    public SettingsViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService)
        : base(languageService)
    {
        _dataService = dataService;
        _startEditProfileCommand = new(StartEditProfileAsync, () => Profile is not null && !IsEditingProfile && !IsSavingProfile);
        _saveProfileCommand = new(SaveProfileAsync, CanSaveProfile);
        _cancelEditProfileCommand = new(CancelEditProfileAsync, () => IsEditingProfile && !IsSavingProfile);
        _buyPremiumCommand = new(() => BuyPremiumAsync(null), () => CanPurchasePremium && !IsPurchasingPremium);
        _selectLanguageCommand = new(SelectLanguageAsync);
    }

    public ObservableCollection<LanguageOption> Languages { get; } = [];
    public ObservableCollection<SettingsMenuItem> MenuItems { get; } = [];

    public UserProfileCard? Profile
    {
        get => _profile;
        private set
        {
            if (SetProperty(ref _profile, value))
            {
                OnPropertyChanged(nameof(HasProfile));
                OnPropertyChanged(nameof(HasResolvedAccount));
                OnPropertyChanged(nameof(IsPremiumActive));
                OnPropertyChanged(nameof(CanPurchasePremium));
                OnPropertyChanged(nameof(PremiumStatusText));
                OnPropertyChanged(nameof(PremiumDescriptionText));
                RefreshCommandState();
            }
        }
    }

    public string FullName
    {
        get => _fullName;
        set
        {
            if (SetProperty(ref _fullName, value))
            {
                ClearProfileMessage();
                RefreshCommandState();
            }
        }
    }

    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
            {
                ClearProfileMessage();
                RefreshCommandState();
            }
        }
    }

    public string Email
    {
        get => _email;
        set
        {
            if (SetProperty(ref _email, value))
            {
                ClearProfileMessage();
                RefreshCommandState();
            }
        }
    }

    public string Phone
    {
        get => _phone;
        set
        {
            if (SetProperty(ref _phone, value))
            {
                ClearProfileMessage();
                RefreshCommandState();
            }
        }
    }

    public bool IsEditingProfile
    {
        get => _isEditingProfile;
        private set
        {
            if (SetProperty(ref _isEditingProfile, value))
            {
                OnPropertyChanged(nameof(IsViewingProfile));
                RefreshCommandState();
            }
        }
    }

    public bool IsSavingProfile
    {
        get => _isSavingProfile;
        private set
        {
            if (SetProperty(ref _isSavingProfile, value))
            {
                RefreshCommandState();
            }
        }
    }

    public bool IsPurchasingPremium
    {
        get => _isPurchasingPremium;
        private set
        {
            if (SetProperty(ref _isPurchasingPremium, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string ProfileMessage
    {
        get => _profileMessage;
        private set
        {
            if (SetProperty(ref _profileMessage, value))
            {
                OnPropertyChanged(nameof(HasProfileMessage));
            }
        }
    }

    public bool IsProfileMessageError
    {
        get => _isProfileMessageError;
        private set => SetProperty(ref _isProfileMessageError, value);
    }

    public string PremiumMessage
    {
        get => _premiumMessage;
        private set
        {
            if (SetProperty(ref _premiumMessage, value))
            {
                OnPropertyChanged(nameof(HasPremiumMessage));
            }
        }
    }

    public bool IsPremiumMessageError
    {
        get => _isPremiumMessageError;
        private set => SetProperty(ref _isPremiumMessageError, value);
    }

    public bool HasProfile => Profile is not null;
    public bool HasResolvedAccount => Profile?.HasResolvedAccount == true;
    public bool IsViewingProfile => !IsEditingProfile;
    public bool HasProfileMessage => !string.IsNullOrWhiteSpace(ProfileMessage);
    public bool HasPremiumMessage => !string.IsNullOrWhiteSpace(PremiumMessage);
    public bool IsPremiumActive => Profile?.IsPremium == true;
    public bool CanPurchasePremium => HasResolvedAccount && !IsPremiumActive;

    public string HeaderTitleText => LanguageService.GetText("settings_title");
    public string AccountTitleText => LanguageService.GetText("settings_account");
    public string LanguageTitleText => LanguageService.GetText("settings_language_title");
    public string PremiumTitleText => LanguageService.GetText("settings_premium_title");
    public string PremiumBadgeText => LanguageService.GetText("premium_badge");
    public string PremiumStatusText => LanguageService.GetText(IsPremiumActive
        ? "settings_premium_status_active"
        : "settings_premium_status_free");
    public string PremiumDescriptionText => LanguageService.GetText(IsPremiumActive
        ? "settings_premium_description_active"
        : "settings_premium_description_free");
    public string PremiumLanguageListTitleText => LanguageService.GetText("settings_premium_language_list_title");
    public string PremiumLanguageListText => string.Join(", ", PremiumOffer.PremiumLanguageCodes.Select(ResolveLanguageDisplayName));
    public string PremiumPriceLabelText => LanguageService.GetText("settings_premium_price_label");
    public string PremiumPriceText => string.Format(LanguageService.CurrentCulture, LanguageService.GetText("settings_premium_price_value"), PremiumOffer.PriceUsd);
    public string PremiumActionText => string.Format(LanguageService.CurrentCulture, LanguageService.GetText("settings_premium_buy_button"), PremiumOffer.PriceUsd);
    public string FullNameLabelText => LanguageService.GetText("settings_full_name");
    public string UserNameLabelText => LanguageService.GetText("settings_user_name");
    public string ContactLabelText => LanguageService.GetText("settings_contact");
    public string LogoutText => LanguageService.GetText("settings_logout");
    public string EditProfileText => LanguageService.GetText("settings_profile_edit");
    public string SaveProfileText => LanguageService.GetText("settings_profile_save");
    public string CancelProfileEditText => LanguageService.GetText("settings_profile_cancel");
    public string NamePlaceholderText => LanguageService.GetText("settings_profile_name_placeholder");
    public string UsernamePlaceholderText => LanguageService.GetText("settings_profile_username_placeholder");
    public string EmailPlaceholderText => LanguageService.GetText("settings_profile_email_placeholder");
    public string PhonePlaceholderText => LanguageService.GetText("settings_profile_phone_placeholder");

    private PremiumPurchaseOffer PremiumOffer
    {
        get => _premiumOffer;
        set
        {
            _premiumOffer = value ?? new PremiumPurchaseOffer();
            OnPropertyChanged(nameof(PremiumLanguageListText));
            OnPropertyChanged(nameof(PremiumPriceText));
            OnPropertyChanged(nameof(PremiumActionText));
        }
    }

    public AsyncCommand<LanguageOption> SelectLanguageCommand => _selectLanguageCommand;
    public AsyncCommand BuyPremiumCommand => _buyPremiumCommand;
    public AsyncCommand LogoutCommand => new(() => Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.Login)));
    public AsyncCommand StartEditProfileCommand => _startEditProfileCommand;
    public AsyncCommand SaveProfileCommand => _saveProfileCommand;
    public AsyncCommand CancelEditProfileCommand => _cancelEditProfileCommand;

    public async Task LoadAsync()
    {
        await RefreshLocalizedStateAsync();
    }

    private async Task RefreshLocalizedStateAsync()
    {
        await _dataService.EnsureAllowedLanguageSelectionAsync();
        Profile = await _dataService.GetUserProfileAsync();
        PremiumOffer = await _dataService.GetPremiumOfferAsync();
        SyncEditorWithProfile(Profile);
        Languages.ReplaceRange(await _dataService.GetLanguagesAsync());
        MenuItems.ReplaceRange(await _dataService.GetSettingsMenuAsync());
        RefreshLocalizedBindings();
    }

    private async Task StartEditProfileAsync()
    {
        SyncEditorWithProfile(Profile);
        ClearProfileMessage();
        IsEditingProfile = true;
        await Task.CompletedTask;
    }

    private async Task CancelEditProfileAsync()
    {
        SyncEditorWithProfile(Profile);
        ClearProfileMessage();
        IsEditingProfile = false;
        await Task.CompletedTask;
    }

    private bool CanSaveProfile()
        => Profile is not null &&
           IsEditingProfile &&
           !IsSavingProfile &&
           !string.IsNullOrWhiteSpace(FullName) &&
           !string.IsNullOrWhiteSpace(Username) &&
           !string.IsNullOrWhiteSpace(Email) &&
           !string.IsNullOrWhiteSpace(Phone);

    private async Task SaveProfileAsync()
    {
        if (!CanSaveProfile())
        {
            return;
        }

        try
        {
            IsSavingProfile = true;
            ClearProfileMessage();

            var updatedProfile = await _dataService.UpdateUserProfileAsync(new UserProfileUpdateRequest
            {
                Name = FullName,
                Username = Username,
                Email = Email,
                Phone = Phone
            });

            Profile = updatedProfile;
            SyncEditorWithProfile(updatedProfile);
            IsEditingProfile = false;
            SetProfileMessage(LanguageService.GetText("settings_profile_saved"), isError: false);
        }
        catch (Exception exception)
        {
            SetProfileMessage(exception.Message, isError: true);
        }
        finally
        {
            IsSavingProfile = false;
        }
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

        await ApplyLanguageSelectionAsync(language.Code);
    }

    private async Task BuyPremiumAsync(string? preferredLanguageCode)
    {
        var canContinue = await EnsurePurchasableAccountAsync();
        if (!canContinue || IsPurchasingPremium || IsPremiumActive)
        {
            return;
        }

        try
        {
            IsPurchasingPremium = true;
            ClearPremiumMessage();
            var route = AppRoutes.PremiumCheckout;
            if (!string.IsNullOrWhiteSpace(preferredLanguageCode))
            {
                route += $"?source=settings&preferredLanguageCode={Uri.EscapeDataString(preferredLanguageCode)}";
            }
            else
            {
                route += "?source=settings";
            }

            await Shell.Current.GoToAsync(route);
        }
        catch (Exception exception)
        {
            SetPremiumMessage(
                string.IsNullOrWhiteSpace(exception.Message)
                    ? LanguageService.GetText("premium_purchase_error")
                    : exception.Message,
                isError: true);
        }
        finally
        {
            IsPurchasingPremium = false;
        }
    }

    private async Task<bool> EnsurePurchasableAccountAsync()
    {
        if (Profile?.HasResolvedAccount == true)
        {
            return true;
        }

        Profile = await _dataService.GetUserProfileAsync();
        if (Profile?.HasResolvedAccount == true)
        {
            return true;
        }

        await Shell.Current.DisplayAlertAsync(
            LanguageService.GetText("premium_upgrade_required_title"),
            LanguageService.GetText("premium_login_required"),
            LanguageService.GetText("common_ok"));
        await Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.Login));
        return false;
    }

    private async Task ApplyLanguageSelectionAsync(string languageCode)
    {
        foreach (var item in Languages)
        {
            item.IsSelected = string.Equals(item.Code, languageCode, StringComparison.OrdinalIgnoreCase);
        }

        await LanguageService.SetLanguageAsync(languageCode);
    }

    protected override async Task ReloadLocalizedStateAsync()
        => await RefreshLocalizedStateAsync();

    private void SyncEditorWithProfile(UserProfileCard? profile)
    {
        FullName = profile?.FullName ?? string.Empty;
        Username = profile?.Username ?? string.Empty;
        Email = profile?.Email ?? string.Empty;
        Phone = profile?.Phone ?? string.Empty;
    }

    private string ResolveLanguageDisplayName(string languageCode)
        => LanguageService.GetLanguageDefinition(languageCode).DisplayName;

    private void SetProfileMessage(string? message, bool isError)
    {
        ProfileMessage = message?.Trim() ?? string.Empty;
        IsProfileMessageError = isError;
    }

    private void ClearProfileMessage()
    {
        if (!string.IsNullOrWhiteSpace(ProfileMessage))
        {
            ProfileMessage = string.Empty;
        }

        if (IsProfileMessageError)
        {
            IsProfileMessageError = false;
        }
    }

    private void SetPremiumMessage(string? message, bool isError)
    {
        PremiumMessage = message?.Trim() ?? string.Empty;
        IsPremiumMessageError = isError;
    }

    private void ClearPremiumMessage()
    {
        if (!string.IsNullOrWhiteSpace(PremiumMessage))
        {
            PremiumMessage = string.Empty;
        }

        if (IsPremiumMessageError)
        {
            IsPremiumMessageError = false;
        }
    }

    private void RefreshCommandState()
    {
        _startEditProfileCommand.NotifyCanExecuteChanged();
        _saveProfileCommand.NotifyCanExecuteChanged();
        _cancelEditProfileCommand.NotifyCanExecuteChanged();
        _buyPremiumCommand.NotifyCanExecuteChanged();
    }
}
