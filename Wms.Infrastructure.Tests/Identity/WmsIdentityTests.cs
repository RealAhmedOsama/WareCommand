using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;

namespace Wms.Infrastructure.Tests.Identity;

public sealed class WmsIdentityTests : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().SetApplicationName("WmsIdentityTests");
        services.AddDbContext<WmsDbContext>(options => options.UseSqlite(_connection));
        services
            .AddIdentityCore<WmsUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 3;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<WmsDbContext>()
            .AddDefaultTokenProviders();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        await _scope.ServiceProvider.GetRequiredService<WmsDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task PasswordAuthenticationUpdatesTheExplicitDesktopSession()
    {
        var userManager = GetUserManager();
        var user = await CreateUserAsync("operator", "operator@example.test");
        var session = new DesktopUserSession();
        var audit = new RecordingAuthenticationAuditService();
        var authentication = new DesktopAuthenticationService(
            userManager,
            session,
            audit,
            new SystemClock(),
            NullLogger<DesktopAuthenticationService>.Instance);

        var result = await authentication.AuthenticateAsync("operator", ValidPassword);

        Assert.True(result.Succeeded);
        Assert.Equal(user.Id, session.UserId);
        Assert.Equal("Warehouse Operator", session.DisplayName);
        Assert.Contains(audit.Events, auditEvent =>
            auditEvent.EventType == WmsAuthenticationEventTypes.LoginSucceeded &&
            auditEvent.UserId == user.Id);
        Assert.NotNull((await userManager.FindByIdAsync(user.Id))!.LastLoginAtUtc);
    }

    [Fact]
    public async Task DisabledUserCannotAuthenticate()
    {
        var userManager = GetUserManager();
        var user = await CreateUserAsync("disabled", "disabled@example.test");
        user.IsActive = false;
        Assert.True((await userManager.UpdateAsync(user)).Succeeded);

        var session = new DesktopUserSession();
        var authentication = new DesktopAuthenticationService(
            userManager,
            session,
            new RecordingAuthenticationAuditService(),
            new SystemClock(),
            NullLogger<DesktopAuthenticationService>.Instance);

        var result = await authentication.AuthenticateAsync("disabled", ValidPassword);

        Assert.False(result.Succeeded);
        Assert.False(session.IsAuthenticated);
    }

    [Fact]
    public async Task RepeatedFailuresLockTheAccountAndCorrectPasswordRemainsRejected()
    {
        var userManager = GetUserManager();
        await CreateUserAsync("lockout", "lockout@example.test");
        var authentication = new DesktopAuthenticationService(
            userManager,
            new DesktopUserSession(),
            new RecordingAuthenticationAuditService(),
            new SystemClock(),
            NullLogger<DesktopAuthenticationService>.Instance);

        await authentication.AuthenticateAsync("lockout", "WrongPassword123!");
        await authentication.AuthenticateAsync("lockout", "WrongPassword123!");
        var thirdAttempt = await authentication.AuthenticateAsync("lockout", "WrongPassword123!");
        var correctAttempt = await authentication.AuthenticateAsync("lockout", ValidPassword);

        var lockedUser = await userManager.FindByNameAsync("lockout");
        Assert.False(thirdAttempt.Succeeded);
        Assert.Contains("locked", thirdAttempt.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await userManager.IsLockedOutAsync(lockedUser!));
        Assert.False(correctAttempt.Succeeded);
    }

    [Fact]
    public async Task PasswordResetTokenChangesTheCredentialWithoutExposingItToAudit()
    {
        var userManager = GetUserManager();
        var user = await CreateUserAsync("resettable", "resettable@example.test");
        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        var result = await userManager.ResetPasswordAsync(user, token, "ResetPassword123!");

        Assert.True(result.Succeeded);
        Assert.True(await userManager.CheckPasswordAsync(user, "ResetPassword123!"));
        Assert.False(await userManager.CheckPasswordAsync(user, ValidPassword));
    }

    private UserManager<WmsUser> GetUserManager() =>
        _scope.ServiceProvider.GetRequiredService<UserManager<WmsUser>>();

    private async Task<WmsUser> CreateUserAsync(string userName, string email)
    {
        var user = new WmsUser
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Warehouse Operator",
            EmployeeCode = $"EMP-{userName.ToUpperInvariant()}",
            Locale = "en-US",
            TimeZone = "UTC",
            IsActive = true,
            LockoutEnabled = true
        };

        var result = await GetUserManager().CreateAsync(user, ValidPassword);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        return user;
    }

    private const string ValidPassword = "ValidPassword123!";

    private sealed class RecordingAuthenticationAuditService : IAuthenticationAuditService
    {
        public List<AuditEvent> Events { get; } = [];

        public Task RecordAsync(
            string eventType,
            bool succeeded,
            string? userId = null,
            string? userName = null,
            string? remoteIpAddress = null,
            string? userAgent = null,
            string? details = null,
            CancellationToken cancellationToken = default)
        {
            Events.Add(new AuditEvent(eventType, succeeded, userId, userName, details));
            return Task.CompletedTask;
        }
    }

    private sealed record AuditEvent(
        string EventType,
        bool Succeeded,
        string? UserId,
        string? UserName,
        string? Details);
}
