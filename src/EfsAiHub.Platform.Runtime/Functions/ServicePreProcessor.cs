using System.Text;
using EfsAiHub.Core.Abstractions.Conversations;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EfsAiHub.Platform.Runtime.Functions;

/// <summary>
/// Pre-Processor DelegateExecutor para workflows de atendimento.
/// Recebe ChatTurnContext JSON e enriquece a mensagem do usuário com
/// contexto determinístico: account, horário Brasília, expiração default,
/// e hints de normalização numérica (regex). Registrado como <c>service_pre_processor</c>.
/// </summary>
public static class ServicePreProcessor
{
    private static readonly TimeZoneInfo BrasiliaZone =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "E. South America Standard Time" : "America/Sao_Paulo");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // Regex patterns para hint injection
    private static readonly Regex QuantityMultiplierPattern = new(
        @"(\d+[.,]?\d*)\s*[kK](?:\b|$)", RegexOptions.Compiled);

    private static readonly Regex QuantityMillionPattern = new(
        @"(\d+[.,]?\d*)\s*[mM](?:i(?:lh[ãa]o|lhões)?)?\b", RegexOptions.Compiled);

    private static readonly Regex CurrencyPattern = new(
        @"R\$\s*([\d.,]+)", RegexOptions.Compiled);

    private static readonly Regex DecimalCommaPattern = new(
        @"\b(\d+),(\d{1,2})\b", RegexOptions.Compiled);

    public static Task<string> EnrichInput(string input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Task.FromResult(input);

        ChatTurnContext? ctx;
        try
        {
            ctx = JsonSerializer.Deserialize<ChatTurnContext>(input, JsonOpts);
        }
        catch
        {
            // Não é ChatTurnContext — pass-through sem enriquecer
            return Task.FromResult(input);
        }

        if (ctx is null)
            return Task.FromResult(input);

        // ── 1. Extrair metadata da sessão ────────────────────────────────────
        ctx.Metadata.TryGetValue("account", out var account);
        ctx.Metadata.TryGetValue("userId", out var userId);
        ctx.Metadata.TryGetValue("userType", out var userType);

        // ── 2. Calcular horário Brasília e expiração default ─────────────────
        var brasiliaTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BrasiliaZone);
        var (expireTime, expireDate) = CalculateDefaultExpiration(brasiliaTime);

        // ── 3. Regex hint injection ──────────────────────────────────────────
        var userText = ctx.Message.Content;
        var hints = ExtractHints(userText);

        // ���─ 4. Montar bloco [CONTEXT] ──────────────���─────────────────────────
        var contextBlock = new StringBuilder();
        contextBlock.Append("[CONTEXT: ");

        if (!string.IsNullOrEmpty(account))
            contextBlock.Append($"conta={account}, ");
        else if (!string.IsNullOrEmpty(userId))
            contextBlock.Append($"userId={userId}, ");

        if (!string.IsNullOrEmpty(userType))
            contextBlock.Append($"userType={userType}, ");

        contextBlock.Append($"horario={brasiliaTime:yyyy-MM-ddTHH:mm:ss}, ");
        contextBlock.Append($"expireDefault={{\"expireTime\":\"{expireTime}\",\"expireDate\":\"{expireDate}\"}}");

        if (hints.Count > 0)
        {
            contextBlock.Append(", hints: ");
            contextBlock.Append(string.Join(", ", hints.Select(h => $"\"{h.Key}\"={h.Value}")));
        }

        contextBlock.Append(']');

        // ── 5. Retornar ChatTurnContext com mensagem enriquecida ─────────────
        var enrichedMessage = new ChatTurnMessage
        {
            Role = ctx.Message.Role,
            Content = $"{ctx.Message.Content}\n\n{contextBlock}"
        };

        var enrichedCtx = new ChatTurnContext
        {
            UserId = ctx.UserId,
            ConversationId = ctx.ConversationId,
            Message = enrichedMessage,
            History = ctx.History,
            Metadata = ctx.Metadata
        };

        return Task.FromResult(JsonSerializer.Serialize(enrichedCtx, JsonOpts));
    }

    /// <summary>
    /// Calcula expiração default baseada no horário Brasília:
    /// - Antes de 17:55 do dia útil → mesmo dia 17:55
    /// - Depois de 17:55 ou fim de semana → próximo dia útil 10:30
    /// </summary>
    public static (string expireTime, string expireDate) CalculateDefaultExpiration(DateTime brasiliaTime)
    {
        var marketCloseTime = new TimeSpan(17, 55, 0);

        if (IsBusinessDay(brasiliaTime) && brasiliaTime.TimeOfDay < marketCloseTime)
        {
            return ("17:55", brasiliaTime.ToString("yyyy-MM-dd"));
        }

        // Próximo dia útil
        var nextBizDay = GetNextBusinessDay(brasiliaTime.Date);
        return ("10:30", nextBizDay.ToString("yyyy-MM-dd"));
    }

    private static bool IsBusinessDay(DateTime date) =>
        date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

    private static DateTime GetNextBusinessDay(DateTime date)
    {
        var next = date.AddDays(1);
        while (!IsBusinessDay(next))
            next = next.AddDays(1);
        return next;
    }

    /// <summary>
    /// Extrai hints de normalização numérica do texto usando regex.
    /// Retorna dictionary original → valor normalizado.
    /// </summary>
    public static Dictionary<string, string> ExtractHints(string text)
    {
        var hints = new Dictionary<string, string>();

        // "1k" → 1000, "1.5k" → 1500, "1,5k" → 1500
        foreach (Match m in QuantityMultiplierPattern.Matches(text))
        {
            var numStr = m.Groups[1].Value.Replace(',', '.');
            if (double.TryParse(numStr, System.Globalization.CultureInfo.InvariantCulture, out var num))
                hints[m.Value.Trim()] = ((long)(num * 1000)).ToString();
        }

        // "2M" → 2000000 (mas não confundir com "M" de mercado isolado)
        foreach (Match m in QuantityMillionPattern.Matches(text))
        {
            var numStr = m.Groups[1].Value.Replace(',', '.');
            if (double.TryParse(numStr, System.Globalization.CultureInfo.InvariantCulture, out var num))
                hints[m.Value.Trim()] = ((long)(num * 1_000_000)).ToString();
        }

        // "R$ 95,50" → 95.50
        foreach (Match m in CurrencyPattern.Matches(text))
        {
            var numStr = m.Groups[1].Value.Replace(".", "").Replace(',', '.');
            if (double.TryParse(numStr, System.Globalization.CultureInfo.InvariantCulture, out var num))
                hints[$"R${m.Groups[1].Value.Trim()}"] = num.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }

        // "95,50" → 95.50 (vírgula decimal brasileira, apenas se não já capturado por R$)
        foreach (Match m in DecimalCommaPattern.Matches(text))
        {
            var full = m.Value;
            if (!hints.ContainsKey($"R${full}") && !hints.ContainsKey(full))
            {
                var normalized = $"{m.Groups[1].Value}.{m.Groups[2].Value}";
                hints[full] = normalized;
            }
        }

        return hints;
    }
}
