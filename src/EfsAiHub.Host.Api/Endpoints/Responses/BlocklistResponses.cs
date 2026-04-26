using EfsAiHub.Core.Abstractions.Blocklist;

namespace EfsAiHub.Host.Api.Endpoints.Responses;

/// <summary>Response do GET /api/admin/blocklist/catalog (snapshot do catálogo curado).</summary>
public sealed record BlocklistCatalogResponse(
    int Version,
    IReadOnlyList<BlocklistPatternGroup> Groups,
    IReadOnlyList<BlocklistPattern> Patterns)
{
    public static BlocklistCatalogResponse From(BlocklistCatalogSnapshot s)
        => new(s.Version, s.Groups, s.Patterns);
}

/// <summary>Response do GET /api/projects/{id}/blocklist (config efetiva do projeto).</summary>
public sealed record ProjectBlocklistResponse(
    string ProjectId,
    BlocklistSettings Settings);

/// <summary>
/// Row de violação no GET /api/projects/{id}/blocklist/violations.
/// Conteúdo cru NUNCA é retornado — apenas hash + contexto ofuscado vindos do audit.
/// </summary>
public sealed record BlocklistViolationRow(
    long AuditId,
    DateTime DetectedAt,
    string? UserId,
    string AgentId,
    string? Phase,
    string? Category,
    string? PatternId,
    string? Action,
    string? ContentHash,
    string? ContextObfuscated);
