using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using VinhKhanh.MobileApp.Services;
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
            .AddAudio()
            .UseBarcodeReader()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<IAppLanguageService, AppLanguageService>();
        builder.Services.AddSingleton<IFoodStreetDataService, FoodStreetMockDataService>();
        builder.Services.AddSingleton<IPoiNarrationService, PoiNarrationService>();
        builder.Services.AddSingleton<IPoiTourStoreService, PoiTourStoreService>();

        builder.Services.AddTransient<LanguageSelectionViewModel>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<HomeMapViewModel>();
        builder.Services.AddTransient<MyTourViewModel>();
        builder.Services.AddTransient<QrScannerViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
