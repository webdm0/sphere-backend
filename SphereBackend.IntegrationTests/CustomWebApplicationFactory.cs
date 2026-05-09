using SphereBackend.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace SphereBackend.IntegrationTests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"spherebackend_integration_tests_{Guid.NewGuid():N}";
    private readonly string _testConnectionString;
    private readonly string _adminConnectionString;
    private bool _disposed;

    public CustomWebApplicationFactory()
    {
        var baseConnectionString = ResolveBaseConnectionString();
        var testConnectionBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = _databaseName,
            Pooling = false
        };
        _testConnectionString = testConnectionBuilder.ConnectionString;

        _adminConnectionString = ResolveAdminConnectionString(baseConnectionString);

        SetEnvironmentVariable("Jwt__Key", "integration-tests-jwt-key-1234567890");
        SetEnvironmentVariable("Jwt__Issuer", "SphereBackend.IntegrationTests");
        SetEnvironmentVariable("Jwt__Audience", "SphereBackend.IntegrationTests");
        SetEnvironmentVariable("SessionHint__Key", "integration-tests-session-key-123456");
        SetEnvironmentVariable("SessionHint__Issuer", "SphereBackend.IntegrationTests");
        SetEnvironmentVariable("SessionHint__Audience", "SphereBackend.IntegrationTests");
        SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _testConnectionString);
        SetEnvironmentVariable("Resend__ApiKey", "integration-tests-resend-api-key");
        SetEnvironmentVariable("Resend__FromEmail", "noreply@example.com");
        SetEnvironmentVariable("Frontend__BaseUrl", "https://frontend.example.com");
        SetEnvironmentVariable("Hashids__Salt", "integration-tests-salt");
        SetEnvironmentVariable("Hashids__MinHashLength", "8");
        SetEnvironmentVariable("AllowedOrigins__0", "https://localhost");
        SetEnvironmentVariable("Sse__EnableDistributedRelay", "false");
        SetEnvironmentVariable("Sse__AllowQueryStringToken", "false");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "integration-tests-jwt-key-1234567890",
                ["Jwt:Issuer"] = "SphereBackend.IntegrationTests",
                ["Jwt:Audience"] = "SphereBackend.IntegrationTests",
                ["SessionHint:Key"] = "integration-tests-session-key-123456",
                ["SessionHint:Issuer"] = "SphereBackend.IntegrationTests",
                ["SessionHint:Audience"] = "SphereBackend.IntegrationTests",
                ["ConnectionStrings:DefaultConnection"] = _testConnectionString,
                ["Resend:ApiKey"] = "integration-tests-resend-api-key",
                ["Resend:FromEmail"] = "noreply@example.com",
                ["Frontend:BaseUrl"] = "https://frontend.example.com",
                ["Hashids:Salt"] = "integration-tests-salt",
                ["Hashids:MinHashLength"] = "8",
                ["AllowedOrigins:0"] = "https://localhost",
                ["Sse:EnableDistributedRelay"] = "false",
                ["Sse:AllowQueryStringToken"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseNpgsql(_testConnectionString);
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultScheme = TestAuthenticationHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName,
                _ => { });
        });
    }

    public HttpClient CreateAuthenticatedClient(int userId, bool isDemo = false)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserIdHeaderName, userId.ToString());
        if (isDemo)
        {
            client.DefaultRequestHeaders.Add(TestAuthenticationHandler.IsDemoHeaderName, bool.TrueString);
        }
        return client;
    }

    public async Task ResetDatabaseAsync(Action<AppDbContext> seed)
    {
        await RecreateDatabaseAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync();

        seed(db);
        await db.SaveChangesAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing || _disposed)
        {
            return;
        }

        _disposed = true;
        DeleteDatabaseAsync().GetAwaiter().GetResult();
    }

    private async Task RecreateDatabaseAsync()
    {
        await using var adminConnection = new NpgsqlConnection(_adminConnectionString);
        await adminConnection.OpenAsync();

        await TerminateDatabaseConnectionsAsync(adminConnection);
        await ExecuteNonQueryAsync(adminConnection, $"DROP DATABASE IF EXISTS \"{_databaseName}\";");
        await ExecuteNonQueryAsync(adminConnection, $"CREATE DATABASE \"{_databaseName}\";");
    }

    private async Task DeleteDatabaseAsync()
    {
        await using var adminConnection = new NpgsqlConnection(_adminConnectionString);
        await adminConnection.OpenAsync();

        await TerminateDatabaseConnectionsAsync(adminConnection);
        await ExecuteNonQueryAsync(adminConnection, $"DROP DATABASE IF EXISTS \"{_databaseName}\";");
    }

    private async Task TerminateDatabaseConnectionsAsync(NpgsqlConnection adminConnection)
    {
        const string sql = """
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = @databaseName
              AND pid <> pg_backend_pid();
            """;

        await using var command = new NpgsqlCommand(sql, adminConnection);
        command.Parameters.AddWithValue("databaseName", _databaseName);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteNonQueryAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string ResolveBaseConnectionString()
    {
        var integrationConnectionString = Environment.GetEnvironmentVariable("IntegrationTests__DefaultConnection");
        if (!string.IsNullOrWhiteSpace(integrationConnectionString))
        {
            return integrationConnectionString;
        }

        var defaultConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrWhiteSpace(defaultConnectionString))
        {
            return defaultConnectionString;
        }

        var dotEnvConnectionString = TryReadDotEnvValue("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrWhiteSpace(dotEnvConnectionString))
        {
            return dotEnvConnectionString;
        }

        throw new InvalidOperationException(
            "Integration tests require IntegrationTests__DefaultConnection, ConnectionStrings__DefaultConnection, or SphereBackend/.env with ConnectionStrings__DefaultConnection.");
    }

    private static string ResolveAdminConnectionString(string baseConnectionString)
    {
        var integrationAdminConnectionString = Environment.GetEnvironmentVariable("IntegrationTests__AdminConnection");
        if (!string.IsNullOrWhiteSpace(integrationAdminConnectionString))
        {
            return new NpgsqlConnectionStringBuilder(integrationAdminConnectionString)
            {
                Pooling = false
            }.ConnectionString;
        }

        return new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "postgres",
            Pooling = false
        }.ConnectionString;
    }

    private static string? TryReadDotEnvValue(string key)
    {
        var envPath = FindProjectEnvFile();
        if (envPath == null || !File.Exists(envPath))
        {
            return null;
        }

        foreach (var rawLine in File.ReadAllLines(envPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            {
                line = line["export ".Length..].Trim();
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var candidateKey = line[..separatorIndex].Trim();
            if (!string.Equals(candidateKey, key, StringComparison.Ordinal))
            {
                continue;
            }

            var value = line[(separatorIndex + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            return value
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\r", "\r", StringComparison.Ordinal);
        }

        return null;
    }

    private static string? FindProjectEnvFile()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "SphereBackend", ".env");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void SetEnvironmentVariable(string key, string value)
    {
        Environment.SetEnvironmentVariable(key, value);
    }
}
