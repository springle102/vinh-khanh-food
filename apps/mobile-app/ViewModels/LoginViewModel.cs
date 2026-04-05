using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class LoginViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;
    private bool _isLoginMode = true;
    private string _identifier = string.Empty;
    private string _password = string.Empty;
    private bool _isLoaded;

    public LoginViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService)
        : base(languageService)
    {
        _dataService = dataService;
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

    public string BrandTitleText => LanguageService.GetText("brand_title");
    public string PortalSubtitleText => LanguageService.GetText("login_portal_subtitle");
    public string LoginTabText => LanguageService.GetText("login_tab");
    public string SignUpTabText => LanguageService.GetText("signup_tab");
    public string IdentifierPlaceholderText => LanguageService.GetText("login_identifier_placeholder");
    public string PasswordPlaceholderText => LanguageService.GetText("login_password_placeholder");
    public string ForgotPasswordText => LanguageService.GetText("login_forgot_password");
    public string PrimaryButtonText => IsLoginMode
        ? LanguageService.GetText("login_button")
        : LanguageService.GetText("signup_button");
    public string GoogleLoginText => LanguageService.GetText("login_google");
    public string FacebookLoginText => LanguageService.GetText("login_facebook");
    public string AppleLoginText => LanguageService.GetText("login_apple");
    public string CreateAccountText => LanguageService.GetText("login_create_account");
    public string CurrentLanguageDisplayName => LanguageService.GetLanguageDefinition(LanguageService.CurrentLanguage).DisplayName;
    public string CurrentLanguageFlag => LanguageService.GetLanguageDefinition(LanguageService.CurrentLanguage).Flag;

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

        RefreshLocalizedBindings();
    }

    private Task SwitchModeAsync(string? mode)
    {
        IsLoginMode = !string.Equals(mode, "signup", StringComparison.OrdinalIgnoreCase);
        return Task.CompletedTask;
    }

    private async Task GoToHomeAsync()
    {
        var normalizedIdentifier = Identifier?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedIdentifier))
        {
            var selectedProfile = await _dataService.SelectUserProfileAsync(normalizedIdentifier);
            if (selectedProfile is null)
            {
                await Shell.Current.DisplayAlertAsync(
                    LanguageService.GetText("login_tab"),
                    LanguageService.GetText("login_identifier_not_found"),
                    LanguageService.GetText("common_ok"));
                return;
            }

            Identifier = !string.IsNullOrWhiteSpace(selectedProfile.Email)
                ? selectedProfile.Email
                : selectedProfile.Phone;
        }

        await Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.HomeMap));
    }

    private Task OpenLanguageSelectionAsync()
        => Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.LanguageSelection));
}
