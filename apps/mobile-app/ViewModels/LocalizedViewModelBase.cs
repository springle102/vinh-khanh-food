using Microsoft.Maui.ApplicationModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public abstract class LocalizedViewModelBase : BaseViewModel
{
    private int _isLocalizedReloadRunning;
    private int _hasPendingLocalizedReload;

    protected LocalizedViewModelBase(IAppLanguageService languageService)
    {
        LanguageService = languageService;
        LanguageService.LanguageChanged += OnLanguageChangedInternal;
    }

    protected IAppLanguageService LanguageService { get; }

    protected virtual Task ReloadLocalizedStateAsync()
        => Task.CompletedTask;

    protected void RefreshLocalizedBindings()
        => RefreshAllBindings();

    private void OnLanguageChangedInternal(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(RefreshAllBindings);
        QueueLocalizedReload();
    }

    private void QueueLocalizedReload()
    {
        Interlocked.Exchange(ref _hasPendingLocalizedReload, 1);
        if (Interlocked.CompareExchange(ref _isLocalizedReloadRunning, 1, 0) == 0)
        {
            _ = RunLocalizedReloadLoopAsync();
        }
    }

    private async Task RunLocalizedReloadLoopAsync()
    {
        try
        {
            while (Interlocked.Exchange(ref _hasPendingLocalizedReload, 0) == 1)
            {
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(ReloadLocalizedStateAsync);
                    await MainThread.InvokeOnMainThreadAsync(RefreshAllBindings);
                }
                catch
                {
                    // Best effort background reload. Immediate text bindings were already refreshed.
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isLocalizedReloadRunning, 0);
            if (Volatile.Read(ref _hasPendingLocalizedReload) == 1)
            {
                QueueLocalizedReload();
            }
        }
    }
}
