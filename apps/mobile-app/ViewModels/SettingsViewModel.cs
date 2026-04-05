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
    private UserProfileCard? _profile;
    private string _fullName = string.Empty;
    private string _email = string.Empty;
    private string _phone = string.Empty;
    private bool _isEditingProfile;
    private bool _isSavingProfile;
    private string _profileMessage = string.Empty;
    private bool _isProfileMessageError;

    public SettingsViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService)
        : base(languageService)
    {
        _dataService = dataService;
        _startEditProfileCommand = new(StartEditProfileAsync, () => Profile is not null && !IsEditingProfile && !IsSavingProfile);
        _saveProfileCommand = new(SaveProfileAsync, CanSaveProfile);
        _cancelEditProfileCommand = new(CancelEditProfileAsync, () => IsEditingProfile && !IsSavingProfile);
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

    public bool HasProfile => Profile is not null;
    public bool IsViewingProfile => !IsEditingProfile;
    public bool HasProfileMessage => !string.IsNullOrWhiteSpace(ProfileMessage);

    public string HeaderTitleText => LanguageService.GetText("settings_title");
    public string AccountTitleText => LanguageService.GetText("settings_account");
    public string LanguageTitleText => LanguageService.GetText("settings_language_title");
    public string UserNameLabelText => LanguageService.GetText("settings_user_name");
    public string ContactLabelText => LanguageService.GetText("settings_contact");
    public string LogoutText => LanguageService.GetText("settings_logout");
    public string EditProfileText => LanguageService.GetText("settings_profile_edit");
    public string SaveProfileText => LanguageService.GetText("settings_profile_save");
    public string CancelProfileEditText => LanguageService.GetText("settings_profile_cancel");
    public string NamePlaceholderText => LanguageService.GetText("settings_profile_name_placeholder");
    public string EmailPlaceholderText => LanguageService.GetText("settings_profile_email_placeholder");
    public string PhonePlaceholderText => LanguageService.GetText("settings_profile_phone_placeholder");

    public AsyncCommand<LanguageOption> SelectLanguageCommand => new(SelectLanguageAsync);
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
        Profile = await _dataService.GetUserProfileAsync();
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

        await LanguageService.SetLanguageAsync(language.Code);
    }

    protected override async Task ReloadLocalizedStateAsync()
        => await RefreshLocalizedStateAsync();

    private void SyncEditorWithProfile(UserProfileCard? profile)
    {
        FullName = profile?.FullName ?? string.Empty;
        Email = profile?.Email ?? string.Empty;
        Phone = profile?.Phone ?? string.Empty;
    }

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

    private void RefreshCommandState()
    {
        _startEditProfileCommand.NotifyCanExecuteChanged();
        _saveProfileCommand.NotifyCanExecuteChanged();
        _cancelEditProfileCommand.NotifyCanExecuteChanged();
    }
}
