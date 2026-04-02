using Microsoft.Extensions.Logging;
using VinhKhanh.MobileApp.Services;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<IAppLanguageService, AppLanguageService>();
        builder.Services.AddSingleton<IFoodStreetDataService, FoodStreetMockDataService>();
        builder.Services.AddSingleton<IPoiNarrationService, PoiNarrationService>();
        builder.Services.AddSingleton<IPoiTourStoreService, PoiTourStoreService>();

        builder.Services.AddTransient<QRSuccessLanguageViewModel>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<HomeMapViewModel>();
        builder.Services.AddTransient<MyTourViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
