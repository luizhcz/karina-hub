namespace EfsAiHub.Core.Abstractions.Blocklist;

/// <summary>
/// Catálogo curado vindo de aihub.blocklist_pattern_groups + aihub.blocklist_patterns.
/// Read-only do ponto de vista do app — atualizações via db/seeds.sql + apply.sh.
/// Cache (L1+L2) e invalidação NOTIFY ficam no consumer (BlocklistCache em Platform.Runtime).
/// </summary>
public interface IBlocklistCatalogRepository
{
    Task<BlocklistCatalogSnapshot> LoadAllAsync(CancellationToken ct = default);
}
