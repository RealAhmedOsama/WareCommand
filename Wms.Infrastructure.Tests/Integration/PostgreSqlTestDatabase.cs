using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Tests.Integration;

public sealed class PostgreSqlTestDatabase : IAsyncLifetime, IAsyncDisposable
{
    private readonly string? _baseConnectionString =
        Environment.GetEnvironmentVariable("WARECOMMAND_TEST_POSTGRES_CONNECTION");
    private string? _schema;
    private string? _connectionString;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_connectionString);

    public string TargetIdentifier => _schema ?? string.Empty;

    public string TestConnectionString => _connectionString
        ?? throw new InvalidOperationException("The isolated PostgreSQL target has not been initialized.");

    internal string? ScopedConnectionString => _connectionString;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_baseConnectionString))
        {
            return;
        }

        var baseBuilder = new NpgsqlConnectionStringBuilder(_baseConnectionString)
        {
            ApplicationName = "WareCommand.Infrastructure.Tests",
            Pooling = false
        };
        _schema = $"wms_test_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(baseBuilder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"CREATE SCHEMA {QuoteIdentifier(_schema)}",
                connection);
            await command.ExecuteNonQueryAsync();
        }

        baseBuilder.SearchPath = _schema;
        _connectionString = baseBuilder.ConnectionString;

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public WmsDbContext CreateContext()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                "Set WARECOMMAND_TEST_POSTGRES_CONNECTION before creating a PostgreSQL test context.");
        }

        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseNpgsql(
                _connectionString!,
                npgsql => npgsql.MigrationsAssembly(typeof(WmsDbContext).Assembly.FullName))
            .Options;
        return new WmsDbContext(options);
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_baseConnectionString) || string.IsNullOrWhiteSpace(_schema))
        {
            return;
        }

        var baseBuilder = new NpgsqlConnectionStringBuilder(_baseConnectionString)
        {
            ApplicationName = "WareCommand.Infrastructure.Tests.Cleanup",
            Pooling = false
        };
        await using var connection = new NpgsqlConnection(baseBuilder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP SCHEMA IF EXISTS {QuoteIdentifier(_schema)} CASCADE",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

[CollectionDefinition(Name, DisableParallelization = false)]
public sealed class PostgreSqlTestFixture : ICollectionFixture<PostgreSqlTestDatabase>
{
    public const string Name = "PostgreSQL isolated schema";
}
