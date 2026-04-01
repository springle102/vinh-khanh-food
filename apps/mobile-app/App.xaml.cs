using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp;

public partial class App : Application
{
    public App(IServiceProvider services)
    {
        InitializeComponent();
        ServiceHelper.Services = services;
        services.GetRequiredService<IAppLanguageService>().InitializeAsync().GetAwaiter().GetResult();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}
