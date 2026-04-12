using Microsoft.Maui.ApplicationModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Services;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp;

public partial class AppShell : Shell
{
    private readonly string _startupRoute;
    private readonly AppBottomBarViewModel _bottomBarViewModel;
    private readonly IAppLanguageService _languageService;

    public AppShell(string startupRoute)
    {
        InitializeComponent();
        _startupRoute = startupRoute;
        _bottomBarViewModel = ServiceHelper.GetService<AppBottomBarViewModel>();
        _languageService = ServiceHelper.GetService<IAppLanguageService>();
        _bottomBarViewModel.AttachShell(this);
        _languageService.LanguageChanged += OnLanguageChanged;
        ApplyLocalization();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        Dispatcher.Dispatch(async () =>
        {
            var route = AppRoutes.Root(_startupRoute);
            if (!string.Equals(CurrentState?.Location?.OriginalString, route, StringComparison.Ordinal))
            {
                await GoToAsync(route, false);
            }

            _bottomBarViewModel.SyncWithShell();
        });
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => MainThread.BeginInvokeOnMainThread(ApplyLocalization);

    private void ApplyLocalization()
        => Title = _languageService.GetText("app_title");
}
