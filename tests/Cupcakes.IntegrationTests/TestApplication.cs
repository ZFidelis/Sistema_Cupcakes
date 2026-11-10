using System.Collections.Concurrent;
using System.Text.Json;
using Cupcakes.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Cupcakes.IntegrationTests;

public sealed class TestApplication : WebApplicationFactory<Program>
{
    public ConcurrentQueue<Exception> LoggedExceptions { get; } = new();
    private readonly string root;
    private readonly string adminConnection;
    private readonly string databaseName = $"cupcakes_test_{Guid.NewGuid():N}";
    private readonly string testConnection;
    private bool databaseCreated;

    private TestApplication()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SistemaCupcakes.sln")))
            directory = directory.Parent;
        root = directory?.FullName ?? throw new InvalidOperationException("Raiz da solução não encontrada.");

        adminConnection = Environment.GetEnvironmentVariable("CUPCAKES_TEST_POSTGRES_CONNECTION")
            ?? JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".local", "integration-tests.json")))
                .RootElement.GetProperty("ConnectionStrings").GetProperty("TestPostgres").GetString()!;
        var settings = new NpgsqlConnectionStringBuilder(adminConnection);
        if (settings.Database != "cupcakes_tests" || settings.Username != "cupcakes_tests"
            || settings.Host is not ("127.0.0.1" or "localhost"))
            throw new InvalidOperationException("Os testes exigem o PostgreSQL local isolado cupcakes_tests.");
        settings.Database = databaseName;
        settings.Pooling = false;
        testConnection = settings.ConnectionString;
    }

    public static async Task<TestApplication> CreateAsync()
    {
        var app = new TestApplication();
        try
        {
            await using var connection = new NpgsqlConnection(app.adminConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{app.databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
            app.databaseCreated = true;
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<CupcakesDbContext>().Database.MigrateAsync();
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.AddProvider(new ExceptionCaptureProvider(LoggedExceptions)));
        builder.UseContentRoot(Path.Combine(root, "API", "Cupcakes.Api"));
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = testConnection }));
    }

    public HttpClient CreateBrowserClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (!databaseCreated) return;

        // O nome é privado, gerado nesta instância, e só é removido se esta instância o criou.
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
        databaseCreated = false;
    }
}

internal sealed class ExceptionCaptureProvider(ConcurrentQueue<Exception> exceptions) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ExceptionCaptureLogger(exceptions);
    public void Dispose() { }

    private sealed class ExceptionCaptureLogger(ConcurrentQueue<Exception> exceptions) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception is not null) exceptions.Enqueue(exception);
        }
    }
}
