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

public static class AppPreferenceKeys
{
    public const string CurrentCustomerId = "vkfood.current.customer.id";
}

public static class AppRoutes
{
    public const string Login = "LoginPage";
    public const string HomeMap = "HomeMapPage";
    public const string MyTour = "MyTourPage";
    public const string QrScanner = "QrScannerPage";
    public const string LanguageSelection = "LanguageSelectionPage";
    public const string Settings = "SettingsPage";
    public const string PremiumCheckout = "PremiumCheckoutPage";

    public static string Root(string route) => route.StartsWith("//", StringComparison.Ordinal) ? route : $"//{route}";

    public static string NormalizeShellRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return string.Empty;
        }

        var normalized = Uri.UnescapeDataString(route.Trim());
        var queryIndex = normalized.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
        {
            normalized = normalized[..queryIndex];
        }

        normalized = normalized.Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? string.Empty : segments[^1];
    }

    public static AppBottomBarTab ResolveBottomBarTab(string? route)
    {
        var normalizedRoute = NormalizeShellRoute(route);
        return normalizedRoute switch
        {
            HomeMap or "HomePage" or "MapPage" or "PoiListPage" or "PoiDetailPage" => AppBottomBarTab.Poi,
            QrScanner => AppBottomBarTab.QrScanner,
            Settings => AppBottomBarTab.Settings,
            _ => AppBottomBarTab.None
        };
    }
}

public enum AppBottomBarTab
{
    None,
    Poi,
    QrScanner,
    Settings
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

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

public abstract class BaseViewModel : ObservableObject
{
    private bool _isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }
}

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public async void Execute(object? parameter) => await execute();

    public Task ExecuteAsync() => execute();

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncCommand<T>(Func<T?, Task> execute, Func<T?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke((T?)parameter) ?? true;

    public async void Execute(object? parameter) => await execute((T?)parameter);

    public Task ExecuteAsync(T? parameter) => execute(parameter);

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
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
