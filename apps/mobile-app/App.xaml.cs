using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp;

public partial class App : Application
{
    private readonly IAppLanguageService _languageService;
    private readonly IBundledOfflinePackageSeedService _bundledSeedService;
    private readonly IMobileOfflineDatabaseService _offlineDatabaseService;
    private readonly IReadOnlyList<IAppLifecycleAwareService> _lifecycleAwareServices;
    private readonly ILogger<App>? _logger;
    private bool _languageInitializationStarted;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        ServiceHelper.Services = services;
        _languageService = services.GetRequiredService<IAppLanguageService>();
        _bundledSeedService = services.GetRequiredService<IBundledOfflinePackageSeedService>();
        _offlineDatabaseService = services.GetRequiredService<IMobileOfflineDatabaseService>();
        _lifecycleAwareServices = services.GetServices<IAppLifecycleAwareService>().ToArray();
        _logger = services.GetService<ILogger<App>>();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell(AppRoutes.HomeMap));
        window.Created += OnWindowCreated;
        window.Activated += OnWindowActivated;
        window.Resumed += OnWindowResumed;
        window.Deactivated += OnWindowDeactivated;
        window.Stopped += OnWindowStopped;
        window.Destroying += OnWindowDestroying;
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

    private void OnWindowActivated(object? sender, EventArgs e)
        => _ = RefreshRuntimeStateAsync("activated");

    private void OnWindowResumed(object? sender, EventArgs e)
        => _ = RefreshRuntimeStateAsync("resumed");

    private void OnWindowDeactivated(object? sender, EventArgs e)
        => _ = PauseRuntimeStateAsync("deactivated");

    private void OnWindowStopped(object? sender, EventArgs e)
        => _ = PauseRuntimeStateAsync("stopped");

    private void OnWindowDestroying(object? sender, EventArgs e)
        => _ = PauseRuntimeStateAsync("destroying");

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

    private async Task RefreshRuntimeStateAsync(string reason)
    {
        try
        {
            _logger?.LogInformation("[AppState] Refreshing runtime services after window {Reason}.", reason);
            foreach (var service in _lifecycleAwareServices)
            {
                await service.HandleAppResumedAsync();
            }
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "[AppState] Failed to refresh runtime services after window {Reason}.", reason);
        }
    }

    private async Task PauseRuntimeStateAsync(string reason)
    {
        try
        {
            _logger?.LogInformation("[AppState] Pausing runtime services after window {Reason}.", reason);
            foreach (var service in _lifecycleAwareServices)
            {
                await service.HandleAppStoppedAsync();
            }
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "[AppState] Failed to pause runtime services after window {Reason}.", reason);
        }
    }
}
