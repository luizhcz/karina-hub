namespace EfsAiHub.Core.Abstractions.Persistence;

/// <summary>
/// Marca uma entidade como isolada por tenant. Usado pelo DbContext em Infra.Persistence
/// para aplicar query filter global (Fase 9).
/// </summary>
public interface ITenantScoped
{
    string TenantId { get; }
}
