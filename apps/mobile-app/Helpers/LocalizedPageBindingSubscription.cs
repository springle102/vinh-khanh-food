using Microsoft.Maui.ApplicationModel;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.Helpers;

public sealed class LocalizedPageBindingSubscription
{
    private readonly Page _page;
    private readonly IAppLanguageService _languageService;
    private bool _isSubscribed;

    public LocalizedPageBindingSubscription(Page page)
    {
        _page = page;
        _languageService = ServiceHelper.GetService<IAppLanguageService>();
        _page.Loaded += OnLoaded;
        _page.Unloaded += OnUnloaded;
    }

    public void Rebind()
    {
        if (_page.BindingContext is null)
        {
            return;
        }

        var currentBindingContext = _page.BindingContext;
        _page.BindingContext = null;
        _page.BindingContext = currentBindingContext;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (_isSubscribed)
        {
            return;
        }

        _languageService.LanguageChanged += OnLanguageChanged;
        _isSubscribed = true;
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (!_isSubscribed)
        {
            return;
        }

        _languageService.LanguageChanged -= OnLanguageChanged;
        _isSubscribed = false;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => MainThread.BeginInvokeOnMainThread(Rebind);
}
