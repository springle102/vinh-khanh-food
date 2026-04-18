using Microsoft.Maui.ApplicationModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed partial class HomeMapViewModel
{
    private readonly AsyncCommand _offlineNoticePrimaryActionCommand;
    private readonly AsyncCommand _offlineNoticeSecondaryActionCommand;
    private OfflinePackageState _offlinePackageState = OfflinePackageState.Empty;
    private bool _dismissOfflineNoticeForSession;

    public bool ShowOfflineNotice =>
        !_offlinePackageState.IsInstalled &&
        (!_dismissOfflineNoticeForSession || !_offlinePackageState.CanReachServer);

    public bool ShowOfflineNoticeSecondaryAction => ShowOfflineNotice && _offlinePackageState.CanReachServer;

    public string OfflineNoticeTitleText => _offlinePackageState.CanReachServer
        ? LanguageService.GetText("offline_prompt_title")
        : LanguageService.GetText("offline_prompt_no_network_title");

    public string OfflineNoticeDescriptionText => _offlinePackageState.CanReachServer
        ? LanguageService.GetText("offline_prompt_message")
        : LanguageService.GetText("offline_prompt_no_network_message");

    public string OfflineNoticePrimaryActionText => _offlinePackageState.CanReachServer
        ? LanguageService.GetText("offline_prompt_download_action")
        : LanguageService.GetText("offline_prompt_open_settings_action");

    public string OfflineNoticeSecondaryActionText => LanguageService.GetText("offline_prompt_skip_action");

    public AsyncCommand OfflineNoticePrimaryActionCommand => _offlineNoticePrimaryActionCommand;
    public AsyncCommand OfflineNoticeSecondaryActionCommand => _offlineNoticeSecondaryActionCommand;

    private async Task RefreshOfflinePackageStateAsync()
    {
        ApplyOfflinePackageState(await _offlinePackageService.RefreshStatusAsync(), reloadDataIfNeeded: false);
    }

    private void OnOfflinePackageStateChanged(object? sender, OfflinePackageState state)
        => MainThread.BeginInvokeOnMainThread(() => ApplyOfflinePackageState(state, reloadDataIfNeeded: true));

    private void ApplyOfflinePackageState(OfflinePackageState state, bool reloadDataIfNeeded)
    {
        if (_offlinePackageState.IsInstalled && !state.IsInstalled)
        {
            _dismissOfflineNoticeForSession = false;
        }

        var shouldReload =
            reloadDataIfNeeded &&
            (_offlinePackageState.IsInstalled != state.IsInstalled ||
             !string.Equals(_offlinePackageState.InstalledVersion, state.InstalledVersion, StringComparison.OrdinalIgnoreCase));

        _offlinePackageState = state;
        RefreshOfflineNoticeBindings();

        if (shouldReload)
        {
            _ = ReloadCurrentStateAsync(autoPlayNarrationForSelection: false);
        }
    }

    private void RefreshOfflineNoticeBindings()
    {
        OnPropertyChanged(nameof(ShowOfflineNotice));
        OnPropertyChanged(nameof(ShowOfflineNoticeSecondaryAction));
        OnPropertyChanged(nameof(OfflineNoticeTitleText));
        OnPropertyChanged(nameof(OfflineNoticeDescriptionText));
        OnPropertyChanged(nameof(OfflineNoticePrimaryActionText));
        OnPropertyChanged(nameof(OfflineNoticeSecondaryActionText));
    }

    private async Task OpenOfflinePackageSettingsAsync()
    {
        if (Shell.Current is null)
        {
            return;
        }

        var route = _offlinePackageState.CanReachServer
            ? $"{AppRoutes.Root(AppRoutes.Settings)}?autoDownload=true"
            : AppRoutes.Root(AppRoutes.Settings);
        await Shell.Current.GoToAsync(route);
    }

    private Task DismissOfflineNoticeAsync()
    {
        _dismissOfflineNoticeForSession = true;
        RefreshOfflineNoticeBindings();
        return Task.CompletedTask;
    }
}
