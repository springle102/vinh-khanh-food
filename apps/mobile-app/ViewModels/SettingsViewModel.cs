using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class SettingsViewModel : LocalizedViewModelBase
{
    private static readonly TimeSpan LiveSettingsRefreshInterval = TimeSpan.FromSeconds(2);

    private readonly IFoodStreetDataService _dataService;
    private readonly IMobileSystemSettingsService _systemSettingsService;
    private readonly IAutoNarrationService _autoNarrationService;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly AsyncCommand<LanguageOption> _selectLanguageCommand;
    private readonly AsyncCommand _toggleContactInfoCommand;
    private SystemContactInfo _contactInfo = new();
    private bool _isAutoNarrationEnabled;
    private bool _isContactDetailsVisible;
    private string _loadErrorText = string.Empty;
    private CancellationTokenSource? _liveRefreshCts;

    public SettingsViewModel(
        IFoodStreetDataService dataService,
        IMobileSystemSettingsService systemSettingsService,
        IAutoNarrationService autoNarrationService,
        IAppLanguageService languageService,
        ILogger<SettingsViewModel> logger)
        : base(languageService)
    {
        _dataService = dataService;
        _systemSettingsService = systemSettingsService;
        _autoNarrationService = autoNarrationService;
        _logger = logger;
        _isAutoNarrationEnabled = _autoNarrationService.IsEnabled;
        _selectLanguageCommand = new(SelectLanguageAsync);
        _toggleContactInfoCommand = new(ToggleContactInfoAsync);
    }

    public ObservableCollection<LanguageOption> Languages { get; } = [];

    public string HeaderTitleText => LanguageService.GetText("settings_title");
    public string LanguageTitleText => LanguageService.GetText("settings_language_title");
    public string LanguageSectionText => ToSectionText(LanguageTitleText);
    public string AutoNarrationSectionText => ToSectionText(LanguageService.GetText("settings_auto_narration_section"));
    public string AutoNarrationTitleText => LanguageService.GetText("settings_auto_narration_title");
    public string AutoNarrationDescriptionText => LanguageService.GetText("settings_auto_narration_description");
    public string FeedbackSectionText => ToSectionText(LanguageService.GetText("settings_feedback_section"));
    public string ContactTitleText => LanguageService.GetText("settings_contact_title");
    public string ContactActionText => IsContactDetailsVisible
        ? LanguageService.GetText("settings_contact_hide")
        : LanguageService.GetText("settings_contact_open");
    public string ContactSystemNameCaptionText => LanguageService.GetText("settings_contact_system_name");
    public string ContactPhoneCaptionText => LanguageService.GetText("settings_contact_phone");
    public string ContactEmailCaptionText => LanguageService.GetText("settings_contact_email");
    public string ContactAddressCaptionText => LanguageService.GetText("settings_contact_address");
    public string ContactSupportHoursCaptionText => LanguageService.GetText("settings_contact_support_hours");
    public string ContactInstructionsCaptionText => LanguageService.GetText("settings_contact_instructions");
    public string ContactSystemNameText => RequiredContactValue(_contactInfo.SystemName, _contactInfo.AppName);
    public string ContactPhoneText => RequiredContactValue(_contactInfo.Phone, _contactInfo.SupportPhone);
    public string ContactEmailText => FirstContactValue(_contactInfo.Email, _contactInfo.SupportEmail);
    public string ContactAddressText => FirstContactValue(_contactInfo.Address, _contactInfo.ContactAddress);
    public string ContactSupportHoursText => FirstContactValue(_contactInfo.SupportHours);
    public string ContactInstructionsText => RequiredContactValue(_contactInfo.ComplaintGuide, _contactInfo.SupportInstructions);
    public string ContactUpdatedText => BuildContactUpdatedText();
    public string LoadErrorText => _loadErrorText;
    public bool HasLoadError => !string.IsNullOrWhiteSpace(_loadErrorText);
    public bool IsAutoNarrationEnabled
    {
        get => _isAutoNarrationEnabled;
        set
        {
            if (!SetProperty(ref _isAutoNarrationEnabled, value))
            {
                return;
            }

            _ = SetAutoNarrationEnabledAsync(value);
        }
    }

    public bool IsContactDetailsVisible => _isContactDetailsVisible;
    public bool HasContactEmail => HasContactValue(_contactInfo.Email, _contactInfo.SupportEmail);
    public bool HasContactAddress => HasContactValue(_contactInfo.Address, _contactInfo.ContactAddress);
    public bool HasContactSupportHours => HasContactValue(_contactInfo.SupportHours);
    public bool HasContactUpdatedAt => ResolveContactUpdatedAt() is not null;

    public AsyncCommand<LanguageOption> SelectLanguageCommand => _selectLanguageCommand;
    public AsyncCommand ToggleContactInfoCommand => _toggleContactInfoCommand;

    public async Task LoadAsync()
        => await LoadSettingsAsync();

    public void StartLiveRefresh()
    {
        if (_liveRefreshCts is not null)
        {
            return;
        }

        _liveRefreshCts = new CancellationTokenSource();
        _ = RunLiveRefreshAsync(_liveRefreshCts.Token);
    }

    public void StopLiveRefresh()
    {
        var cts = _liveRefreshCts;
        if (cts is null)
        {
            return;
        }

        _liveRefreshCts = null;
        cts.Cancel();
    }

    protected override async Task ReloadLocalizedStateAsync()
    {
        try
        {
            Languages.ReplaceRange(await _dataService.GetLanguagesAsync());
            _contactInfo = await _systemSettingsService.GetContactSettingsAsync(forceRefresh: true);
            ClearLoadError();
            RefreshLocalizedBindings();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetLoadError(LanguageService.GetText("settings_load_error"));
            _logger.LogError(exception, "[Settings] Localized reload failed.");
        }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            await _dataService.EnsureAllowedLanguageSelectionAsync();
            Languages.ReplaceRange(await _dataService.GetLanguagesAsync());
            _contactInfo = await _systemSettingsService.GetContactSettingsAsync(forceRefresh: true) ?? new SystemContactInfo();
            LogLoadedSettings("initial_load");
            ClearLoadError();
            RefreshLocalizedBindings();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "[Settings] Load failed.");
            SetLoadError(LanguageService.GetText("settings_load_error"));
            _contactInfo ??= new SystemContactInfo();
            RefreshLocalizedBindings();
        }
    }

    private async Task RunLiveRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(LiveSettingsRefreshInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await RefreshLiveSettingsAsync(cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogWarning(exception, "[Settings] Live refresh failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshLiveSettingsAsync(CancellationToken cancellationToken)
    {
        var languages = await _dataService.GetLanguagesAsync(cancellationToken);
        var contactInfo = await _systemSettingsService.GetContactSettingsAsync(
            forceRefresh: true,
            cancellationToken);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Languages.ReplaceRange(languages);
            _contactInfo = contactInfo ?? new SystemContactInfo();
            LogLoadedSettings("live_refresh");
            ClearLoadError();
            RefreshLocalizedBindings();
        });
    }

    private async Task SelectLanguageAsync(LanguageOption? language)
    {
        if (language is null)
        {
            return;
        }

        try
        {
            var normalizedCode = AppLanguage.NormalizeCode(language.Code);
            if (string.Equals(normalizedCode, LanguageService.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
            {
                Languages.ReplaceRange(await _dataService.GetLanguagesAsync());
                return;
            }

            foreach (var item in Languages)
            {
                item.IsSelected = string.Equals(
                    AppLanguage.NormalizeCode(item.Code),
                    normalizedCode,
                    StringComparison.OrdinalIgnoreCase);
            }

            await LanguageService.SetLanguageAsync(normalizedCode);
            ClearLoadError();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "[Settings] Language selection failed.");
            SetLoadError(LanguageService.GetText("settings_load_error"));
        }
    }

    private async Task ToggleContactInfoAsync()
    {
        try
        {
            if (!IsContactDetailsVisible)
            {
                _contactInfo = await _systemSettingsService.GetContactSettingsAsync(forceRefresh: true) ?? new SystemContactInfo();
            }

            _isContactDetailsVisible = !_isContactDetailsVisible;
            ClearLoadError();
            RefreshLocalizedBindings();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "[Settings] Contact panel failed to toggle.");
            _contactInfo ??= new SystemContactInfo();
            SetLoadError(LanguageService.GetText("settings_contact_load_error"));
            _isContactDetailsVisible = true;
            RefreshLocalizedBindings();
        }
    }

    private async Task SetAutoNarrationEnabledAsync(bool isEnabled)
    {
        try
        {
            await _autoNarrationService.SetEnabledAsync(isEnabled);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[Settings] Auto narration toggle failed.");
            var restoredValue = _autoNarrationService.IsEnabled;
            if (_isAutoNarrationEnabled != restoredValue)
            {
                _isAutoNarrationEnabled = restoredValue;
                OnPropertyChanged(nameof(IsAutoNarrationEnabled));
            }

            SetLoadError(LanguageService.GetText("settings_load_error"));
        }
    }

    private void SetLoadError(string message)
    {
        _loadErrorText = string.IsNullOrWhiteSpace(message)
            ? LanguageService.GetText("settings_load_error")
            : message;
        OnPropertyChanged(nameof(LoadErrorText));
        OnPropertyChanged(nameof(HasLoadError));
    }

    private void ClearLoadError()
    {
        if (string.IsNullOrWhiteSpace(_loadErrorText))
        {
            return;
        }

        _loadErrorText = string.Empty;
        OnPropertyChanged(nameof(LoadErrorText));
        OnPropertyChanged(nameof(HasLoadError));
    }

    private string RequiredContactValue(params string?[] values)
    {
        var value = FirstContactValue(values);
        return string.IsNullOrWhiteSpace(value)
            ? LanguageService.GetText("settings_contact_not_updated")
            : value;
    }

    private static bool HasContactValue(params string?[] values)
        => !string.IsNullOrWhiteSpace(FirstContactValue(values));

    private static string FirstContactValue(params string?[] values)
        => values
               .Select(value => value?.Trim() ?? string.Empty)
               .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
           string.Empty;

    private string BuildContactUpdatedText()
    {
        var updatedAt = ResolveContactUpdatedAt();
        return updatedAt is null
            ? string.Empty
            : string.Format(
                LanguageService.CurrentCulture,
                LanguageService.GetText("settings_contact_updated"),
                updatedAt.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", LanguageService.CurrentCulture));
    }

    private DateTimeOffset? ResolveContactUpdatedAt()
        => _contactInfo.UpdatedAtUtc ?? _contactInfo.ContactUpdatedAtUtc;

    private void LogLoadedSettings(string source)
    {
        _logger.LogInformation(
            "[Settings] settings bound to UI. source={Source}; languagesCount={LanguagesCount}; hasSystemName={HasSystemName}; hasPhone={HasPhone}; hasEmail={HasEmail}; hasAddress={HasAddress}; hasComplaintGuide={HasComplaintGuide}",
            source,
            Languages.Count,
            !string.IsNullOrWhiteSpace(ContactSystemNameText) && ContactSystemNameText != LanguageService.GetText("settings_contact_not_updated"),
            !string.IsNullOrWhiteSpace(ContactPhoneText) && ContactPhoneText != LanguageService.GetText("settings_contact_not_updated"),
            HasContactEmail,
            HasContactAddress,
            !string.IsNullOrWhiteSpace(ContactInstructionsText) && ContactInstructionsText != LanguageService.GetText("settings_contact_not_updated"));
    }

    private string ToSectionText(string value)
        => (value ?? string.Empty).Trim().ToUpper(LanguageService.CurrentCulture);
}
