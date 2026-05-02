using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Migrations.PinningV1;

/// <summary>
/// Script standalone one-shot — regenera todos os agent_versions com o formato v1 final
/// (lossless, sem SchemaVersion discriminator) e auto-pina workflows em current de cada agent ref.
/// Workflows com agent refs órfãos (agent não existe mais) são deletados.
///
/// Conexão lida da env <c>ConnectionStrings__Postgres</c>. Roda uma vez, é deletado do repo
/// após sucesso. Não há tabela de migrations_applied — idempotência via deleção do código.
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings:Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine(
                "Erro: env var ConnectionStrings__Postgres não setada. " +
                "Exemplo: ConnectionStrings__Postgres=\"Host=localhost;Port=5432;Database=efs_ai_hub;Username=efs_ai_hub;Password=...;Search Path=aihub\"");
            return 1;
        }

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(b =>
            {
                b.ClearProviders();
                b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
                b.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton(new MigrationOptions(connectionString));
                services.AddTransient<MigrationRunner>();
            })
            .Build();

        var runner = host.Services.GetRequiredService<MigrationRunner>();
        var logger = host.Services.GetRequiredService<ILogger<MigrationRunner>>();

        try
        {
            await runner.RunAsync(CancellationToken.None);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Migration] Falha — transação rollbackada, DB inalterado.");
            return 1;
        }
    }
}

internal sealed record MigrationOptions(string ConnectionString);
