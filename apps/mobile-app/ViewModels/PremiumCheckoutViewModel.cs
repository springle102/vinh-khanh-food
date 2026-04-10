using System.Collections.ObjectModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class PremiumCheckoutViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;
    private readonly AsyncCommand<PremiumPaymentMethodOption> _selectPaymentMethodCommand;
    private readonly AsyncCommand<string> _selectWalletProviderCommand;
    private readonly AsyncCommand _submitPaymentCommand;
    private readonly AsyncCommand _cancelCommand;
    private UserProfileCard _profile = new();
    private PremiumPurchaseOffer _premiumOffer = new();
    private string _navigationSource = "settings";
    private string? _preferredLanguageCode;
    private string? _pendingPoiId;
    private string _cardholderName = string.Empty;
    private string _cardNumber = string.Empty;
    private string _expiryMonth = string.Empty;
    private string _expiryYear = string.Empty;
    private string _cvv = string.Empty;
    private string _walletProvider = PremiumWalletProviderIds.Momo;
    private string _walletAccount = string.Empty;
    private string _walletPin = string.Empty;
    private bool _isProcessingPayment;
    private string _message = string.Empty;
    private bool _isMessageError;
    private bool _hasInitializedForm;

    public PremiumCheckoutViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService)
        : base(languageService)
    {
        _dataService = dataService;
        _selectPaymentMethodCommand = new(SelectPaymentMethodAsync);
        _selectWalletProviderCommand = new(SelectWalletProviderAsync);
        _submitPaymentCommand = new(SubmitPaymentAsync, () => CanSubmitPayment);
        _cancelCommand = new(CancelAsync, () => !IsProcessingPayment);
    }

    public ObservableCollection<PremiumPaymentMethodOption> PaymentMethods { get; } = [];

    public string HeaderTitleText => LanguageService.GetText("premium_checkout_title");
    public string SubtitleText => LanguageService.GetText("premium_checkout_subtitle");
    public string SummaryTitleText => LanguageService.GetText("premium_checkout_summary_title");
    public string MethodsTitleText => LanguageService.GetText("premium_checkout_methods_title");
    public string PaymentNoteText => LanguageService.GetText("premium_checkout_note");
    public string PremiumLanguagesTitleText => LanguageService.GetText("settings_premium_language_list_title");
    public string PremiumLanguagesText => string.Join(", ", _premiumOffer.PremiumLanguageCodes.Select(ResolveLanguageDisplayName));
    public string PriceLabelText => LanguageService.GetText("settings_premium_price_label");
    public string PriceText => string.Format(LanguageService.CurrentCulture, LanguageService.GetText("settings_premium_price_value"), _premiumOffer.PriceUsd);
    public string CardSectionTitleText => LanguageService.GetText("premium_checkout_card_section");
    public string CardholderLabelText => LanguageService.GetText("premium_checkout_cardholder");
    public string CardNumberLabelText => LanguageService.GetText("premium_checkout_card_number");
    public string CardExpiryLabelText => LanguageService.GetText("premium_checkout_card_expiry");
    public string CardCvvLabelText => LanguageService.GetText("premium_checkout_card_cvv");
    public string WalletSectionTitleText => LanguageService.GetText("premium_checkout_wallet_section");
    public string WalletProviderLabelText => LanguageService.GetText("premium_checkout_wallet_provider");
    public string WalletAccountLabelText => LanguageService.GetText("premium_checkout_wallet_account");
    public string WalletPinLabelText => LanguageService.GetText("premium_checkout_wallet_pin");
    public string SubmitPaymentText => string.Format(LanguageService.CurrentCulture, LanguageService.GetText("premium_checkout_submit_button"), _premiumOffer.PriceUsd);
    public string CancelText => LanguageService.GetText("common_cancel");
    public string CardholderPlaceholderText => LanguageService.GetText("premium_checkout_cardholder_placeholder");
    public string CardNumberPlaceholderText => LanguageService.GetText("premium_checkout_card_number_placeholder");
    public string ExpiryMonthPlaceholderText => LanguageService.GetText("premium_checkout_expiry_month_placeholder");
    public string ExpiryYearPlaceholderText => LanguageService.GetText("premium_checkout_expiry_year_placeholder");
    public string CvvPlaceholderText => LanguageService.GetText("premium_checkout_card_cvv_placeholder");
    public string WalletAccountPlaceholderText => LanguageService.GetText("premium_checkout_wallet_account_placeholder");
    public string WalletPinPlaceholderText => LanguageService.GetText("premium_checkout_wallet_pin_placeholder");
    public string MomoText => LanguageService.GetText("premium_checkout_wallet_momo");
    public string ZaloPayText => LanguageService.GetText("premium_checkout_wallet_zalopay");
    public bool IsBankCardSelected => string.Equals(SelectedPaymentMethodId, PremiumPaymentMethodIds.BankCard, StringComparison.OrdinalIgnoreCase);
    public bool IsEWalletSelected => string.Equals(SelectedPaymentMethodId, PremiumPaymentMethodIds.EWallet, StringComparison.OrdinalIgnoreCase);
    public bool IsMomoSelected => string.Equals(WalletProvider, PremiumWalletProviderIds.Momo, StringComparison.OrdinalIgnoreCase);
    public bool IsZaloPaySelected => string.Equals(WalletProvider, PremiumWalletProviderIds.ZaloPay, StringComparison.OrdinalIgnoreCase);
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool CanSubmitPayment => !IsProcessingPayment && _profile.HasResolvedAccount && !_profile.IsPremium;
    public string SelectedPaymentMethodId => PaymentMethods.FirstOrDefault(item => item.IsSelected)?.Id ?? PremiumPaymentMethodIds.BankCard;

    public string CardholderName
    {
        get => _cardholderName;
        set
        {
            if (SetProperty(ref _cardholderName, value))
            {
                ClearMessage();
                RefreshFormState();
            }
        }
    }

    public string CardNumber
    {
        get => _cardNumber;
        set
        {
            if (SetProperty(ref _cardNumber, value))
            {
                ClearMessage();
                RefreshFormState();
            }
        }
    }

    public string ExpiryMonth
    {
        get => _expiryMonth;
        set
        {
            if (SetProperty(ref _expiryMonth, value))
            {
                ClearMessage();
                RefreshFormState();
            }
        }
    }

    public string ExpiryYear
    {
        get => _expiryYear;
        set
        {
            if (SetProperty(ref _expiryYear, value))
            {
                ClearMessage();
                RefreshFormState();
            }
        }
    }

    public string Cvv
    {
        get => _cvv;
        set
        {
            if (SetProperty(ref _cvv, value))
            {
                ClearMessage();
                RefreshFormState();
            }
        }
    }

    public string WalletProvider
    {
        get => _walletProvider;
        private set
        {
            if (SetProperty(ref _walletProvider, value))
            {
                ClearMessage();
                OnPropertyChanged(nameof(IsMomoSelected));
                OnPropertyChanged(nameof(IsZaloPaySelected));
                RefreshFormState();
            }
        }
    }

    public string WalletAccount
    {
        get => _walletAccount;
        set
        {
            if (SetProperty(ref _walletAccount, value))
            {
                ClearMessage();
                RefreshFormState();
            }
        }
    }

    public string WalletPin
    {
        get => _walletPin;
        set
        {
            if (SetProperty(ref _walletPin, value))
            {
                ClearMessage();
                RefreshFormState();
            }
        }
    }

    public bool IsProcessingPayment
    {
        get => _isProcessingPayment;
        private set
        {
            if (SetProperty(ref _isProcessingPayment, value))
            {
                RefreshFormState();
            }
        }
    }

    public string Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool IsMessageError
    {
        get => _isMessageError;
        private set => SetProperty(ref _isMessageError, value);
    }

    public AsyncCommand<PremiumPaymentMethodOption> SelectPaymentMethodCommand => _selectPaymentMethodCommand;
    public AsyncCommand<string> SelectWalletProviderCommand => _selectWalletProviderCommand;
    public AsyncCommand SubmitPaymentCommand => _submitPaymentCommand;
    public AsyncCommand CancelCommand => _cancelCommand;

    public void SetNavigationContext(string? source, string? preferredLanguageCode, string? pendingPoiId)
    {
        _navigationSource = string.IsNullOrWhiteSpace(source) ? "settings" : source.Trim().ToLowerInvariant();
        _preferredLanguageCode = string.IsNullOrWhiteSpace(preferredLanguageCode) ? null : Uri.UnescapeDataString(preferredLanguageCode.Trim());
        _pendingPoiId = string.IsNullOrWhiteSpace(pendingPoiId) ? null : Uri.UnescapeDataString(pendingPoiId.Trim());
    }

    public async Task LoadAsync()
    {
        _profile = await _dataService.GetUserProfileAsync();
        _premiumOffer = await _dataService.GetPremiumOfferAsync();
        EnsurePaymentMethods();
        EnsureFormDefaults();
        RefreshViewState();
    }

    protected override async Task ReloadLocalizedStateAsync()
    {
        EnsurePaymentMethods();
        RefreshPaymentMethodTexts();
        await LoadAsync();
    }

    private void EnsurePaymentMethods()
    {
        if (PaymentMethods.Count == 0)
        {
            PaymentMethods.Add(new PremiumPaymentMethodOption { Id = PremiumPaymentMethodIds.BankCard, AccentColor = "#C97A32" });
            PaymentMethods.Add(new PremiumPaymentMethodOption { Id = PremiumPaymentMethodIds.EWallet, AccentColor = "#2E8C6A" });
            PaymentMethods[0].IsSelected = true;
        }

        RefreshPaymentMethodTexts();
    }

    private void RefreshPaymentMethodTexts()
    {
        foreach (var item in PaymentMethods)
        {
            if (string.Equals(item.Id, PremiumPaymentMethodIds.BankCard, StringComparison.OrdinalIgnoreCase))
            {
                item.Title = LanguageService.GetText("premium_checkout_method_card");
                item.Description = LanguageService.GetText("premium_checkout_method_card_description");
            }
            else
            {
                item.Title = LanguageService.GetText("premium_checkout_method_wallet");
                item.Description = LanguageService.GetText("premium_checkout_method_wallet_description");
            }
        }
    }

    private void EnsureFormDefaults()
    {
        if (_hasInitializedForm)
        {
            return;
        }

        CardholderName = string.Empty;
        CardNumber = string.Empty;
        ExpiryMonth = string.Empty;
        ExpiryYear = string.Empty;
        Cvv = string.Empty;
        WalletProvider = PremiumWalletProviderIds.Momo;
        WalletAccount = string.Empty;
        WalletPin = string.Empty;
        _hasInitializedForm = true;
    }

    private async Task SelectPaymentMethodAsync(PremiumPaymentMethodOption? option)
    {
        if (option is null || IsProcessingPayment)
        {
            return;
        }

        foreach (var item in PaymentMethods)
        {
            item.IsSelected = string.Equals(item.Id, option.Id, StringComparison.OrdinalIgnoreCase);
        }

        ClearMessage();
        RefreshViewState();
        await Task.CompletedTask;
    }

    private async Task SelectWalletProviderAsync(string? provider)
    {
        if (IsProcessingPayment || string.IsNullOrWhiteSpace(provider))
        {
            return;
        }

        WalletProvider = provider.Trim().ToLowerInvariant();
        await Task.CompletedTask;
    }

    private async Task SubmitPaymentAsync()
    {
        if (!CanSubmitPayment)
        {
            SetMessage(LanguageService.GetText("premium_login_required"), isError: true);
            return;
        }

        if (!TryValidateForm(out var validationMessage))
        {
            SetMessage(validationMessage, isError: true);
            return;
        }

        try
        {
            IsProcessingPayment = true;
            ClearMessage();

            var request = new PremiumCheckoutRequest
            {
                PaymentProvider = "mock",
                PaymentMethod = SelectedPaymentMethodId,
                ClientRequestId = Guid.NewGuid().ToString("N"),
                ExpectedPriceUsd = _premiumOffer.PriceUsd,
                CardholderName = CardholderName.Trim(),
                CardNumber = CardNumber.Trim(),
                ExpiryMonth = NormalizeDigits(ExpiryMonth),
                ExpiryYear = NormalizeYear(ExpiryYear),
                Cvv = Cvv.Trim(),
                WalletProvider = WalletProvider,
                WalletAccount = WalletAccount.Trim(),
                WalletPin = WalletPin.Trim()
            };

            await _dataService.PurchasePremiumAsync(request);
            if (!string.IsNullOrWhiteSpace(_preferredLanguageCode))
            {
                await LanguageService.SetLanguageAsync(_preferredLanguageCode);
            }

            await Shell.Current.DisplayAlertAsync(
                LanguageService.GetText("premium_purchase_title"),
                LanguageService.GetText("premium_checkout_success_message"),
                LanguageService.GetText("common_ok"));
            await Shell.Current.GoToAsync(ResolveSuccessRoute());
        }
        catch (Exception exception)
        {
            SetMessage(
                string.IsNullOrWhiteSpace(exception.Message)
                    ? LanguageService.GetText("premium_purchase_error")
                    : exception.Message,
                isError: true);
        }
        finally
        {
            IsProcessingPayment = false;
        }
    }

    private async Task CancelAsync()
    {
        if (IsProcessingPayment)
        {
            return;
        }

        await Shell.Current.GoToAsync("..");
    }

    private bool TryValidateForm(out string message)
    {
        if (!_profile.HasResolvedAccount)
        {
            message = LanguageService.GetText("premium_login_required");
            return false;
        }

        if (_profile.IsPremium)
        {
            message = LanguageService.GetText("premium_checkout_already_active");
            return false;
        }

        if (IsBankCardSelected)
        {
            if (string.IsNullOrWhiteSpace(CardholderName))
            {
                message = LanguageService.GetText("premium_checkout_validation_cardholder");
                return false;
            }

            var cardDigits = NormalizeDigits(CardNumber);
            if (cardDigits.Length < 12 || cardDigits.Length > 19 || !PassesLuhnCheck(cardDigits))
            {
                message = LanguageService.GetText("premium_checkout_validation_card_number");
                return false;
            }

            if (!int.TryParse(NormalizeDigits(ExpiryMonth), out var expiryMonth) || expiryMonth is < 1 or > 12)
            {
                message = LanguageService.GetText("premium_checkout_validation_expiry");
                return false;
            }

            if (!int.TryParse(NormalizeYear(ExpiryYear), out var expiryYear) || expiryYear < DateTimeOffset.UtcNow.Year)
            {
                message = LanguageService.GetText("premium_checkout_validation_expiry");
                return false;
            }

            var expiry = new DateTimeOffset(expiryYear, expiryMonth, DateTime.DaysInMonth(expiryYear, expiryMonth), 23, 59, 59, TimeSpan.Zero);
            if (expiry < DateTimeOffset.UtcNow)
            {
                message = LanguageService.GetText("premium_checkout_validation_expiry");
                return false;
            }

            var cvvDigits = NormalizeDigits(Cvv);
            if (cvvDigits.Length is < 3 or > 4)
            {
                message = LanguageService.GetText("premium_checkout_validation_cvv");
                return false;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(WalletAccount))
            {
                message = LanguageService.GetText("premium_checkout_validation_wallet_account");
                return false;
            }

            var walletPinDigits = NormalizeDigits(WalletPin);
            if (walletPinDigits.Length is < 4 or > 6)
            {
                message = LanguageService.GetText("premium_checkout_validation_wallet_pin");
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private string ResolveSuccessRoute()
    {
        if (string.Equals(_navigationSource, "language", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(_pendingPoiId)
                ? $"{AppRoutes.Root(AppRoutes.HomeMap)}?poiId={Uri.EscapeDataString(_pendingPoiId)}"
                : AppRoutes.Root(AppRoutes.HomeMap);
        }

        return AppRoutes.Root(AppRoutes.Settings);
    }

    private string ResolveLanguageDisplayName(string languageCode)
        => LanguageService.GetLanguageDefinition(languageCode).DisplayName;

    private void RefreshViewState()
    {
        OnPropertyChanged(nameof(PremiumLanguagesText));
        OnPropertyChanged(nameof(PriceText));
        OnPropertyChanged(nameof(SelectedPaymentMethodId));
        OnPropertyChanged(nameof(IsBankCardSelected));
        OnPropertyChanged(nameof(IsEWalletSelected));
        OnPropertyChanged(nameof(CanSubmitPayment));
        RefreshFormState();
    }

    private void RefreshFormState()
    {
        OnPropertyChanged(nameof(CanSubmitPayment));
        _submitPaymentCommand.NotifyCanExecuteChanged();
        _cancelCommand.NotifyCanExecuteChanged();
    }

    private void SetMessage(string? message, bool isError)
    {
        Message = message?.Trim() ?? string.Empty;
        IsMessageError = isError;
    }

    private void ClearMessage()
    {
        if (!string.IsNullOrWhiteSpace(Message))
        {
            Message = string.Empty;
        }

        if (IsMessageError)
        {
            IsMessageError = false;
        }
    }

    private static string NormalizeDigits(string? value)
        => new string((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string NormalizeYear(string? value)
    {
        var digits = NormalizeDigits(value);
        if (digits.Length == 2 && int.TryParse(digits, out var shortYear))
        {
            return (2000 + shortYear).ToString();
        }

        return digits;
    }

    private static bool PassesLuhnCheck(string digits)
    {
        var checksum = 0;
        var shouldDouble = false;

        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var digit = digits[index] - '0';
            if (shouldDouble)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            checksum += digit;
            shouldDouble = !shouldDouble;
        }

        return checksum > 0 && checksum % 10 == 0;
    }
}
