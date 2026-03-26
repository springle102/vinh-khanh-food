using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace VinhKhanh.MobileApp.Helpers;

public static class ServiceHelper
{
    public static IServiceProvider Services { get; set; } = default!;

    public static T GetService<T>() where T : notnull => Services.GetRequiredService<T>();
}

public static class AppRoutes
{
    public const string Splash = "SplashPage";
    public const string LanguageSelection = "LanguageSelectionPage";
    public const string Home = "HomePage";
    public const string Map = "MapPage";
    public const string PoiList = "PoiListPage";
    public const string PoiDetail = "PoiDetailPage";
    public const string Settings = "SettingsPage";
    public const string QrScanner = "QrScannerPage";

    public static string Root(string route) => route.StartsWith("//", StringComparison.Ordinal) ? route : $"//{route}";
}

public abstract class BaseViewModel : INotifyPropertyChanged
{
    private bool _isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(backingStore, value))
        {
            return false;
        }

        backingStore = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected void RefreshAllBindings() => OnPropertyChanged(string.Empty);
}

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public async void Execute(object? parameter) => await execute();

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncCommand<T>(Func<T?, Task> execute, Func<T?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke((T?)parameter) ?? true;

    public async void Execute(object? parameter) => await execute((T?)parameter);

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public static class GeoFenceHelper
{
    public static double CalculateDistanceMeters(double startLatitude, double startLongitude, double endLatitude, double endLongitude)
    {
        const double earthRadiusMeters = 6371000;
        var dLat = DegreesToRadians(endLatitude - startLatitude);
        var dLon = DegreesToRadians(endLongitude - startLongitude);

        var lat1 = DegreesToRadians(startLatitude);
        var lat2 = DegreesToRadians(endLatitude);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return earthRadiusMeters * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}

public static class ObservableCollectionExtensions
{
    public static void ReplaceRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }
}
