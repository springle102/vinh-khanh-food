using System.Threading;
using Microsoft.Maui.ApplicationModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public abstract class LocalizedViewModelBase : BaseViewModel
{
    private readonly SemaphoreSlim _languageChangeLock = new(1, 1);
    private int _languageChangeVersion;

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
        var version = Interlocked.Increment(ref _languageChangeVersion);
        MainThread.BeginInvokeOnMainThread(() => _ = ApplyLanguageChangeAsync(version));
    }

    private async Task ApplyLanguageChangeAsync(int version)
    {
        await _languageChangeLock.WaitAsync();
        try
        {
            if (version != _languageChangeVersion)
            {
                return;
            }

            await ReloadLocalizedStateAsync();
        }
        finally
        {
            RefreshAllBindings();
            _languageChangeLock.Release();
        }
    }
}
