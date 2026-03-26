using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Plugin.Maui.Audio;
using VinhKhanh.MobileApp.Interfaces;
using VinhKhanh.MobileApp.Services;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;
using ZXing.Net.Maui.Controls;

namespace VinhKhanh.MobileApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseBarcodeReader()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton(AudioManager.Current);

        builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();
        builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
        builder.Services.AddSingleton<IOfflineCacheService, OfflineCacheService>();
        builder.Services.AddSingleton<IGuideApiService, GuideApiService>();
        builder.Services.AddSingleton<INarrationService, NarrationService>();
        builder.Services.AddSingleton<ILocationTrackerService, LocationTrackerService>();

        builder.Services.AddTransient<SplashViewModel>();
        builder.Services.AddTransient<LanguageSelectionViewModel>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<PoiListViewModel>();
        builder.Services.AddTransient<MapViewModel>();
        builder.Services.AddTransient<PoiDetailViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<QrScannerViewModel>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}


