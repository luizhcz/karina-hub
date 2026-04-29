using System.Text.Json;
using System.Text.Json.Serialization;
using EfsAiHub.Core.Agents.Trading;

namespace EfsAiHub.Platform.Runtime.Executors;

/// <summary>
/// Executor terminal alcançado quando o router de atendimento decide
/// <c>target_agent = "texto"</c> (saudação, fora de escopo, dúvida genérica).
/// Reembala a <c>message</c> do router num envelope <see cref="OutputAtendimento"/>
/// pro consumidor de UI receber a forma esperada.
/// </summary>
public static class RouterFallback
{
    public static Task<OutputAtendimento> WrapAsync(RouterOutput input, CancellationToken ct)
    {
        var msg = string.IsNullOrWhiteSpace(input.Message)
            ? "Não consegui entender o pedido. Pode reformular?"
            : input.Message;

        return Task.FromResult(new OutputAtendimento
        {
            ResponseType = "texto",
            Message = msg,
            UiComponent = "text_card"
        });
    }
}

public sealed record RouterOutput
{
    [JsonPropertyName("target_agent")]
    public string? TargetAgent { get; init; }

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
