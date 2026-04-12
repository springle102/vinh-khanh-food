using System.Threading;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class AppBottomBarViewModel : ObservableObject
{
    private readonly IPoiNarrationService _poiNarrationService;
    private readonly SemaphoreSlim _navigationLock = new(1, 1);
    private Shell? _attachedShell;
    private string _currentRoute = string.Empty;
    private AppBottomBarTab _selectedTab = AppBottomBarTab.None;

    public AppBottomBarViewModel(IPoiNarrationService poiNarrationService)
    {
        _poiNarrationService = poiNarrationService;
        NavigateToPoiCommand = new(() => NavigateToAsync(AppRoutes.HomeMap));
        NavigateToQrCommand = new(() => NavigateToAsync(AppRoutes.QrScanner));
        NavigateToSettingsCommand = new(() => NavigateToAsync(AppRoutes.Settings));
    }

    public string CurrentRoute
    {
        get => _currentRoute;
        private set => SetProperty(ref _currentRoute, value);
    }

    public AppBottomBarTab SelectedTab
    {
        get => _selectedTab;
        private set => SetProperty(ref _selectedTab, value);
    }

    public AsyncCommand NavigateToPoiCommand { get; }

    public AsyncCommand NavigateToQrCommand { get; }

    public AsyncCommand NavigateToSettingsCommand { get; }

    public void AttachShell(Shell shell)
    {
        if (ReferenceEquals(_attachedShell, shell))
        {
            SyncWithShell();
            return;
        }

        if (_attachedShell is not null)
        {
            _attachedShell.Navigated -= OnShellNavigated;
        }

        _attachedShell = shell;
        _attachedShell.Navigated += OnShellNavigated;
        SyncWithShell();
    }

    public void SyncWithShell()
    {
        var route = _attachedShell?.CurrentState?.Location?.OriginalString
            ?? Shell.Current?.CurrentState?.Location?.OriginalString;
        ApplyRoute(route);
    }

    private async Task NavigateToAsync(string route)
    {
        var shell = _attachedShell ?? Shell.Current;
        if (shell is null)
        {
            return;
        }

        if (!await _navigationLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            ApplyRoute(shell.CurrentState?.Location?.OriginalString);
            var currentRoute = AppRoutes.NormalizeShellRoute(shell.CurrentState?.Location?.OriginalString);
            if (string.Equals(currentRoute, route, StringComparison.Ordinal))
            {
                ApplyRoute(route);
                return;
            }

            await _poiNarrationService.StopAsync();
            await shell.GoToAsync(AppRoutes.Root(route));
            ApplyRoute(shell.CurrentState?.Location?.OriginalString);
        }
        finally
        {
            _navigationLock.Release();
        }
    }

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
        => ApplyRoute((sender as Shell)?.CurrentState?.Location?.OriginalString ?? e.Current?.Location?.OriginalString);

    private void ApplyRoute(string? route)
    {
        var normalizedRoute = AppRoutes.NormalizeShellRoute(route);
        if (string.IsNullOrWhiteSpace(normalizedRoute))
        {
            return;
        }

        CurrentRoute = normalizedRoute;
        SelectedTab = AppRoutes.ResolveBottomBarTab(normalizedRoute);
    }
}
