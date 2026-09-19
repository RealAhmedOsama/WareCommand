namespace Wms.Infrastructure.Identity;

public interface IDesktopAuthenticationService
{
    Task<DesktopAuthenticationResult> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed record DesktopAuthenticationResult(bool Succeeded, string Message)
{
    public static DesktopAuthenticationResult Success { get; } = new(true, string.Empty);

    public static DesktopAuthenticationResult Failure(string message) => new(false, message);
}
