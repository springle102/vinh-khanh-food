using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp;

public partial class AppShell : Shell
{
    private readonly string _startupRoute;
    private readonly AppBottomBarViewModel _bottomBarViewModel;

    public AppShell(string startupRoute)
    {
        InitializeComponent();
        _startupRoute = startupRoute;
        _bottomBarViewModel = ServiceHelper.GetService<AppBottomBarViewModel>();
        _bottomBarViewModel.AttachShell(this);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        Dispatcher.Dispatch(async () =>
        {
            var route = Helpers.AppRoutes.Root(_startupRoute);
            if (!string.Equals(CurrentState?.Location?.OriginalString, route, StringComparison.Ordinal))
            {
                await GoToAsync(route, false);
            }

            _bottomBarViewModel.SyncWithShell();
        });
    }
}
