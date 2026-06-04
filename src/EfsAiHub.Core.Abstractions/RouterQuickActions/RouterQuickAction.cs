namespace EfsAiHub.Core.Abstractions.RouterQuickActions;

/// <summary>
/// Atalho determinístico de classificação pra um agente Router: bypass do LLM
/// quando a mensagem do usuário bate em um <see cref="Pattern"/> pré-cadastrado.
///
/// <para>
/// Sintaxe do <see cref="Pattern"/> (controlada, sem regex aberto):
/// - Texto literal pré-normalizado (lowercase + trim + whitespace colapsado).
/// - Sufixo opcional <c>" *"</c> indica "1+ token livre" no final
///   (ex: <c>"comprar *"</c> bate em "Comprar PETR4", "comprar  vale3", etc).
/// - Sem <c>*</c> = match exato após normalização.
/// </para>
///
/// <para>
/// Match wins por especificidade (mais tokens fixos primeiro): <c>"cotacao do dia"</c>
/// vence sobre <c>"cotacao *"</c> quando o input for exatamente "cotacao do dia".
/// </para>
/// </summary>
public class RouterQuickAction
{
    public required string Id { get; init; }

    /// <summary>FK lógica pra <c>agent_definitions.Id</c> — Router dono desse atalho.</summary>
    public required string RouterId { get; init; }

    /// <summary>Texto normalizado usado no lookup (lowercase + trim + collapsed ws), com '*' opcional no fim.</summary>
    public required string Pattern { get; init; }

    /// <summary>Texto exibido na UI (preserva casing original do admin) — usado como conteúdo enviado ao clicar no botão.</summary>
    public required string DisplayText { get; init; }

    /// <summary>Intent retornada quando o pattern bate — deve existir no pool do Router (agent_router_intents).</summary>
    public required string Intent { get; init; }

    /// <summary>Tooltip opcional pra UI.</summary>
    public string? Description { get; set; }

    public required string ProjectId { get; init; }
    public required string TenantId { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
