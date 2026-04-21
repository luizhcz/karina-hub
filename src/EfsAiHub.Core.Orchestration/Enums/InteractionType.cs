namespace EfsAiHub.Core.Orchestration.Enums;

/// <summary>
/// Tipo de interação Human-in-the-Loop.
/// Define a semântica do contrato de request/response entre o agente e o humano.
/// </summary>
public enum InteractionType
{
    /// <summary>Escolha binária (Confirmar/Cancelar, Aprovar/Rejeitar).</summary>
    Approval,

    /// <summary>Entrada de texto livre (pergunta aberta ao humano).</summary>
    Input,

    /// <summary>Escolha entre N opções pré-definidas.</summary>
    Choice
}
