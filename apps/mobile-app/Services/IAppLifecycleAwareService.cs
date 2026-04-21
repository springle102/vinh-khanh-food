namespace VinhKhanh.MobileApp.Services;

public interface IAppLifecycleAwareService
{
    Task HandleAppResumedAsync(CancellationToken cancellationToken = default);
}
