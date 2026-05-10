namespace EfsAiHub.Core.Agents.RouterIntents;

/// <summary>
/// Item do pool global de intenções. Escopo é tenant — qualquer projeto do
/// mesmo tenant enxerga e referencia. <c>ProjectId</c> aqui é a "categoria"
/// (referência a um projeto do tenant que classifica a intent), não filtro
/// de visibilidade.
///
/// Routers consomem via <c>aihub.agent_router_intents</c>. Edits propagam
/// pros Routers que referenciam (lookup runtime); creates não propagam
/// (user precisa editar o Router pra incluir a intent nova no set dele).
/// </summary>
public sealed class RouterIntent
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }

    /// <summary>
    /// Projeto-categoria. Persiste o Id; UI exibe nome via lookup em
    /// <c>aihub.projects</c>. FK garante existência no tenant.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>Nome técnico canônico (snake_case decidido pelo analyzer). Vai pro <c>intent.enum</c> do schema do Router em runtime.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Texto humano editável (PT-BR, com espaços/acentos). Vem do que o user
    /// digitou no campo "Nome da intenção". Independe de <c>Name</c> — pode
    /// ser livre. Quando ausente, UI cai pro <c>Name</c>.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>Briefing do "quando essa intent se aplica". Alimenta o prompt skeleton.</summary>
    public required string Description { get; set; }

    /// <summary>
    /// Lista opcional de inputs típicos. Anexada no prompt como exemplos pra
    /// ajudar o modelo a ancorar a categoria. Vazio = sem exemplos.
    /// </summary>
    public IReadOnlyList<string> Examples { get; set; } = Array.Empty<string>();

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class RouterIntentNameConflictException : Exception
{
    public RouterIntentNameConflictException(string name)
        : base($"Já existe uma intent com Name='{name}' nesse tenant.")
    { }
}

public sealed class RouterIntentInUseException : Exception
{
    public IReadOnlyList<string> AgentIds { get; }

    public RouterIntentInUseException(string intentId, IReadOnlyList<string> agentIds)
        : base($"Intent '{intentId}' está em uso por {agentIds.Count} Router(s); remova das referências antes de deletar.")
    {
        AgentIds = agentIds;
    }
}
