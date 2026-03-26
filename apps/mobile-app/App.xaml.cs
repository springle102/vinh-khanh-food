using VinhKhanh.MobileApp.Helpers;

namespace VinhKhanh.MobileApp;

public partial class App : Application
{
    public App(IServiceProvider services)
    {
        InitializeComponent();
        ServiceHelper.Services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }

    protected override async void OnAppLinkRequestReceived(Uri uri)
    {
        base.OnAppLinkRequestReceived(uri);

        if (!uri.Scheme.Equals("vinhkhanhfoodguide", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 2 && segments[0].Equals("poi", StringComparison.OrdinalIgnoreCase))
        {
            await Shell.Current.GoToAsync($"{nameof(PoiDetailPage)}?slug={Uri.EscapeDataString(segments[1])}");
        }
    }
}
