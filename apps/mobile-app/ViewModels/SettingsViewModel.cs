using System.Collections.ObjectModel;
using Microsoft.Maui.ApplicationModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class SettingsViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;
    private readonly IOfflinePackageService _offlinePackageService;
    private readonly AsyncCommand<LanguageOption> _selectLanguageCommand;
    private readonly AsyncCommand _downloadOfflinePackageCommand;
    private readonly AsyncCommand _updateOfflinePackageCommand;
    private readonly AsyncCommand _deleteOfflinePackageCommand;
    private readonly AsyncCommand _cancelOfflinePackageCommand;
    private OfflinePackageState _offlinePackageState = OfflinePackageState.Empty;
    private long? _availableFreeSpaceBytes;

    public SettingsViewModel(
        IFoodStreetDataService dataService,
        IOfflinePackageService offlinePackageService,
        IAppLanguageService languageService)
        : base(languageService)
    {
        _dataService = dataService;
        _offlinePackageService = offlinePackageService;
        _selectLanguageCommand = new(SelectLanguageAsync);
        _downloadOfflinePackageCommand = new(DownloadOfflinePackageAsync, () => !_offlinePackageState.IsBusy && _offlinePackageState.CanReachServer);
        _updateOfflinePackageCommand = new(UpdateOfflinePackageAsync, CanUpdateOfflinePackage);
        _deleteOfflinePackageCommand = new(DeleteOfflinePackageAsync, () => !_offlinePackageState.IsBusy && _offlinePackageState.IsInstalled);
        _cancelOfflinePackageCommand = new(CancelOfflinePackageAsync, () => _offlinePackageState.IsBusy);
        _offlinePackageService.StateChanged += OnOfflinePackageStateChanged;
    }

    public ObservableCollection<LanguageOption> Languages { get; } = [];

    public string HeaderTitleText => LanguageService.GetText("settings_title");
    public string LanguageTitleText => LanguageService.GetText("settings_language_title");
    public string OfflinePackageTitleText => LanguageService.GetText("settings_offline_package_title");
    public string OfflinePackageDescriptionText => LanguageService.GetText("settings_offline_package_description");
    public string OfflinePackageStatusCaptionText => LanguageService.GetText("settings_offline_package_status_label");
    public string OfflinePackageSizeCaptionText => LanguageService.GetText("settings_offline_package_size_label");
    public string OfflinePackageUpdatedCaptionText => LanguageService.GetText("settings_offline_package_updated_label");
    public string OfflinePackageVersionCaptionText => LanguageService.GetText("settings_offline_package_version_label");
    public string OfflinePackageSpaceCaptionText => LanguageService.GetText("settings_offline_package_space_label");
    public string OfflinePackagePoiCountCaptionText => LanguageService.GetText("settings_offline_package_poi_count");
    public string OfflinePackageAudioCountCaptionText => LanguageService.GetText("settings_offline_package_audio_count");
    public string OfflinePackageImageCountCaptionText => LanguageService.GetText("settings_offline_package_image_count");
    public string OfflinePackageTourCountCaptionText => LanguageService.GetText("settings_offline_package_tour_count");
    public string OfflinePackageDownloadButtonText => LanguageService.GetText("settings_offline_package_download_button");
    public string OfflinePackageUpdateButtonText => LanguageService.GetText("settings_offline_package_update_button");
    public string OfflinePackageDeleteButtonText => LanguageService.GetText("settings_offline_package_delete_button");
    public string OfflinePackageCancelButtonText => LanguageService.GetText("settings_offline_package_cancel_button");

    public string OfflinePackageStatusText => _offlinePackageState.Status switch
    {
        OfflinePackageLifecycleStatus.Preparing => LanguageService.GetText("settings_offline_package_status_preparing"),
        OfflinePackageLifecycleStatus.Downloading => string.Format(
            LanguageService.CurrentCulture,
            LanguageService.GetText("settings_offline_package_status_downloading"),
            _offlinePackageState.ProgressPercent),
        OfflinePackageLifecycleStatus.Validating => LanguageService.GetText("settings_offline_package_status_validating"),
        OfflinePackageLifecycleStatus.Installing => LanguageService.GetText("settings_offline_package_status_installing"),
        OfflinePackageLifecycleStatus.Completed => LanguageService.GetText("settings_offline_package_status_completed"),
        OfflinePackageLifecycleStatus.Canceled => LanguageService.GetText("settings_offline_package_status_canceled"),
        OfflinePackageLifecycleStatus.Error => LanguageService.GetText("settings_offline_package_status_error"),
        OfflinePackageLifecycleStatus.Deleting => LanguageService.GetText("settings_offline_package_status_deleting"),
        _ when _offlinePackageState.IsInstalled => LanguageService.GetText("settings_offline_package_status_ready"),
        _ => LanguageService.GetText("settings_offline_package_status_not_installed")
    };

    public string OfflinePackageSizeText => _offlinePackageState.Metadata?.PackageSizeBytes > 0
        ? FormatFileSize(_offlinePackageState.Metadata.PackageSizeBytes)
        : LanguageService.GetText("settings_offline_package_size_pending");

    public string OfflinePackageUpdatedText => _offlinePackageState.Metadata?.LastUpdatedAtUtc > DateTimeOffset.MinValue
        ? _offlinePackageState.Metadata!.LastUpdatedAtUtc.ToLocalTime().ToString("g", LanguageService.CurrentCulture)
        : LanguageService.GetText("settings_offline_package_not_downloaded");

    public string OfflinePackageVersionText => !string.IsNullOrWhiteSpace(_offlinePackageState.InstalledVersion)
        ? _offlinePackageState.InstalledVersion
        : LanguageService.GetText("settings_offline_package_not_downloaded");

    public string OfflinePackageSpaceText => _availableFreeSpaceBytes.HasValue
        ? FormatFileSize(_availableFreeSpaceBytes.Value)
        : LanguageService.GetText("settings_offline_package_space_unknown");

    public string OfflinePackageProgressText => _offlinePackageState.IsBusy
        ? string.Format(
            LanguageService.CurrentCulture,
            LanguageService.GetText("settings_offline_package_progress_format"),
            _offlinePackageState.DownloadedFileCount,
            _offlinePackageState.TotalFileCount,
            _offlinePackageState.ProgressPercent)
        : string.Empty;

    public string OfflinePackageUpdateHintText => !_offlinePackageState.CanReachServer
        ? LanguageService.GetText("settings_offline_package_update_no_network")
        : _offlinePackageState.IsUpdateAvailable
            ? LanguageService.GetText("settings_offline_package_update_available")
            : LanguageService.GetText("settings_offline_package_update_latest");

    public string OfflinePackageErrorText => _offlinePackageState.ErrorMessage;

    public int OfflinePackagePoiCount => _offlinePackageState.Metadata?.PoiCount ?? 0;
    public int OfflinePackageAudioCount => _offlinePackageState.Metadata?.AudioCount ?? 0;
    public int OfflinePackageImageCount => _offlinePackageState.Metadata?.ImageCount ?? 0;
    public int OfflinePackageTourCount => _offlinePackageState.Metadata?.TourCount ?? 0;
    public double OfflinePackageProgressValue => _offlinePackageState.ProgressFraction;
    public bool IsOfflinePackageBusy => _offlinePackageState.IsBusy;
    public bool IsOfflinePackageInstalled => _offlinePackageState.IsInstalled;
    public bool HasOfflinePackageError => !string.IsNullOrWhiteSpace(_offlinePackageState.ErrorMessage);
    public bool HasOfflinePackageProgress => _offlinePackageState.IsBusy;
    public bool HasOfflinePackageSpaceInfo => _availableFreeSpaceBytes.HasValue;
    public bool IsOfflinePackageUpdateAvailable => _offlinePackageState.IsUpdateAvailable;
    public bool HasOfflinePackageUpdateHint => IsOfflinePackageInstalled || !_offlinePackageState.CanReachServer;
    public bool ShowOfflinePackageDownloadButton => !IsOfflinePackageInstalled;
    public bool ShowOfflinePackageInstalledActions => IsOfflinePackageInstalled;
    public bool ShowOfflinePackageUpdateButton =>
        IsOfflinePackageInstalled &&
        (_offlinePackageState.IsUpdateAvailable || _offlinePackageState.Status == OfflinePackageLifecycleStatus.Error);

    public AsyncCommand<LanguageOption> SelectLanguageCommand => _selectLanguageCommand;
    public AsyncCommand DownloadOfflinePackageCommand => _downloadOfflinePackageCommand;
    public AsyncCommand UpdateOfflinePackageCommand => _updateOfflinePackageCommand;
    public AsyncCommand DeleteOfflinePackageCommand => _deleteOfflinePackageCommand;
    public AsyncCommand CancelOfflinePackageCommand => _cancelOfflinePackageCommand;

    public async Task LoadAsync()
        => await LoadSettingsAsync();

    public async Task StartOfflineDownloadAsync()
        => await DownloadOfflinePackageAsync();

    protected override async Task ReloadLocalizedStateAsync()
    {
        Languages.ReplaceRange(await _dataService.GetLanguagesAsync());
        RefreshOfflinePackageBindings();
    }

    private async Task LoadSettingsAsync()
    {
        await _dataService.EnsureAllowedLanguageSelectionAsync();
        Languages.ReplaceRange(await _dataService.GetLanguagesAsync());
        _availableFreeSpaceBytes = _offlinePackageService.TryGetAvailableFreeSpaceBytes();
        ApplyOfflinePackageState(await _offlinePackageService.RefreshStatusAsync());
        RefreshLocalizedBindings();
    }

    private async Task SelectLanguageAsync(LanguageOption? language)
    {
        if (language is null)
        {
            return;
        }

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
    }

    private async Task DownloadOfflinePackageAsync()
    {
        var nextState = await _offlinePackageService.DownloadOrUpdateAsync();
        ApplyOfflinePackageState(nextState);
        if (nextState.Status == OfflinePackageLifecycleStatus.Completed && Shell.Current is not null)
        {
            await Shell.Current.DisplayAlertAsync(
                LanguageService.GetText("settings_offline_package_success_title"),
                LanguageService.GetText("settings_offline_package_success_message"),
                LanguageService.GetText("common_ok"));
        }
    }

    private async Task UpdateOfflinePackageAsync()
        => await DownloadOfflinePackageAsync();

    private async Task DeleteOfflinePackageAsync()
    {
        if (Shell.Current is null)
        {
            return;
        }

        var shouldDelete = await Shell.Current.DisplayAlertAsync(
            LanguageService.GetText("settings_offline_package_delete_title"),
            LanguageService.GetText("settings_offline_package_delete_message"),
            LanguageService.GetText("settings_offline_package_delete_confirm"),
            LanguageService.GetText("common_cancel"));
        if (!shouldDelete)
        {
            return;
        }

        ApplyOfflinePackageState(await _offlinePackageService.DeleteAsync());
    }

    private async Task CancelOfflinePackageAsync()
    {
        await _offlinePackageService.CancelAsync();
        ApplyOfflinePackageState(await _offlinePackageService.RefreshStatusAsync());
    }

    private void OnOfflinePackageStateChanged(object? sender, OfflinePackageState state)
        => MainThread.BeginInvokeOnMainThread(() => ApplyOfflinePackageState(state));

    private void ApplyOfflinePackageState(OfflinePackageState state)
    {
        _offlinePackageState = state;
        _availableFreeSpaceBytes = _offlinePackageService.TryGetAvailableFreeSpaceBytes();
        RefreshOfflinePackageBindings();
    }

    private void RefreshOfflinePackageBindings()
    {
        RefreshLocalizedBindings();
        _downloadOfflinePackageCommand.NotifyCanExecuteChanged();
        _updateOfflinePackageCommand.NotifyCanExecuteChanged();
        _deleteOfflinePackageCommand.NotifyCanExecuteChanged();
        _cancelOfflinePackageCommand.NotifyCanExecuteChanged();
    }

    private bool CanUpdateOfflinePackage()
        => !_offlinePackageState.IsBusy &&
           _offlinePackageState.IsInstalled &&
           _offlinePackageState.CanReachServer &&
           (_offlinePackageState.IsUpdateAvailable || _offlinePackageState.Status == OfflinePackageLifecycleStatus.Error);

    private string FormatFileSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 MB";
        }

        var units = new[] { "B", "KB", "MB", "GB" };
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        var format = unitIndex == 0 ? "0" : unitIndex == 1 ? "0.0" : "0.##";
        return string.Format(LanguageService.CurrentCulture, "{0:" + format + "} {1}", value, units[unitIndex]);
    }
}
