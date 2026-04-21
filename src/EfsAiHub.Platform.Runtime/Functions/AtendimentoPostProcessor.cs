using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EfsAiHub.Platform.Runtime.Functions;

/// <summary>
/// Post-Processor DelegateExecutor para workflows de atendimento (Layer 2 — domain validation).
/// Aceita tanto o formato legacy (OutputAtendimento flat) quanto o novo formato envelope
/// (AgentOutputEnvelope com payload de boleta). Valida schema e business rules,
/// preenche defaults, e retorna JSON validado ou erro para feedback loop condicional.
///
/// Nota: quando o workflow usa <c>generic_enricher</c>, o enrichment de defaults
/// (expireTime, account) é feito lá via regras declarativas.
/// O EnrichBoletaDefaults permanece para backward compat com workflows v1.
/// </summary>
public static class AtendimentoPostProcessor
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> ValidOrderTypes = new(StringComparer.OrdinalIgnoreCase) { "Buy", "Sell" };
    private static readonly HashSet<string> ValidPriceTypes = new(StringComparer.OrdinalIgnoreCase) { "M", "L", "F" };
    private static readonly HashSet<string> ValidResponseTypes = new(StringComparer.OrdinalIgnoreCase) { "boleta", "relatorio", "texto", "recomendacao" };

    // Regex para extrair [CONTEXT: ...] do input que pode vir encadeado
    private static readonly Regex ContextBlockPattern = new(
        @"\[CONTEXT:\s*(.+?)\]", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex ExpireDefaultPattern = new(
        @"expireDefault=\{""expireTime"":""([^""]*)"",""expireDate"":""([^""]*)""\}",
        RegexOptions.Compiled);

    private static readonly Regex ContaPattern = new(
        @"conta=(\S+?)(?:,|$|\])", RegexOptions.Compiled);

    public static Task<string> ValidateAndEnrich(string input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Task.FromResult(BuildErrorJson(["Output vazio do agente."], null));

        // Detectar formato: envelope (tem "payload") ou legacy (OutputAtendimento flat)
        bool isEnvelope = false;
        OutputAtendimento? output;
        try
        {
            using var probe = JsonDocument.Parse(input);
            isEnvelope = probe.RootElement.TryGetProperty("payload", out _);

            if (isEnvelope)
            {
                // Envelope format: extrair boletas do payload e construir OutputAtendimento
                output = DeserializeFromEnvelope(probe.RootElement);
            }
            else
            {
                output = JsonSerializer.Deserialize<OutputAtendimento>(input, JsonOpts);
            }
        }
        catch (JsonException ex)
        {
            return Task.FromResult(BuildErrorJson([$"JSON inválido: {ex.Message}"], input));
        }

        if (output is null)
            return Task.FromResult(BuildErrorJson(["Output deserializou como null."], input));

        var errors = new List<string>();

        // ── 1. Validar response_type ─────────────────────────────────────────
        if (!ValidResponseTypes.Contains(output.ResponseType))
            errors.Add($"response_type '{output.ResponseType}' inválido. Esperado: boleta, relatorio, texto.");

        // ── 2. Validar message ───────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(output.Message))
            errors.Add("Campo 'message' é obrigatório.");

        // ── 3. Validar ui_component ──────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(output.UiComponent))
            errors.Add("Campo 'ui_component' é obrigatório.");

        // ── 4. Auto-corrigir boleta incompleta → texto ──────────────────────
        // Quando o LLM classifica como "boleta" mas não tem dados suficientes
        // (boletas null/vazio + ui_component=incomplete_card), corrigir para "texto"
        if (output.ResponseType.Equals("boleta", StringComparison.OrdinalIgnoreCase)
            && (output.Boletas is null || output.Boletas.Count == 0)
            && output.UiComponent is "incomplete_card" or "out_of_scope")
        {
            output.ResponseType = "texto";
            output.Command = null;
        }

        // ── 5. Validação específica por response_type ────────────────────────
        switch (output.ResponseType.ToLowerInvariant())
        {
            case "boleta":
                ValidateBoletas(output, errors);
                break;
            case "relatorio":
                ValidateRelatorio(output, errors);
                break;
            case "texto":
                // Texto livre — apenas message/ui_component já validados acima
                break;
        }

        if (errors.Count > 0)
            return Task.FromResult(BuildErrorJson(errors, input));

        // ── 5. Preencher defaults (boleta) — apenas para legacy (sem generic_enricher)
        if (!isEnvelope && output.ResponseType.Equals("boleta", StringComparison.OrdinalIgnoreCase) && output.Boletas is not null)
        {
            EnrichBoletaDefaults(output, input);
        }

        // ── 6. Retornar output validado e enriquecido ───────────────────────
        if (isEnvelope)
        {
            // Re-serializar em formato envelope preservando a estrutura original
            return Task.FromResult(SerializeAsEnvelope(output, input));
        }
        return Task.FromResult(JsonSerializer.Serialize(output, JsonOpts));
    }

    private static void ValidateBoletas(OutputAtendimento output, List<string> errors)
    {
        if (output.Boletas is null || output.Boletas.Count == 0)
        {
            errors.Add("response_type='boleta' requer ao menos uma boleta em 'boletas'.");
            return;
        }

        for (int i = 0; i < output.Boletas.Count; i++)
        {
            var b = output.Boletas[i];
            var prefix = output.Boletas.Count > 1 ? $"boletas[{i}]: " : "";

            if (!ValidOrderTypes.Contains(b.OrderType))
                errors.Add($"{prefix}order_type '{b.OrderType}' inválido. Esperado: Buy ou Sell.");

            if (string.IsNullOrWhiteSpace(b.Ticker))
                errors.Add($"{prefix}ticker é obrigatório.");

            if (!ValidPriceTypes.Contains(b.PriceType))
                errors.Add($"{prefix}priceType '{b.PriceType}' inválido. Esperado: M, L ou F.");

            // priceLimit obrigatório quando priceType=L
            if (b.PriceType.Equals("L", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(b.PriceLimit) || b.PriceLimit == "0")
                    errors.Add($"{prefix}priceType 'L' requer priceLimit > 0.");
            }

            // Pelo menos quantity ou volume deve estar preenchido
            var hasQuantity = !string.IsNullOrWhiteSpace(b.Quantity) && b.Quantity != "0";
            var hasVolume = !string.IsNullOrWhiteSpace(b.Volume) && b.Volume != "0";
            if (!hasQuantity && !hasVolume)
                errors.Add($"{prefix}quantity ou volume deve ser preenchido.");
        }

        if (string.IsNullOrWhiteSpace(output.Command))
            errors.Add("response_type='boleta' requer campo 'command'.");
    }

    private static void ValidateRelatorio(OutputAtendimento output, List<string> errors)
    {
        if (output.Posicoes is null)
            errors.Add("response_type='relatorio' requer campo 'posicoes' (pode ser lista vazia).");
    }

    private static void EnrichBoletaDefaults(OutputAtendimento output, string rawInput)
    {
        // Extrair [CONTEXT] block do raw input (pode estar encadeado nos dados anteriores)
        var contextMatch = ContextBlockPattern.Match(rawInput);
        string? defaultExpireTime = null;
        string? defaultExpireDate = null;
        string? sessionAccount = null;

        if (contextMatch.Success)
        {
            var contextContent = contextMatch.Groups[1].Value;

            var expireMatch = ExpireDefaultPattern.Match(contextContent);
            if (expireMatch.Success)
            {
                defaultExpireTime = expireMatch.Groups[1].Value;
                defaultExpireDate = expireMatch.Groups[2].Value;
            }

            var contaMatch = ContaPattern.Match(contextContent);
            if (contaMatch.Success)
                sessionAccount = contaMatch.Groups[1].Value;
        }

        foreach (var b in output.Boletas!)
        {
            // Preencher expireTime default se vazio
            if (string.IsNullOrWhiteSpace(b.ExpireTime) && !string.IsNullOrWhiteSpace(defaultExpireTime))
                b.ExpireTime = $"{defaultExpireDate} {defaultExpireTime}";

            // Preencher account da sessão se vazio
            if (string.IsNullOrWhiteSpace(b.Account) && !string.IsNullOrWhiteSpace(sessionAccount))
                b.Account = sessionAccount;

            // Limpar priceLimit quando priceType=M (mercado não usa limite)
            if (b.PriceType.Equals("M", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(b.PriceLimit) && b.PriceLimit != "0")
            {
                b.PriceLimit = "";
            }
        }
    }

    /// <summary>
    /// Deserializa envelope format → OutputAtendimento extraindo boletas/posicoes do payload.
    /// </summary>
    private static OutputAtendimento? DeserializeFromEnvelope(JsonElement root)
    {
        var responseType = root.GetProperty("response_type").GetString() ?? "";
        var message = root.GetProperty("message").GetString() ?? "";
        var uiComponent = root.TryGetProperty("ui_component", out var uiProp) ? uiProp.GetString() ?? "" : "";
        var command = root.TryGetProperty("command", out var cmdProp) ? cmdProp.GetString() : null;

        List<Boleta>? boletas = null;
        List<PosicaoCliente>? posicoes = null;

        if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("boletas", out var boletasEl))
                boletas = JsonSerializer.Deserialize<List<Boleta>>(boletasEl.GetRawText(), JsonOpts);

            if (payload.TryGetProperty("command", out var payloadCmd) && command is null)
                command = payloadCmd.GetString();

            if (payload.TryGetProperty("posicoes", out var posicoesEl))
                posicoes = JsonSerializer.Deserialize<List<PosicaoCliente>>(posicoesEl.GetRawText(), JsonOpts);
        }

        return new OutputAtendimento
        {
            ResponseType = responseType,
            Message = message,
            UiComponent = uiComponent,
            Command = command,
            Boletas = boletas,
            Posicoes = posicoes
        };
    }

    /// <summary>
    /// Re-serializa OutputAtendimento validado de volta para formato envelope,
    /// preservando campos extras do input original (payload, enrichment, etc.).
    /// </summary>
    private static string SerializeAsEnvelope(OutputAtendimento output, string originalInput)
    {
        using var doc = JsonDocument.Parse(originalInput);
        var originalNode = System.Text.Json.Nodes.JsonNode.Parse(originalInput);
        if (originalNode is not System.Text.Json.Nodes.JsonObject envelope)
            return JsonSerializer.Serialize(output, JsonOpts);

        // Atualizar campos do envelope com valores possivelmente corrigidos
        envelope["response_type"] = output.ResponseType;
        envelope["message"] = output.Message;
        envelope["ui_component"] = output.UiComponent;

        // Atualizar payload com boletas/command validados
        if (output.Boletas is not null && envelope["payload"] is System.Text.Json.Nodes.JsonObject payloadObj)
        {
            payloadObj["boletas"] = System.Text.Json.Nodes.JsonNode.Parse(
                JsonSerializer.Serialize(output.Boletas, JsonOpts));
            if (output.Command is not null)
                payloadObj["command"] = output.Command;
        }

        return envelope.ToJsonString(JsonOpts);
    }

    private static string BuildErrorJson(List<string> errors, string? originalOutput)
    {
        var errorObj = new
        {
            errors,
            original = originalOutput
        };
        return JsonSerializer.Serialize(errorObj, JsonOpts);
    }
}
