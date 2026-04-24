namespace EfsAiHub.Core.Abstractions.Identity;

/// <summary>
/// Contexto de tenant resolvido por requisição/execução. Populado pelo middleware
/// no Host.Api e injetado em escopo para uso por query filters, throttlers e guards.
///
/// Isolamento de dados é feito por <b>ProjectId</b> (ver <c>IProjectContextAccessor</c>).
/// <c>TenantId</c> fica como dimensão analítica em <c>admin_audit_log</c> + billing
/// agregado cross-project, não é usado em <c>HasQueryFilter</c> (ver ADR 003).
/// </summary>
public sealed class TenantContext
{
    public string TenantId { get; }
    public TenantIsolationLevel IsolationLevel { get; }

    public TenantContext(string tenantId, TenantIsolationLevel isolationLevel = TenantIsolationLevel.Shared)
    {
        TenantId = tenantId;
        IsolationLevel = isolationLevel;
    }

    public static TenantContext Default { get; } = new("default");
}

public enum TenantIsolationLevel
{
    Shared,
    Dedicated
}
