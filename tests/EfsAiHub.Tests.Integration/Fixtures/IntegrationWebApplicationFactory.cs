using System.ComponentModel;
using DotNet.Testcontainers.Builders;
using EfsAiHub.Platform.Runtime.Interfaces;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Platform.Runtime.Functions;
using EfsAiHub.Infra.Persistence.Postgres;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace EfsAiHub.Tests.Integration.Fixtures;

/// <summary>
/// WebApplicationFactory shared across all integration tests.
/// Starts a Postgres Testcontainer and a WireMock server for external API mocking.
/// Redis uses InMemory override to avoid an additional container.
/// </summary>
public sealed class IntegrationWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("efs_ai_hub_test")
        .WithUsername("test")
        .WithPassword("test")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
        .Build();

    private readonly ExternalApiMock _apiMock = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();

        var schemaSql = await File.ReadAllTextAsync(FindSiblingFile("schemas.sql"));
        await using (var cmd = new NpgsqlCommand(schemaSql, conn))
            await cmd.ExecuteNonQueryAsync();

        var viewsSql = await File.ReadAllTextAsync(FindSiblingFile("views.sql"));
        await using (var cmd = new NpgsqlCommand(viewsSql, conn))
            await cmd.ExecuteNonQueryAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        _apiMock.Dispose();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddJsonFile("appsettings.Test.json", optional: true);
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = AiHubConnectionString(),
                ["OpenAI:DefaultModel"] = "gpt-4o",
            });
            config.AddEnvironmentVariables();
        });

        builder.ConfigureServices(services =>
        {
            // .NET 10 ficou estrito sobre merge de arrays na configuração: o
            // appsettings.json da API (com Admin:AccountIds:["011982329"]) não
            // é mais sobrescrito por uma chave vazia em appsettings.Test.json.
            // Forçamos lista vazia via post-configure pra desabilitar o gate em testes.
            services.PostConfigure<EfsAiHub.Host.Api.Configuration.AdminOptions>(o => o.AccountIds.Clear());

            // ── Override Postgres DbContextFactory ───────────────────────────
            services.RemoveAll<IDbContextFactory<AgentFwDbContext>>();
            services.RemoveAll<DbContextOptions<AgentFwDbContext>>();
            services.RemoveAll<DbContextOptions>();

            var dbDescriptors = services
                .Where(d => d.ServiceType.FullName?.Contains("AgentFwDbContext") == true ||
                            d.ServiceType.FullName?.Contains("DbContextOptions") == true)
                .ToList();
            foreach (var d in dbDescriptors) services.Remove(d);

            services.AddPooledDbContextFactory<AgentFwDbContext>(o =>
                o.UseNpgsql(AiHubConnectionString()));

            // ── Override NpgsqlDataSource (keyed + non-keyed) ─────────────────
            // Program.cs builds NpgsqlDataSource instances from the connection
            // string at startup, BEFORE ConfigureAppConfiguration runs.
            // Without this override, repositories that use NpgsqlDataSource
            // directly (PgProjectRepository, PgModelCatalogRepository, etc.)
            // would hit the dev database instead of the Testcontainer.
            var descriptorsToRemove = services
                .Where(d => d.ServiceType == typeof(NpgsqlDataSource))
                .ToList();
            foreach (var d in descriptorsToRemove) services.Remove(d);

            var testDataSource = new NpgsqlDataSourceBuilder(AiHubConnectionString()).Build();
            services.AddKeyedSingleton<NpgsqlDataSource>("general", testDataSource);
            services.AddKeyedSingleton<NpgsqlDataSource>("sse", testDataSource);
            services.AddKeyedSingleton<NpgsqlDataSource>("reporting", testDataSource);
            services.AddSingleton(testDataSource);

            // ── Override FunctionToolRegistry (stubs rápidos) ────────────────
            var mockBaseUrl = _apiMock.BaseUrl;
            _mockHttpClient = new HttpClient { BaseAddress = new Uri(mockBaseUrl) };

            services.RemoveAll<IFunctionToolRegistry>();
            services.AddSingleton<IFunctionToolRegistry>(_ =>
            {
                var registry = new FunctionToolRegistry();

                registry.Register("search_asset",
                    AIFunctionFactory.Create(
                        (Func<string, int, Task<string>>)((q, _) => Task.FromResult(
                            """[{"ticker":"PETR4","name":"Petrobras PN","exchange":"BVMF"}]""")),
                        new AIFunctionFactoryOptions { Name = "search_asset" }));

                registry.Register("get_asset_position",
                    AIFunctionFactory.Create(
                        (Func<string, string, Task<string>>)((accountId, ticker) => Task.FromResult("[]")),
                        new AIFunctionFactoryOptions { Name = "get_asset_position" }));

                registry.Register("SendOrder",
                    AIFunctionFactory.Create(
                        (Func<string, Task<string>>)(_ => Task.FromResult(
                            """[{"status":"captured","message":"Test stub"}]""")),
                        new AIFunctionFactoryOptions { Name = "SendOrder" }));

                return registry;
            });
        });
    }

    /// <summary>Connection string do Testcontainer Postgres (schema aihub).</summary>
    public string ConnectionString => AiHubConnectionString();

    private string AiHubConnectionString() => _postgres.GetConnectionString() + ";Search Path=aihub";

    private static HttpClient? _mockHttpClient;

    private static string FindSiblingFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "db", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"db/{fileName} not found in any parent directory.");
    }

    /// <summary>
    /// Creates a client where the DefaultProjectGuard is active with the given admin account.
    /// Use this only in tests that specifically test the default-project protection gate.
    /// </summary>
    public HttpClient CreateClientWithAdminGate(string adminAccountId) =>
        WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            // Re-popula AccountIds APÓS o PostConfigure do factory base que limpa.
            // Múltiplos PostConfigure rodam em ordem de registro, e este é o último.
            services.PostConfigure<EfsAiHub.Host.Api.Configuration.AdminOptions>(o =>
            {
                o.AccountIds.Clear();
                o.AccountIds.Add(adminAccountId);
            });
        })).CreateClient();
}

[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<IntegrationWebApplicationFactory> { }

/// <summary>
/// Extension methods for HttpClient to simplify project- and account-scoped requests in integration tests.
/// </summary>
public static class HttpClientProjectExtensions
{
    public static HttpClient WithProject(this HttpClient client, string projectId)
    {
        client.DefaultRequestHeaders.Remove("x-efs-project-id");
        client.DefaultRequestHeaders.Add("x-efs-project-id", projectId);
        return client;
    }

    public static HttpClient WithAdminAccount(this HttpClient client, string accountId)
    {
        client.DefaultRequestHeaders.Remove("x-efs-account");
        client.DefaultRequestHeaders.Add("x-efs-account", accountId);
        return client;
    }
}
