namespace VinhKhanh.MobileApp;

public partial class AppShell : Shell
{
    private readonly string _startupRoute;

    public AppShell(string startupRoute)
    {
        InitializeComponent();
        _startupRoute = startupRoute;
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
        });
    }
}
