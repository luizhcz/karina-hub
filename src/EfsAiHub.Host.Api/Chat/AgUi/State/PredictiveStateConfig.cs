namespace EfsAiHub.Host.Api.Chat.AgUi.State;

/// <summary>
/// Configuração de estado preditivo: define como argumentos de tool calls
/// mapeiam para campos de estado, permitindo emitir STATE_DELTA durante
/// o streaming de argumentos antes da tool call completar.
/// </summary>
public sealed record PredictiveStateConfig
{
    /// <summary>
    /// Mapeamento de toolName para o caminho do campo de estado que deve ser atualizado.
    /// Exemplo: { "get_portfolio": "/portfolio/selected" }
    /// </summary>
    public required Dictionary<string, string> ToolNameToStateField { get; init; }
}
