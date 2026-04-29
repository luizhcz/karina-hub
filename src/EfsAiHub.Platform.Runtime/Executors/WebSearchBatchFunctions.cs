using EfsAiHub.Platform.Runtime.Tools;

namespace EfsAiHub.Platform.Runtime.Executors;

/// <summary>
/// Pesquisa web unitária para uso como code executor no modo Graph.
/// </summary>
public static class WebSearchBatchFunctions
{
    /// <summary>
    /// Pesquisa um único ativo. Recebe "NEXT_ASSET:ticker,nome" (saída da fila)
    /// ou "ticker,nome" diretamente. Retorna contexto formatado para o LLM.
    /// </summary>
    public static async Task<string> SearchSingle(string input, CancellationToken ct = default)
    {
        const string Prefix = "NEXT_ASSET:";
        var line = input.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? input[Prefix.Length..]
            : input;

        var parts = line.Split(',', 2);
        var ticker = parts[0].Trim();
        var nome = parts.Length > 1 ? parts[1].Trim() : ticker;

        // Passa "ticker nome" para que o scraping financeiro consiga extrair o ticker
        var searchQuery = ticker != nome ? $"{ticker} {nome}" : nome;
        var result = await WebSearchFunctions.SearchWeb(searchQuery);

        return $"Ticker: {ticker}\nNome: {nome}\n\n{result}";
    }
}
