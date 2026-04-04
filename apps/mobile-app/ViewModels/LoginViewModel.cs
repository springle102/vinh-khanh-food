using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class LoginViewModel : BaseViewModel
{
    private readonly IFoodStreetDataService _dataService;
    private readonly IAppLanguageService _languageService;
    private bool _isLoginMode = true;
    private string _identifier = string.Empty;
    private string _password = string.Empty;
    private bool _isLoaded;

    public LoginViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService)
    {
        _dataService = dataService;
        _languageService = languageService;
        _languageService.LanguageChanged += (_, _) => RefreshLocalizedTexts();
    }

    public bool IsLoginMode
    {
        get => _isLoginMode;
        set
        {
            if (SetProperty(ref _isLoginMode, value))
            {
                OnPropertyChanged(nameof(IsSignUpMode));
                OnPropertyChanged(nameof(PrimaryButtonText));
            }
        }
    }

    public bool IsSignUpMode => !IsLoginMode;

    public string Identifier
    {
        get => _identifier;
        set => SetProperty(ref _identifier, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string BrandTitleText => _languageService.GetText("brand_title");
    public string PortalSubtitleText => _languageService.GetText("login_portal_subtitle");
    public string LoginTabText => _languageService.GetText("login_tab");
    public string SignUpTabText => _languageService.GetText("signup_tab");
    public string IdentifierPlaceholderText => _languageService.GetText("login_identifier_placeholder");
    public string PasswordPlaceholderText => _languageService.GetText("login_password_placeholder");
    public string ForgotPasswordText => _languageService.GetText("login_forgot_password");
    public string PrimaryButtonText => IsLoginMode
        ? _languageService.GetText("login_button")
        : _languageService.GetText("signup_button");
    public string GoogleLoginText => _languageService.GetText("login_google");
    public string FacebookLoginText => _languageService.GetText("login_facebook");
    public string AppleLoginText => _languageService.GetText("login_apple");
    public string CreateAccountText => _languageService.GetText("login_create_account");
    public string CurrentLanguageDisplayName => _languageService.GetLanguageDefinition(_languageService.CurrentLanguage).DisplayName;
    public string CurrentLanguageFlag => _languageService.GetLanguageDefinition(_languageService.CurrentLanguage).Flag;

    public AsyncCommand<string> SwitchModeCommand => new(SwitchModeAsync);
    public AsyncCommand LoginCommand => new(GoToHomeAsync);
    public AsyncCommand<string> SocialLoginCommand => new(_ => GoToHomeAsync());
    public AsyncCommand OpenLanguageSelectionCommand => new(OpenLanguageSelectionAsync);
    public AsyncCommand OpenCreateAccountCommand => new(async () =>
    {
        IsLoginMode = false;
        await Task.CompletedTask;
    });

    public async Task LoadAsync()
    {
        if (!_isLoaded)
        {
            var profile = await _dataService.GetUserProfileAsync();
            if (string.IsNullOrWhiteSpace(Identifier))
            {
                Identifier = !string.IsNullOrWhiteSpace(profile.Email) ? profile.Email : profile.Phone;
            }

            _isLoaded = true;
        }

        RefreshLocalizedTexts();
    }

    private Task SwitchModeAsync(string? mode)
    {
        IsLoginMode = !string.Equals(mode, "signup", StringComparison.OrdinalIgnoreCase);
        return Task.CompletedTask;
    }

    private Task GoToHomeAsync()
        => Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.HomeMap));

    private Task OpenLanguageSelectionAsync()
        => Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.LanguageSelection));

    private void RefreshLocalizedTexts()
    {
        OnPropertyChanged(nameof(BrandTitleText));
        OnPropertyChanged(nameof(PortalSubtitleText));
        OnPropertyChanged(nameof(LoginTabText));
        OnPropertyChanged(nameof(SignUpTabText));
        OnPropertyChanged(nameof(IdentifierPlaceholderText));
        OnPropertyChanged(nameof(PasswordPlaceholderText));
        OnPropertyChanged(nameof(ForgotPasswordText));
        OnPropertyChanged(nameof(PrimaryButtonText));
        OnPropertyChanged(nameof(GoogleLoginText));
        OnPropertyChanged(nameof(FacebookLoginText));
        OnPropertyChanged(nameof(AppleLoginText));
        OnPropertyChanged(nameof(CreateAccountText));
        OnPropertyChanged(nameof(CurrentLanguageDisplayName));
        OnPropertyChanged(nameof(CurrentLanguageFlag));
    }
}
