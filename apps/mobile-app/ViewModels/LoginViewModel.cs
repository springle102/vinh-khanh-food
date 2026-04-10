using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class LoginViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;
    private bool _isLoginMode = true;
    private string _identifier = string.Empty;
    private string _signUpName = string.Empty;
    private string _signUpUsername = string.Empty;
    private string _signUpEmail = string.Empty;
    private string _signUpPhone = string.Empty;
    private string _password = string.Empty;
    private string _confirmPassword = string.Empty;
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

    public string SignUpName
    {
        get => _signUpName;
        set => SetProperty(ref _signUpName, value);
    }

    public string SignUpEmail
    {
        get => _signUpEmail;
        set => SetProperty(ref _signUpEmail, value);
    }

    public string SignUpUsername
    {
        get => _signUpUsername;
        set => SetProperty(ref _signUpUsername, value);
    }

    public string SignUpPhone
    {
        get => _signUpPhone;
        set => SetProperty(ref _signUpPhone, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => SetProperty(ref _confirmPassword, value);
    }

    public string BrandTitleText => LanguageService.GetText("brand_title");
    public string PortalSubtitleText => LanguageService.GetText("login_portal_subtitle");
    public string LoginTabText => LanguageService.GetText("login_tab");
    public string SignUpTabText => LanguageService.GetText("signup_tab");
    public string IdentifierPlaceholderText => LanguageService.GetText("login_identifier_placeholder");
    public string SignUpNamePlaceholderText => LanguageService.GetText("signup_name_placeholder");
    public string SignUpUsernamePlaceholderText => LanguageService.GetText("signup_username_placeholder");
    public string SignUpEmailPlaceholderText => LanguageService.GetText("signup_email_placeholder");
    public string SignUpPhonePlaceholderText => LanguageService.GetText("signup_phone_placeholder");
    public string PasswordPlaceholderText => LanguageService.GetText("login_password_placeholder");
    public string ConfirmPasswordPlaceholderText => LanguageService.GetText("signup_confirm_password_placeholder");
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
    public AsyncCommand LoginCommand => new(SubmitAsync);
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
            await _dataService.EnsureAllowedLanguageSelectionAsync();
            _isLoaded = true;
        }

        RefreshLocalizedBindings();
    }

    private Task SwitchModeAsync(string? mode)
    {
        IsLoginMode = !string.Equals(mode, "signup", StringComparison.OrdinalIgnoreCase);
        return Task.CompletedTask;
    }

    private async Task SubmitAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (IsLoginMode)
            {
                await LoginAsync();
                return;
            }

            await SignUpAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoginAsync()
    {
        var normalizedIdentifier = Identifier?.Trim() ?? string.Empty;
        var normalizedPassword = Password?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedIdentifier) || string.IsNullOrWhiteSpace(normalizedPassword))
        {
            await Shell.Current.DisplayAlertAsync(
                LoginTabText,
                LanguageService.GetText("login_validation_required"),
                LanguageService.GetText("common_ok"));
            return;
        }

        try
        {
            var selectedProfile = await _dataService.LoginCustomerAsync(normalizedIdentifier, normalizedPassword);
            if (selectedProfile is null)
            {
                await Shell.Current.DisplayAlertAsync(
                    LoginTabText,
                    LanguageService.GetText("login_invalid_credentials"),
                    LanguageService.GetText("common_ok"));
                return;
            }
        }
        catch (MobileBackendConnectionException)
        {
            await Shell.Current.DisplayAlertAsync(
                LoginTabText,
                LanguageService.GetText("login_backend_unavailable"),
                LanguageService.GetText("common_ok"));
            return;
        }
        catch (InvalidOperationException exception)
        {
            await Shell.Current.DisplayAlertAsync(
                LoginTabText,
                exception.Message,
                LanguageService.GetText("common_ok"));
            return;
        }

        Identifier = normalizedIdentifier;
        Password = string.Empty;

        await Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.HomeMap));
    }

    private async Task GoToHomeAsync()
    {
        await _dataService.EnsureAllowedLanguageSelectionAsync();

        await Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.HomeMap));
    }

    private async Task SignUpAsync()
    {
        var normalizedName = SignUpName?.Trim() ?? string.Empty;
        var normalizedUsername = SignUpUsername?.Trim() ?? string.Empty;
        var normalizedEmail = SignUpEmail?.Trim() ?? string.Empty;
        var normalizedPhone = SignUpPhone?.Trim() ?? string.Empty;
        var normalizedPassword = Password?.Trim() ?? string.Empty;
        var normalizedConfirmPassword = ConfirmPassword?.Trim() ?? string.Empty;

        var validationError = ValidateRegistrationInput(
            normalizedName,
            normalizedUsername,
            normalizedEmail,
            normalizedPhone,
            normalizedPassword,
            normalizedConfirmPassword);

        if (!string.IsNullOrWhiteSpace(validationError))
        {
            await Shell.Current.DisplayAlertAsync(
                SignUpTabText,
                validationError,
                LanguageService.GetText("common_ok"));
            return;
        }

        await _dataService.RegisterUserProfileAsync(new CustomerRegistrationRequest
        {
            Name = normalizedName,
            Username = normalizedUsername,
            Email = normalizedEmail,
            Phone = normalizedPhone,
            Password = normalizedPassword,
            PreferredLanguage = LanguageService.CurrentLanguage,
            Country = "VN"
        });

        Identifier = normalizedUsername;
        Password = string.Empty;
        ConfirmPassword = string.Empty;

        await Shell.Current.DisplayAlertAsync(
            SignUpTabText,
            LanguageService.GetText("signup_success_message"),
            LanguageService.GetText("common_ok"));

        await Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.HomeMap));
    }

    private string? ValidateRegistrationInput(
        string name,
        string username,
        string email,
        string phone,
        string password,
        string confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return LanguageService.GetText("signup_validation_name");
        }

        if (!IsValidUsername(username))
        {
            return LanguageService.GetText("signup_validation_username");
        }

        if (!IsValidEmail(email))
        {
            return LanguageService.GetText("signup_validation_email");
        }

        if (!IsValidPhone(phone))
        {
            return LanguageService.GetText("signup_validation_phone");
        }

        if (password.Length < 6)
        {
            return LanguageService.GetText("signup_validation_password");
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            return LanguageService.GetText("signup_validation_confirm_password");
        }

        return null;
    }

    private static bool IsValidEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var atIndex = value.IndexOf('@');
        return atIndex > 0 && atIndex < value.Length - 3 && value[(atIndex + 1)..].Contains('.');
    }

    private static bool IsValidPhone(string value)
    {
        var digits = string.Concat((value ?? string.Empty).Where(char.IsDigit));
        return digits.Length >= 8 && digits.Length <= 15;
    }

    private static bool IsValidUsername(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 3)
        {
            return false;
        }

        return value.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-');
    }

    private Task OpenLanguageSelectionAsync()
        => Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.LanguageSelection));
}
