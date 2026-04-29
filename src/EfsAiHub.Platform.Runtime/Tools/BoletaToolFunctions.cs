using System.ComponentModel;
using System.Text.Json;

namespace EfsAiHub.Platform.Runtime.Tools;

/// <summary>
/// Tools usadas pelo agente de boletas.
/// Registrada como singleton no DI — usa IHttpClientFactory para chamadas ao backend EFS.
/// </summary>
public class BoletaToolFunctions(IHttpClientFactory httpClientFactory, ILogger<BoletaToolFunctions> logger)
{
    private static readonly TimeZoneInfo BrasiliaZone =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "E. South America Standard Time" : "America/Sao_Paulo");

    private HttpClient GetHttp() => httpClientFactory.CreateClient("efs-backend");

    // ── PegarHorarioBrasileiro ────────────────────────────────────────────────

    [Description("Retorna a data e hora atual no fuso horário de Brasília (UTC-3). Use sempre antes de definir qualquer expireTime.")]
    public Task<string> PegarHorarioBrasileiro()
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BrasiliaZone);
        return Task.FromResult(now.ToString("yyyy-MM-ddTHH:mm:ss"));
    }

    // ── search_asset ─────────────────────────────────────────────────────────

    [Description("Busca ativos financeiros por ticker, nome, apelido, setor ou linguagem natural de trading. Retorna até top_k resultados ordenados por relevância e liquidez.")]
    public async Task<string> SearchAsset(
        [Description("Query de busca: ticker, nome, apelido, setor ou linguagem natural (ex: 'Petrobras', 'PETR4', 'bancos', 'petróleo')")]
        string query,
        [Description("Número máximo de resultados (padrão: 3)")]
        int top_k = 3)
    {
        var ticker = query.Trim().ToUpper();
        try
        {
            var response = await GetHttp().GetAsync($"/ativos/{Uri.EscapeDataString(ticker)}");
            var body = await response.Content.ReadAsStringAsync();

            // API retorna { "found": bool, "ativo": { "ticker": "...", "nome": "..." } | null }
            // Normaliza para o formato de array esperado pelo LLM: [{ ticker, name, exchange }]
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var found = root.TryGetProperty("found", out var foundEl) && foundEl.GetBoolean();

            if (!found || !root.TryGetProperty("ativo", out var ativoEl) || ativoEl.ValueKind == JsonValueKind.Null)
                return $"[{{\"found\":false,\"ticker\":\"{ticker}\",\"message\":\"Ativo não encontrado.\"}}]";

            var nome = ativoEl.TryGetProperty("nome", out var nomeEl) ? nomeEl.GetString() : ticker;
            var result = new[] { new { ticker, name = nome, exchange = "BVMF" } };
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SearchAsset falhou para ticker '{Ticker}'.", ticker);
            return $"[{{\"found\":false,\"ticker\":\"{ticker}\",\"message\":\"Erro ao buscar ativo: {ex.Message}\"}}]";
        }
    }

    // ── get_asset_position ────────────────────────────────────────────────────

    [Description("Consulta a posição atual do cliente em um ativo específico. Retorna ticker, totalQuantity e financialVolume (BRL).")]
    public async Task<string> GetAssetPosition(
        [Description("Conta operacional do cliente")]
        string conta,
        [Description("Ticker canônico do ativo (ex: PETR4)")]
        string ticker)
    {
        try
        {
            var url = $"/posicao?conta={Uri.EscapeDataString(conta)}&ticker={Uri.EscapeDataString(ticker.ToUpper())}";
            var response = await GetHttp().GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            // Normaliza resposta da API: renomeia "volume" → "financialVolume" para consistência com OutputRelatorio
            using var doc = JsonDocument.Parse(body);
            var items = new List<object>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                items.Add(new
                {
                    ticker = el.TryGetProperty("ticker", out var t) ? t.GetString() : ticker.ToUpper(),
                    totalQuantity = el.TryGetProperty("totalQuantity", out var q) ? q.GetDouble() : 0,
                    financialVolume = el.TryGetProperty("volume", out var v) ? v.GetDouble()
                                   : el.TryGetProperty("financialVolume", out var fv) ? fv.GetDouble() : 0
                });
            }
            return JsonSerializer.Serialize(items);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GetAssetPosition falhou para conta '{Conta}' ticker '{Ticker}'.", conta, ticker);
            return $"[{{\"ticker\":\"{ticker.ToUpper()}\",\"totalQuantity\":0,\"financialVolume\":0.0,\"message\":\"Erro ao obter posição: {ex.Message}\"}}]";
        }
    }

    // ── SendOrder ────────────────────────────────────────────────────────────
    //
    // DESIGN INTENCIONAL: esta tool NÃO executa a ordem no OMS.
    // O sistema EfsAiHub.Api é responsável por PRODUZIR os dados da ordem
    // (validação, enriquecimento, formatação). A execução no OMS é
    // responsabilidade do CONSUMIDOR desta API, que recebe o campo
    // `output.boletas` no retorno do endpoint de mensagens e encaminha ao OMS.
    //
    // Fluxo: LLM chama SendOrder → confirma captura dos dados → inclui boletas
    // no OutputAtendimento (structured output) → consumidor da API executa no OMS.

    [Description("Registra a intenção de ordem validada. NÃO executa no OMS — a execução é responsabilidade do consumidor da API que receber o retorno. Use apenas quando todos os campos obrigatórios estiverem completos.")]
    public Task<string> SendOrder(
        [Description("Array JSON das boletas a registrar. Cada boleta deve conter order_type (Buy|Sell), ticker, account, quantity ou volume, priceType (M|L|F) e, se L, priceLimit.")]
        string boletas)
    {
        JsonDocument? parsed = null;
        try { parsed = JsonDocument.Parse(boletas); }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "SendOrder: JSON de boletas inválido.");
        }

        if (parsed is null)
            return Task.FromResult("""[{"status":"error","message":"JSON de boletas inválido."}]""");

        // AccountGuard server-side (ClientLocked): rejeita boletas com account != userId da sessão.
        var ctx = EfsAiHub.Core.Orchestration.Executors.DelegateExecutor.Current.Value;
        var guardClientLocked =
            ctx?.GuardMode == EfsAiHub.Core.Agents.Execution.AccountGuardMode.ClientLocked
            && !string.IsNullOrEmpty(ctx!.UserId);

        // Confirma captura dos dados para o LLM. A execução real fica com o consumidor da API.
        var results = new System.Text.StringBuilder("[");
        bool first = true;
        foreach (var boleta in parsed.RootElement.EnumerateArray())
        {
            if (!first) results.Append(',');
            var ticker = boleta.TryGetProperty("ticker", out var t) ? t.GetString() : "?";

            if (guardClientLocked)
            {
                var boletaAccount = boleta.TryGetProperty("account", out var a) ? a.GetString() : null;
                if (!string.Equals(boletaAccount, ctx!.UserId, StringComparison.Ordinal))
                {
                    EfsAiHub.Infra.Observability.MetricsRegistry.ToolAccountRejections.Add(1,
                        new KeyValuePair<string, object?>("tool.name", "SendOrder"));
                    logger.LogError(
                        "[AccountGuard] SendOrder rejeitou boleta com account='{Got}' (esperado '{Expected}'), ticker='{Ticker}'.",
                        boletaAccount, ctx.UserId, ticker);
                    results.Append($$$"""{"status":"rejected","reason":"account_mismatch","ticker":"{{{ticker}}}","message":"Boleta rejeitada: account divergente da sess\u00e3o."}""");
                    first = false;
                    continue;
                }
            }

            var refId = Guid.NewGuid().ToString("N")[..8].ToUpper();
            results.Append($$$"""{"refId":"{{{refId}}}","status":"captured","ticker":"{{{ticker}}}","message":"Dados da ordem capturados. Execu\u00e7\u00e3o no OMS ser\u00e1 realizada pelo consumidor da API."}""");
            first = false;
        }
        results.Append(']');

        return Task.FromResult(results.ToString());
    }
}
