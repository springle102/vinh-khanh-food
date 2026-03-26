namespace VinhKhanh.MobileApp;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(PoiDetailPage), typeof(PoiDetailPage));
    }
}
