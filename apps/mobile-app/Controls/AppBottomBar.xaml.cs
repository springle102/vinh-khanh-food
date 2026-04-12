using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using ShapePath = Microsoft.Maui.Controls.Shapes.Path;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Services;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp.Controls;

public partial class AppBottomBar : ContentView
{
    private static readonly Color ActiveColor = Color.FromArgb("#F48A22");
    private static readonly Color InactiveColor = Color.FromArgb("#7A7A7A");
    private static readonly Color ActiveBackgroundColor = Color.FromArgb("#FFF4E7");
    private static readonly Color ActiveStrokeColor = Color.FromArgb("#EAB06D");
    private readonly IAppLanguageService _languageService;
    private readonly AppBottomBarViewModel _viewModel;
    private bool _isSubscribed;

    public AppBottomBar()
    {
        InitializeComponent();
        _languageService = ServiceHelper.GetService<IAppLanguageService>();
        _viewModel = ServiceHelper.GetService<AppBottomBarViewModel>();
        BindingContext = _viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (_isSubscribed)
        {
            return;
        }

        _isSubscribed = true;
        _languageService.LanguageChanged += OnLanguageChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.SyncWithShell();
        ApplyLocalization();
        ApplyState();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (!_isSubscribed)
        {
            return;
        }

        _isSubscribed = false;
        _languageService.LanguageChanged -= OnLanguageChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => ApplyLocalization();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppBottomBarViewModel.SelectedTab) or nameof(AppBottomBarViewModel.CurrentRoute) or "")
        {
            MainThread.BeginInvokeOnMainThread(ApplyState);
        }
    }

    private void ApplyState()
    {
        ApplyItemState(_viewModel.SelectedTab == AppBottomBarTab.Poi, PoiTab, PoiLabel, PoiPath1, PoiPath2, PoiPath3);
        ApplyItemState(_viewModel.SelectedTab == AppBottomBarTab.Settings, SettingsTab, SettingsLabel, SettingsPath1, SettingsPath2);
        ApplyQrState(_viewModel.SelectedTab == AppBottomBarTab.QrScanner);
    }

    private void ApplyLocalization()
    {
        QrLabel.Text = _languageService.GetText("bottom_qr");
        PoiLabel.Text = _languageService.GetText("bottom_poi");
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
        QrTab.Stroke = isActive ? new SolidColorBrush(Color.FromArgb("#FFD9B3")) : new SolidColorBrush(Colors.Transparent);
        QrTab.StrokeThickness = isActive ? 1.2 : 0;
        QrTab.Scale = 1.0;
        QrLabel.FontAttributes = FontAttributes.Bold;
    }
}
