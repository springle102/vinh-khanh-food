using ShapePath = Microsoft.Maui.Controls.Shapes.Path;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using VinhKhanh.MobileApp.Helpers;

namespace VinhKhanh.MobileApp.Controls;

public partial class AppBottomBar : ContentView
{
    private static readonly Color ActiveColor = Color.FromArgb("#FF5A0A");
    private static readonly Color InactiveColor = Color.FromArgb("#98A3B7");

    public static readonly BindableProperty CurrentRouteProperty = BindableProperty.Create(
        nameof(CurrentRoute),
        typeof(string),
        typeof(AppBottomBar),
        AppRoutes.Home,
        propertyChanged: (bindable, _, _) => ((AppBottomBar)bindable).ApplyState());

    public string CurrentRoute
    {
        get => (string)GetValue(CurrentRouteProperty);
        set => SetValue(CurrentRouteProperty, value);
    }

    public AppBottomBar()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyState();
    }

    private async void OnHomeTapped(object? sender, TappedEventArgs e) => await NavigateAsync(AppRoutes.Home);
    private async void OnMapTapped(object? sender, TappedEventArgs e) => await NavigateAsync(AppRoutes.Map);
    private async void OnListTapped(object? sender, TappedEventArgs e) => await NavigateAsync(AppRoutes.PoiList);
    private async void OnSettingsTapped(object? sender, TappedEventArgs e) => await NavigateAsync(AppRoutes.Settings);
    private async void OnQrTapped(object? sender, TappedEventArgs e) => await NavigateAsync(AppRoutes.QrScanner);

    private async Task NavigateAsync(string route)
    {
        if (string.Equals(CurrentRoute, route, StringComparison.Ordinal))
        {
            return;
        }

        CurrentRoute = route;
        await Shell.Current.GoToAsync(AppRoutes.Root(route));
    }

    private void ApplyState()
    {
        ApplyItemState(CurrentRoute == AppRoutes.Home, HomeLabel, HomePath);
        ApplyItemState(CurrentRoute == AppRoutes.Map, MapLabel, MapPath1, MapPath2);
        ApplyItemState(CurrentRoute == AppRoutes.PoiList, ListLabel, ListPath1, ListPath2);
        ApplyItemState(CurrentRoute == AppRoutes.Settings, SettingsLabel, SettingsPath1, SettingsPath2);
        QrButton.Scale = CurrentRoute == AppRoutes.QrScanner ? 1.03 : 1.0;
    }

    private static void ApplyItemState(bool isActive, Label label, params ShapePath[] paths)
    {
        var color = isActive ? ActiveColor : InactiveColor;
        label.TextColor = color;
        label.FontAttributes = isActive ? FontAttributes.Bold : FontAttributes.None;

        foreach (var path in paths)
        {
            path.Stroke = new SolidColorBrush(color);
        }
    }
}


