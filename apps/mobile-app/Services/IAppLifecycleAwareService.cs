namespace VinhKhanh.MobileApp.Services;

public interface IAppLifecycleAwareService
{
    Task HandleAppResumedAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    Task HandleAppStoppedAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
