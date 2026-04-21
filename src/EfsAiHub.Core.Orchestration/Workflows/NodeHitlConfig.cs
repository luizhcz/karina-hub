using EfsAiHub.Core.Orchestration.Enums;

namespace EfsAiHub.Core.Orchestration.Workflows;

/// <summary>
/// Configuração declarativa de HITL no nível do nó (agente ou executor).
/// Quando presente, o engine pausa automaticamente antes ou depois da execução
/// e aguarda resposta humana via IHumanInteractionService.
/// </summary>
public class NodeHitlConfig
{
    /// <summary>"before" = pausa antes da execução do nó, "after" = pausa depois.</summary>
    public required string When { get; init; }

    /// <summary>Tipo de interação: Approval (binário), Input (texto livre), Choice (N opções).</summary>
    public InteractionType InteractionType { get; init; } = InteractionType.Approval;

    /// <summary>Pergunta exibida ao humano no popup de aprovação.</summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// Quando when="after", inclui o output do nó como contexto no popup.
    /// Permite ao humano avaliar o resultado antes de aprovar.
    /// </summary>
    public bool ShowOutput { get; init; } = false;

    /// <summary>Opções para Choice. Null usa default Aprovar/Rejeitar.</summary>
    public IReadOnlyList<string>? Options { get; init; }

    /// <summary>Timeout em segundos para a resposta humana. Default: 300 (5 min).</summary>
    public int TimeoutSeconds { get; init; } = 300;
}
