using System.Windows.Input;

namespace VinhKhanh.MobileApp.Controls;

public partial class POIDetailBottomSheet : ContentView
{
    public static readonly BindableProperty CloseCommandProperty = BindableProperty.Create(
        nameof(CloseCommand),
        typeof(ICommand),
        typeof(POIDetailBottomSheet));

    private bool _isPresented;

    public POIDetailBottomSheet()
    {
        InitializeComponent();
    }

    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public async Task SetPresentedAsync(bool isPresented)
    {
        if (_isPresented == isPresented && IsVisible == isPresented)
        {
            return;
        }

        _isPresented = isPresented;
        SheetBorder.AbortAnimation(nameof(SetPresentedAsync));

        if (isPresented)
        {
            IsVisible = true;
            SheetBorder.Opacity = 0;
            SheetBorder.TranslationY = 48;
            await Task.WhenAll(
                SheetBorder.TranslateToAsync(0, 0, 240, Easing.CubicOut),
                SheetBorder.FadeToAsync(1, 180, Easing.CubicOut));
            return;
        }

        if (!IsVisible)
        {
            return;
        }

        await Task.WhenAll(
            SheetBorder.TranslateToAsync(0, 56, 180, Easing.CubicIn),
            SheetBorder.FadeToAsync(0, 130, Easing.CubicIn));
        IsVisible = false;
    }

    public async Task AnimateContentRefreshAsync()
    {
        if (!IsVisible)
        {
            return;
        }

        await SheetContent.ScaleToAsync(0.985, 80, Easing.CubicOut);
        await SheetContent.ScaleToAsync(1, 140, Easing.CubicIn);
    }

    private void OnRootSizeChanged(object? sender, EventArgs e)
    {
        if (Height <= 0)
        {
            return;
        }

        SheetBorder.MaximumHeightRequest = Math.Max(320, Height * 0.62);
    }

    private async void OnSheetPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (!IsVisible)
        {
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Running when e.TotalY > 0:
                SheetBorder.TranslationY = e.TotalY;
                break;

            case GestureStatus.Completed:
                if (e.TotalY >= 120)
                {
                    await ExecuteCloseAsync();
                }
                else
                {
                    await SheetBorder.TranslateToAsync(0, 0, 150, Easing.CubicOut);
                }

                break;

            case GestureStatus.Canceled:
                await SheetBorder.TranslateToAsync(0, 0, 120, Easing.CubicOut);
                break;
        }
    }

    private async Task ExecuteCloseAsync()
    {
        if (CloseCommand?.CanExecute(null) == true)
        {
            CloseCommand.Execute(null);
            return;
        }

        await SetPresentedAsync(false);
    }
}
