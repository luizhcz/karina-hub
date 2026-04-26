namespace EfsAiHub.Host.Api.CodeExecutors;

/// <summary>
/// Resultado tipado de save_ativo_exec — desacopla mensagem humana
/// (apresentação) de schema (estrutura consumível por edges tipados).
/// </summary>
public sealed record AtivoSavedResult(string Ticker, int SiblingsUpdated, string Message);

/// <summary>
/// Registra os code executors relacionados a ativos financeiros no <see cref="ICodeExecutorRegistry"/>.
/// Chamado via extensão de <see cref="WebApplication"/> após o contêiner ser construído,
/// pois os executors são funções que capturam serviços de infraestrutura (IAtivoRepository).
/// </summary>
public static class AtivoExecutorSetup
{
    public static void RegisterAtivoExecutors(this WebApplication app)
    {
        var ativoRepo = app.Services.GetRequiredService<IAtivoRepository>();
        var codeRegistry = app.Services.GetRequiredService<ICodeExecutorRegistry>();

        codeRegistry.Register<Ativo, AtivoSavedResult>("save_ativo_exec", async (ativo, ct) =>
        {
            await ativoRepo.UpsertAsync(ativo, ct);

            // Padronização: propagar setor/descrição para siblings com mesmo prefixo (4 chars)
            var siblingsUpdated = 0;
            if (ativo.Ticker.Length >= 4 && !string.IsNullOrWhiteSpace(ativo.Descricao))
            {
                var prefix = ativo.Ticker[..4].ToUpperInvariant();
                var siblings = await ativoRepo.GetSiblingsByPrefixAsync(prefix, ativo.Ticker, ct);
                foreach (var sib in siblings)
                {
                    sib.Setor = ativo.Setor;
                    sib.Descricao = ativo.Descricao;
                    await ativoRepo.UpsertAsync(sib, ct);
                    siblingsUpdated++;
                }
            }

            return new AtivoSavedResult(ativo.Ticker, siblingsUpdated, $"Ativo '{ativo.Ticker}' salvo com sucesso.");
        });
    }
}
