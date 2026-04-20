using Microsoft.Extensions.Logging;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp;

public partial class App : Application
{
    private readonly IAppLanguageService _languageService;
    private readonly IBundledOfflinePackageSeedService _bundledSeedService;
    private readonly IMobileOfflineDatabaseService _offlineDatabaseService;
    private readonly ILogger<App>? _logger;
    private bool _languageInitializationStarted;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        ServiceHelper.Services = services;
        _languageService = services.GetRequiredService<IAppLanguageService>();
        _bundledSeedService = services.GetRequiredService<IBundledOfflinePackageSeedService>();
        _offlineDatabaseService = services.GetRequiredService<IMobileOfflineDatabaseService>();
        _logger = services.GetService<ILogger<App>>();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell(AppRoutes.HomeMap));
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
        _logger?.LogInformation(
            "[AppStart] App window created. currentLanguage={CurrentLanguage}",
            _languageService.CurrentLanguage);
        _ = InitializeLanguageAsync();
    }

    private async Task InitializeLanguageAsync()
    {
        try
        {
            _logger?.LogInformation("[AppStart] Ensuring bundled offline seed is installed.");
            await _bundledSeedService.EnsureInstalledAsync();
            await _offlineDatabaseService.EnsureInitializedAsync();
            await _languageService.InitializeAsync();
            _logger?.LogInformation(
                "[AppStart] App language initialization finished. currentLanguage={CurrentLanguage}",
                _languageService.CurrentLanguage);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unable to initialize app language during startup.");
        }
    }
}
