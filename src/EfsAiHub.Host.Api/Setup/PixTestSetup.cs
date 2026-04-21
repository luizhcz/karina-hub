using System.Text.Json;

namespace EfsAiHub.Host.Api.CodeExecutors;

/// <summary>
/// Registra code executors para o workflow de teste "pix-transfer".
/// Lógica 100% em código (sem LLM no caminho crítico).
///
/// HITL é tratado de forma declarativa no JSON do workflow (NodeHitlConfig),
/// aplicado automaticamente pelo HitlDecoratorExecutor no engine.
/// Os executores aqui contêm apenas lógica de negócio pura.
/// </summary>
public static class PixTestSetup
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void RegisterPixExecutors(this WebApplication app)
    {
        var codeRegistry = app.Services.GetRequiredService<ICodeExecutorRegistry>();

        codeRegistry.Register("pix_validate", ValidateAsync);
        codeRegistry.Register("pix_execute_transfer", ExecuteTransferAsync);
    }

    // ── Executor 1: Validação pura ─────────────────────────────────────────────

    private static Task<string> ValidateAsync(string input, CancellationToken ct)
    {
        var pix = JsonSerializer.Deserialize<PixRequest>(input, JsonOpts)
            ?? throw new InvalidOperationException($"JSON inválido para PixRequest: {input}");

        // ── Validação básica ──
        if (string.IsNullOrWhiteSpace(pix.Destinatario) || string.IsNullOrWhiteSpace(pix.ChavePix))
            return Task.FromResult(Error("Destinatário e chave PIX são obrigatórios."));

        if (pix.Valor <= 0)
            return Task.FromResult(Error("Valor deve ser positivo."));

        // ── Retorna dados validados ──
        var result = JsonSerializer.Serialize(new
        {
            status = "validated",
            pix.Destinatario,
            pix.ChavePix,
            pix.Valor,
            pix.Descricao,
            message = $"Dados validados: PIX de R$ {pix.Valor:N2} para {pix.Destinatario} (chave: {pix.ChavePix})."
        });

        return Task.FromResult(result);
    }

    // ── Executor 2: Execução da transferência ──────────────────────────────────

    private static Task<string> ExecuteTransferAsync(string input, CancellationToken ct)
    {
        var data = JsonSerializer.Deserialize<JsonElement>(input);

        var status = data.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (status == "error")
        {
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                status = "error",
                message = data.TryGetProperty("message", out var msg) ? msg.GetString() : "Dados inválidos.",
            }));
        }

        var txId = $"PIX{DateTimeOffset.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(1000, 9999)}";

        var destinatario = data.TryGetProperty("destinatario", out var d) ? d.GetString() : "N/A";
        if (destinatario == "N/A" && data.TryGetProperty("Destinatario", out var d2))
            destinatario = d2.GetString();

        var valor = 0m;
        if (data.TryGetProperty("valor", out var v)) valor = v.GetDecimal();
        else if (data.TryGetProperty("Valor", out var v2)) valor = v2.GetDecimal();

        var result = JsonSerializer.Serialize(new
        {
            status = "executed",
            transactionId = txId,
            destinatario,
            valor,
            executedAt = DateTimeOffset.UtcNow,
            message = $"PIX de R$ {valor:N2} para {destinatario} executado com sucesso. TX: {txId}"
        });

        return Task.FromResult(result);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static string Error(string message)
        => JsonSerializer.Serialize(new { status = "error", message });

    // ── DTO ─────────────────────────────────────────────────────────────────────

    private sealed record PixRequest(
        string Destinatario,
        string ChavePix,
        decimal Valor,
        string? Descricao = null);
}
