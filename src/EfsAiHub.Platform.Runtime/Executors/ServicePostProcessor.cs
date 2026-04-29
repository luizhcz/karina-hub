using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EfsAiHub.Core.Agents.Trading;

namespace EfsAiHub.Platform.Runtime.Executors;

/// <summary>
/// Post-Processor para workflows de atendimento. Aceita envelope (com payload aninhado)
/// ou OutputAtendimento flat, valida schema/business rules, preenche defaults e devolve
/// um <see cref="PostProcessorResult"/> tipado. Predicate de Switch usa <c>$.hasErrors</c>.
/// Registrado como <c>service_post_processor</c>.
/// </summary>
public static class ServicePostProcessor
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

    private static readonly Regex ContextBlockPattern = new(
        @"\[CONTEXT:\s*(.+?)\]", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex ExpireDefaultPattern = new(
        @"expireDefault=\{""expireTime"":""([^""]*)"",""expireDate"":""([^""]*)""\}",
        RegexOptions.Compiled);

    private static readonly Regex ContaPattern = new(
        @"conta=(\S+?)(?:,|$|\])", RegexOptions.Compiled);

    public static Task<PostProcessorResult> ValidateAndEnrichTyped(string input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Task.FromResult(new PostProcessorResult
            {
                HasErrors = true,
                Errors = ["Output vazio do agente."],
                OriginalOutput = input
            });

        bool isEnvelope = false;
        OutputAtendimento? output;
        try
        {
            using var probe = JsonDocument.Parse(input);
            isEnvelope = probe.RootElement.TryGetProperty("payload", out _);

            if (isEnvelope)
                output = DeserializeFromEnvelope(probe.RootElement);
            else
                output = JsonSerializer.Deserialize<OutputAtendimento>(input, JsonOpts);
        }
        catch (JsonException ex)
        {
            return Task.FromResult(new PostProcessorResult
            {
                HasErrors = true,
                Errors = [$"JSON inválido: {ex.Message}"],
                OriginalOutput = input
            });
        }

        if (output is null)
            return Task.FromResult(new PostProcessorResult
            {
                HasErrors = true,
                Errors = ["Output deserializou como null."],
                OriginalOutput = input
            });

        var errors = new List<string>();

        if (!ValidResponseTypes.Contains(output.ResponseType))
            errors.Add($"response_type '{output.ResponseType}' inválido. Esperado: boleta, relatorio, texto.");

        if (string.IsNullOrWhiteSpace(output.Message))
            errors.Add("Campo 'message' é obrigatório.");

        if (string.IsNullOrWhiteSpace(output.UiComponent))
            errors.Add("Campo 'ui_component' é obrigatório.");

        if (output.ResponseType.Equals("boleta", StringComparison.OrdinalIgnoreCase)
            && (output.Boletas is null || output.Boletas.Count == 0)
            && output.UiComponent is "incomplete_card" or "out_of_scope")
        {
            output.ResponseType = "texto";
            output.Command = null;
        }

        switch (output.ResponseType.ToLowerInvariant())
        {
            case "boleta":
                ValidateBoletas(output, errors);
                break;
            case "relatorio":
                ValidateRelatorio(output, errors);
                break;
        }

        if (errors.Count > 0)
            return Task.FromResult(new PostProcessorResult
            {
                HasErrors = true,
                Errors = errors,
                OriginalOutput = input
            });

        if (!isEnvelope && output.ResponseType.Equals("boleta", StringComparison.OrdinalIgnoreCase) && output.Boletas is not null)
            EnrichBoletaDefaults(output, input);

        var envelopeJson = isEnvelope ? SerializeAsEnvelope(output, input) : null;

        return Task.FromResult(new PostProcessorResult
        {
            HasErrors = false,
            Output = output,
            IsEnvelope = isEnvelope,
            Envelope = envelopeJson
        });
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

            if (b.PriceType.Equals("L", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(b.PriceLimit) || b.PriceLimit == "0")
                    errors.Add($"{prefix}priceType 'L' requer priceLimit > 0.");
            }

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
            if (string.IsNullOrWhiteSpace(b.ExpireTime) && !string.IsNullOrWhiteSpace(defaultExpireTime))
                b.ExpireTime = $"{defaultExpireDate} {defaultExpireTime}";

            if (string.IsNullOrWhiteSpace(b.Account) && !string.IsNullOrWhiteSpace(sessionAccount))
                b.Account = sessionAccount;

            if (b.PriceType.Equals("M", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(b.PriceLimit) && b.PriceLimit != "0")
            {
                b.PriceLimit = "";
            }
        }
    }

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

    private static string SerializeAsEnvelope(OutputAtendimento output, string originalInput)
    {
        var originalNode = System.Text.Json.Nodes.JsonNode.Parse(originalInput);
        if (originalNode is not System.Text.Json.Nodes.JsonObject envelope)
            return JsonSerializer.Serialize(output, JsonOpts);

        envelope["response_type"] = output.ResponseType;
        envelope["message"] = output.Message;
        envelope["ui_component"] = output.UiComponent;

        if (output.Boletas is not null && envelope["payload"] is System.Text.Json.Nodes.JsonObject payloadObj)
        {
            payloadObj["boletas"] = System.Text.Json.Nodes.JsonNode.Parse(
                JsonSerializer.Serialize(output.Boletas, JsonOpts));
            if (output.Command is not null)
                payloadObj["command"] = output.Command;
        }

        return envelope.ToJsonString(JsonOpts);
    }

    /// <summary>Re-serializa <see cref="OutputAtendimento"/> validado para o consumidor terminal.</summary>
    public static string SerializeOutput(PostProcessorResult result)
    {
        if (result.IsEnvelope && result.Envelope is not null)
            return result.Envelope;
        if (result.Output is not null)
            return JsonSerializer.Serialize(result.Output, JsonOpts);
        return "";
    }
}
