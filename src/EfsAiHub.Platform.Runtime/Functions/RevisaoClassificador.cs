using System.Text.Json;
using EfsAiHub.Core.Agents.Trading;

namespace EfsAiHub.Platform.Runtime.Functions;

/// <summary>
/// Classifica o output em texto livre do agente <c>revisor-analise-ativo</c>:
/// <list type="bullet">
///   <item>Texto começando com <c>REPROVADO:</c> → <see cref="RevisaoResultado.Status"/> = "REPROVADO" + Reasoning.</item>
///   <item>JSON válido de <see cref="Ativo"/> → Status = "APROVADO" + AprovadoPayload.</item>
/// </list>
/// Predicate de Switch (<c>$.status</c>) decide entre voltar para o escritor (loop) ou seguir para save-ativo.
/// </summary>
public static class RevisaoClassificador
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static Task<RevisaoResultado> ClassifyAsync(string input, CancellationToken ct)
    {
        var trimmed = input?.Trim() ?? "";

        if (trimmed.StartsWith("REPROVADO", StringComparison.OrdinalIgnoreCase))
        {
            var reasoning = ExtractReprovacaoReason(trimmed);
            return Task.FromResult(new RevisaoResultado
            {
                Status = "REPROVADO",
                Reasoning = reasoning
            });
        }

        Ativo? ativo = null;
        try
        {
            ativo = JsonSerializer.Deserialize<Ativo>(trimmed, JsonOpts);
        }
        catch (JsonException)
        {
            // Texto que não é REPROVADO nem JSON Ativo válido → tratar como reprovação
            // pra não quebrar workflow; loop volta pro escritor com o texto bruto.
            return Task.FromResult(new RevisaoResultado
            {
                Status = "REPROVADO",
                Reasoning = $"Output do revisor não é JSON Ativo válido nem REPROVADO explícito. Texto: {trimmed}"
            });
        }

        if (ativo is null || string.IsNullOrWhiteSpace(ativo.Ticker))
            return Task.FromResult(new RevisaoResultado
            {
                Status = "REPROVADO",
                Reasoning = "Output do revisor parseou como Ativo mas Ticker está vazio."
            });

        return Task.FromResult(new RevisaoResultado
        {
            Status = "APROVADO",
            AprovadoPayload = ativo
        });
    }

    /// <summary>Texto pra retornar ao escritor no loop de reprovação.</summary>
    public static string FormatReprovacaoForEscritor(RevisaoResultado result)
    {
        var reason = string.IsNullOrWhiteSpace(result.Reasoning)
            ? "(sem detalhes)"
            : result.Reasoning;
        return
            "Sua análise anterior foi REPROVADA pelo revisor de compliance. Refaça atendendo às regras informadas.\n\n" +
            "Motivo da reprovação:\n" + reason;
    }

    private static string ExtractReprovacaoReason(string text)
    {
        // Formato esperado:
        //   REPROVADO: <motivo>
        //   JSON_ANTERIOR: <json>
        // Mantém só a parte antes de JSON_ANTERIOR pra reasoning ficar legível.
        var jsonMarker = text.IndexOf("JSON_ANTERIOR", StringComparison.OrdinalIgnoreCase);
        var slice = jsonMarker > 0 ? text[..jsonMarker].TrimEnd() : text;

        var colon = slice.IndexOf(':');
        return colon > 0 && colon + 1 < slice.Length
            ? slice[(colon + 1)..].Trim()
            : slice.Trim();
    }
}
