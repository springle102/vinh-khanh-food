using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using ShapePath = Microsoft.Maui.Controls.Shapes.Path;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.Controls;

public partial class AppBottomBar : ContentView
{
    private static readonly Color ActiveColor = Color.FromArgb("#F48A22");
    private static readonly Color InactiveColor = Color.FromArgb("#7A7A7A");
    private static readonly Color ActiveBackgroundColor = Color.FromArgb("#FFF4E7");
    private static readonly Color ActiveStrokeColor = Color.FromArgb("#EAB06D");
    private readonly IAppLanguageService _languageService;
    private readonly IPoiNarrationService _poiNarrationService;

    public static readonly BindableProperty CurrentRouteProperty = BindableProperty.Create(
        nameof(CurrentRoute),
        typeof(string),
        typeof(AppBottomBar),
        AppRoutes.HomeMap,
        propertyChanged: (bindable, _, _) => ((AppBottomBar)bindable).ApplyState());

    public string CurrentRoute
    {
        get => (string)GetValue(CurrentRouteProperty);
        set => SetValue(CurrentRouteProperty, value);
    }

    public AppBottomBar()
    {
        InitializeComponent();
        _languageService = ServiceHelper.GetService<IAppLanguageService>();
        _poiNarrationService = ServiceHelper.GetService<IPoiNarrationService>();
        _languageService.LanguageChanged += (_, _) => ApplyLocalization();
        Loaded += (_, _) =>
        {
            ApplyLocalization();
            ApplyState();
        };
    }

    private async void OnQrTapped(object? sender, TappedEventArgs e) => await NavigateAsync(AppRoutes.QrScanner);
    private async void OnSettingsTapped(object? sender, TappedEventArgs e) => await NavigateAsync(AppRoutes.Settings);
    private async void OnPoiTapped(object? sender, TappedEventArgs e) => await NavigateAsync(AppRoutes.HomeMap);
    private async void OnTourTapped(object? sender, TappedEventArgs e) => await NavigateAsync(AppRoutes.MyTour);

    private async Task NavigateAsync(string route)
    {
        if (string.Equals(CurrentRoute, route, StringComparison.Ordinal))
        {
            return;
        }

        await _poiNarrationService.StopAsync();
        CurrentRoute = route;
        await Shell.Current.GoToAsync(AppRoutes.Root(route));
    }

    private void ApplyState()
    {
        ApplyItemState(CurrentRoute == AppRoutes.HomeMap, PoiTab, PoiLabel, PoiPath1, PoiPath2, PoiPath3);
        ApplyItemState(CurrentRoute == AppRoutes.MyTour, TourTab, TourLabel, TourPath1, TourPath2);
        ApplyItemState(CurrentRoute == AppRoutes.Settings, SettingsTab, SettingsLabel, SettingsPath1, SettingsPath2);
        ApplyQrState(CurrentRoute == AppRoutes.QrScanner);
    }

    private void ApplyLocalization()
    {
        QrLabel.Text = _languageService.GetText("bottom_qr");
        PoiLabel.Text = _languageService.GetText("bottom_poi");
        TourLabel.Text = _languageService.GetText("bottom_tour");
        SettingsLabel.Text = _languageService.GetText("bottom_settings");
    }

    private static void ApplyItemState(bool isActive, Border tab, Label label, params ShapePath[] paths)
    {
        var color = isActive ? ActiveColor : InactiveColor;
        tab.BackgroundColor = isActive ? ActiveBackgroundColor : Colors.Transparent;
        tab.Stroke = isActive ? new SolidColorBrush(ActiveStrokeColor) : new SolidColorBrush(Colors.Transparent);
        tab.StrokeThickness = isActive ? 1.2 : 0;
        label.TextColor = color;
        label.FontAttributes = isActive ? FontAttributes.Bold : FontAttributes.None;

        foreach (var path in paths)
        {
            path.Stroke = new SolidColorBrush(color);
        }
    }

    private void ApplyQrState(bool isActive)
    {
        QrTab.BackgroundColor = isActive ? Color.FromArgb("#D87410") : ActiveColor;
        QrTab.Scale = isActive ? 1.02 : 1.0;
        QrLabel.FontAttributes = FontAttributes.Bold;
    }
}
