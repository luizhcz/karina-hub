namespace EfsAiHub.Core.Agents.RouterIntents;

/// <summary>
/// Catálogo de intents reservadas do sistema. São seedadas uma por tenant via
/// migration 004 com <see cref="RouterIntent.IsSystem"/>=true e auto-linkadas
/// em todo agente <c>Router</c> no save. Garantem que o classificador sempre
/// tem uma saída segura quando nenhuma intent de negócio bate — sem depender
/// de o admin lembrar de configurar fallback no Switch.
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
    /// Conjunto de nomes reservados — usado pelo <c>RouterIntentService</c>
    /// pra bloquear delete/edit e pra detectar ausência no set do Router.
    /// </summary>
    public static readonly IReadOnlySet<string> ReservedNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { OutOfScopeName };

    public static bool IsReserved(string? name) =>
        !string.IsNullOrEmpty(name) && ReservedNames.Contains(name);
}
