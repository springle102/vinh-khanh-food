using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using VinhKhanh.MobileApp.Services;
using VinhKhanh.MobileApp.ViewModels;
#if ANDROID
using Microsoft.Maui.Handlers;
#endif

namespace VinhKhanh.MobileApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .AddAudio()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if ANDROID
        builder.ConfigureMauiHandlers(handlers =>
        {
            WebViewHandler.Mapper.AppendToMapping("VinhKhanhMapNetworkSettings", (handler, _) =>
            {
                var settings = handler.PlatformView.Settings;
                settings.JavaScriptEnabled = true;
                settings.DomStorageEnabled = true;
                settings.LoadsImagesAutomatically = true;
                settings.BlockNetworkImage = false;
                settings.BlockNetworkLoads = false;
                settings.AllowContentAccess = true;
                settings.AllowFileAccess = true;
            });
        });
#endif

        builder.Services.AddSingleton<IAppLanguageService, AppLanguageService>();
        builder.Services.AddSingleton<IMobileApiBaseUrlService, MobileApiBaseUrlService>();

        builder.Services.AddSingleton<IOfflineStorageService, OfflineStorageService>();
        builder.Services.AddSingleton<IBundledOfflinePackageSeedService, BundledOfflinePackageSeedService>();
        builder.Services.AddSingleton<MobileOfflineDatabaseService>();
        builder.Services.AddSingleton<IMobileOfflineDatabaseService>(sp => sp.GetRequiredService<MobileOfflineDatabaseService>());
        builder.Services.AddSingleton<IMobileDatasetRepository>(sp => sp.GetRequiredService<MobileOfflineDatabaseService>());
        builder.Services.AddSingleton<IMobileSyncQueueRepository>(sp => sp.GetRequiredService<MobileOfflineDatabaseService>());
        builder.Services.AddSingleton<OfflinePackageService>();
        builder.Services.AddSingleton<IOfflinePackageService>(sp => sp.GetRequiredService<OfflinePackageService>());
        builder.Services.AddSingleton<FoodStreetApiDataService>();
        builder.Services.AddSingleton<IFoodStreetDataService>(sp => sp.GetRequiredService<FoodStreetApiDataService>());
        builder.Services.AddSingleton<IMobileAnalyticsService, MobileAnalyticsService>();
        builder.Services.AddSingleton<AppPresenceService>();
        builder.Services.AddSingleton<PoiAudioPlaybackService>();
        builder.Services.AddSingleton<IPoiAudioPlaybackService>(sp => sp.GetRequiredService<PoiAudioPlaybackService>());
        builder.Services.AddSingleton<IAppLifecycleAwareService>(sp => sp.GetRequiredService<AppPresenceService>());
        builder.Services.AddSingleton<IAppLifecycleAwareService>(sp => sp.GetRequiredService<OfflinePackageService>());
        builder.Services.AddSingleton<IAppLifecycleAwareService>(sp => sp.GetRequiredService<FoodStreetApiDataService>());
        builder.Services.AddSingleton<IAppLifecycleAwareService>(sp => sp.GetRequiredService<PoiAudioPlaybackService>());
        builder.Services.AddSingleton<ILocationService, DeviceLocationService>();
        builder.Services.AddSingleton<IPoiProximityService, PoiProximityService>();
        builder.Services.AddSingleton<IRouteService, HttpRouteService>();
        builder.Services.AddSingleton<IRoutePoiFilterService, RoutePoiFilterService>();
        builder.Services.AddSingleton<ISimulationService, SimulationService>();
        builder.Services.AddSingleton<IAutoNarrationService, AutoNarrationService>();
        builder.Services.AddSingleton<ITourStateService, TourStateService>();
        builder.Services.AddSingleton<AppBottomBarViewModel>();

        builder.Services.AddTransient<HomeMapViewModel>();
        builder.Services.AddTransient<DiscoverToursViewModel>();
        builder.Services.AddTransient<MyTourViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
