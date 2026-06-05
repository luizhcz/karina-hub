namespace EfsAiHub.Core.Agents.RouterIntents;

/// <summary>
/// Catálogo de intents reservadas do sistema. São seedadas uma por tenant
/// (via migrations 004 e 012) com <see cref="RouterIntent.IsSystem"/>=true e
/// auto-linkadas em todo agente <c>Router</c> no save. Garantem que o
/// classificador sempre tem uma saída segura — fora-do-escopo OU pedido de
/// desambiguação — sem depender de o admin lembrar de configurar essas saídas
/// no Switch.
/// </summary>
public static class SystemIntents
{
    /// <summary>
    /// Intent canônica de fora-de-escopo. Resolvida em runtime via lookup por
    /// <see cref="RouterIntent.Name"/> + <see cref="RouterIntent.TenantId"/> —
    /// o Id real é gerado pela migration como <c>sys-oos-{tenantId}</c>.
    /// </summary>
    public const string OutOfScopeName = "out_of_scope";

    /// <summary>
    /// Intent canônica de ambiguidade. Usada quando a mensagem é
    /// semanticamente válida pro produto mas casa com ≥2 intents de negócio
    /// com confidence similar. O Router emite <c>candidate_intents</c> no
    /// output; o nó downstream (agente Clarifier) gera a pergunta de
    /// desambiguação. Id seedado como <c>sys-nc-{tenantId}</c>.
    /// </summary>
    public const string NeedsClarificationName = "needs_clarification";

    /// <summary>
    /// Conjunto de nomes reservados — usado pelo <c>RouterIntentService</c>
    /// pra bloquear delete/edit e pelo <c>AgentService</c> pra auto-linkar
    /// no set do Router.
    /// </summary>
    public static readonly IReadOnlySet<string> ReservedNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            OutOfScopeName,
            NeedsClarificationName,
        };

    public static bool IsReserved(string? name) =>
        !string.IsNullOrEmpty(name) && ReservedNames.Contains(name);
}
