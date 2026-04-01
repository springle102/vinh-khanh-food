using Microsoft.Extensions.Logging;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp;

public partial class App : Application
{
    private readonly IAppLanguageService _languageService;
    private readonly ILogger<App>? _logger;
    private bool _languageInitializationStarted;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        ServiceHelper.Services = services;
        _languageService = services.GetRequiredService<IAppLanguageService>();
        _logger = services.GetService<ILogger<App>>();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());
        window.Created += OnWindowCreated;
        return window;
    }

    private void OnWindowCreated(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Created -= OnWindowCreated;
        }

        if (_languageInitializationStarted)
        {
            return;
        }

        _languageInitializationStarted = true;
        _ = InitializeLanguageAsync();
    }

    private async Task InitializeLanguageAsync()
    {
        try
        {
            await _languageService.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unable to initialize app language during startup.");
        }
    }
}
