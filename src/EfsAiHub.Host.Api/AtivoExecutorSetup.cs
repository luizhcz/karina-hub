using System.Text.Json;
using System.Text.Json.Serialization;

namespace EfsAiHub.Host.Api.CodeExecutors;

/// <summary>
/// Registra os code executors relacionados a ativos financeiros no <see cref="ICodeExecutorRegistry"/>.
/// Chamado via extensão de <see cref="WebApplication"/> após o contêiner ser construído,
/// pois os executors são funções que capturam serviços de infraestrutura (IAtivoRepository).
/// </summary>
public static class AtivoExecutorSetup
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void RegisterAtivoExecutors(this WebApplication app)
    {
        var ativoRepo = app.Services.GetRequiredService<IAtivoRepository>();
        var codeRegistry = app.Services.GetRequiredService<ICodeExecutorRegistry>();

        codeRegistry.Register("save_ativo_exec", async (input, ct) =>
        {
            var ativo = JsonSerializer.Deserialize<Ativo>(input, JsonOpts)
                ?? throw new InvalidOperationException($"JSON inválido para Ativo: {input}");
            await ativoRepo.UpsertAsync(ativo, ct);

            // Padronização: propagar setor/descrição para siblings com mesmo prefixo (4 chars)
            if (ativo.Ticker.Length >= 4 && !string.IsNullOrWhiteSpace(ativo.Descricao))
            {
                var prefix = ativo.Ticker[..4].ToUpperInvariant();
                var siblings = await ativoRepo.GetSiblingsByPrefixAsync(prefix, ativo.Ticker, ct);
                foreach (var sib in siblings)
                {
                    sib.Setor = ativo.Setor;
                    sib.Descricao = ativo.Descricao;
                    await ativoRepo.UpsertAsync(sib, ct);
                }
            }

            return $"Ativo '{ativo.Ticker}' salvo com sucesso.";
        });
    }
}
