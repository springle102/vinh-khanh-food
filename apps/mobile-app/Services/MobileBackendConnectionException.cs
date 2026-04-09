namespace VinhKhanh.MobileApp.Services;

public sealed class MobileBackendConnectionException : Exception
{
    public MobileBackendConnectionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
