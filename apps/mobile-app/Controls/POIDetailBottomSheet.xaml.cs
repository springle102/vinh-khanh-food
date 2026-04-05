using System.Windows.Input;

namespace VinhKhanh.MobileApp.Controls;

public partial class POIDetailBottomSheet : ContentView
{
    private const string SheetHeightAnimationName = "POIDetailBottomSheet.Height";
    private bool _isPresented;
    private bool _isExpanded;
    private bool _isPanning;
    private double _collapsedHeight = 420;
    private double _expandedHeight = 720;
    private double _minimumDragHeight = 240;
    private double _panStartHeight = 420;

    public static readonly BindableProperty CloseCommandProperty = BindableProperty.Create(
        nameof(CloseCommand),
        typeof(ICommand),
        typeof(POIDetailBottomSheet));

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
        EnsureSheetBounds();

        if (_isPresented == isPresented && IsVisible == isPresented)
        {
            return;
        }

        _isPresented = isPresented;
        AbortSheetAnimations();

        if (isPresented)
        {
            _isExpanded = false;
            IsVisible = true;
            SheetBorder.HeightRequest = _collapsedHeight;
            SheetBorder.Opacity = 0;
            SheetBorder.TranslationY = 56;
            Backdrop.Opacity = 0;

            await ScrollToTopAsync();
            await Task.WhenAll(
                Backdrop.FadeToAsync(1, 180, Easing.CubicOut),
                SheetBorder.TranslateToAsync(0, 0, 240, Easing.CubicOut),
                SheetBorder.FadeToAsync(1, 180, Easing.CubicOut));
            return;
        }

        if (!IsVisible)
        {
            return;
        }

        await Task.WhenAll(
            Backdrop.FadeToAsync(0, 130, Easing.CubicIn),
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

        await ScrollToTopAsync();
        await SheetContent.ScaleToAsync(0.985, 80, Easing.CubicOut);
        await SheetContent.ScaleToAsync(1, 140, Easing.CubicIn);
    }

    private void OnRootSizeChanged(object? sender, EventArgs e)
    {
        EnsureSheetBounds();

        if (_isPresented && !_isPanning)
        {
            SheetBorder.HeightRequest = _isExpanded ? _expandedHeight : _collapsedHeight;
        }
    }

    private async void OnHandlePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (!IsVisible)
        {
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _isPanning = true;
                _panStartHeight = ResolveCurrentHeight();
                AbortSheetAnimations();
                break;

            case GestureStatus.Running:
                var nextHeight = Math.Clamp(_panStartHeight - e.TotalY, _minimumDragHeight, _expandedHeight);
                SheetBorder.HeightRequest = nextHeight;
                break;

            case GestureStatus.Completed:
                _isPanning = false;
                await SnapAfterPanAsync(e.TotalY);
                break;

            case GestureStatus.Canceled:
                _isPanning = false;
                await SnapToNearestStateAsync();
                break;
        }
    }

    private async void OnBackdropTapped(object? sender, TappedEventArgs e)
        => await ExecuteCloseAsync();

    private async void OnCloseTapped(object? sender, TappedEventArgs e)
        => await ExecuteCloseAsync();

    private async Task SnapAfterPanAsync(double totalY)
    {
        var currentHeight = ResolveCurrentHeight();
        var closeThreshold = _collapsedHeight - 72;
        if (totalY > 110 && currentHeight <= closeThreshold)
        {
            await ExecuteCloseAsync();
            return;
        }

        await SnapToNearestStateAsync();
    }

    private async Task SnapToNearestStateAsync()
    {
        var currentHeight = ResolveCurrentHeight();
        var midpoint = (_collapsedHeight + _expandedHeight) / 2;
        var shouldExpand = currentHeight >= midpoint;
        _isExpanded = shouldExpand;
        await AnimateSheetHeightAsync(shouldExpand ? _expandedHeight : _collapsedHeight, 180, Easing.CubicOut);
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

    private void EnsureSheetBounds()
    {
        if (Height <= 0)
        {
            return;
        }

        _expandedHeight = Math.Max(440, Height * 0.88);
        _collapsedHeight = Math.Max(360, Math.Min(_expandedHeight - 84, Height * 0.56));
        _minimumDragHeight = Math.Max(220, _collapsedHeight - 180);

        HeroContainer.HeightRequest = Math.Max(170, Math.Min(220, Height * 0.24));
        SheetBorder.MaximumHeightRequest = _expandedHeight;
    }

    private double ResolveCurrentHeight()
    {
        if (SheetBorder.Height > 0)
        {
            return SheetBorder.Height;
        }

        if (SheetBorder.HeightRequest > 0)
        {
            return SheetBorder.HeightRequest;
        }

        return _collapsedHeight;
    }

    private async Task AnimateSheetHeightAsync(double targetHeight, uint duration, Easing easing)
    {
        var startHeight = ResolveCurrentHeight();
        if (Math.Abs(startHeight - targetHeight) < 0.5)
        {
            SheetBorder.HeightRequest = targetHeight;
            return;
        }

        var completion = new TaskCompletionSource<bool>();
        SheetBorder.AbortAnimation(SheetHeightAnimationName);

        var animation = new Animation(
            callback: value => SheetBorder.HeightRequest = value,
            start: startHeight,
            end: targetHeight,
            easing: easing);

        animation.Commit(
            owner: SheetBorder,
            name: SheetHeightAnimationName,
            rate: 16,
            length: duration,
            easing: easing,
            finished: (_, _) =>
            {
                SheetBorder.HeightRequest = targetHeight;
                completion.TrySetResult(true);
            });

        await completion.Task;
    }

    private void AbortSheetAnimations()
    {
        SheetBorder.AbortAnimation(SheetHeightAnimationName);
        SheetBorder.AbortAnimation(nameof(SetPresentedAsync));
        Backdrop.AbortAnimation(nameof(SetPresentedAsync));
    }

    private async Task ScrollToTopAsync()
    {
        try
        {
            await SheetScrollView.ScrollToAsync(0, 0, false);
        }
        catch
        {
            // Best effort only.
        }
    }
}
