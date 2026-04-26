using EfsAiHub.Core.Abstractions.Blocklist;

namespace EfsAiHub.Host.Api.Endpoints.Requests;

/// <summary>
/// Body do PUT /api/projects/{id}/blocklist. Substitui inteiramente
/// <c>ProjectSettings.Blocklist</c> — caller envia o estado completo desejado.
/// </summary>
public sealed record UpdateBlocklistRequest(
    bool Enabled,
    bool ScanInput,
    bool ScanOutput,
    string Replacement,
    bool AuditBlocks,
    Dictionary<string, BlocklistGroupOverrideInput>? Groups,
    List<BlocklistCustomPatternInput>? CustomPatterns)
{
    public BlocklistSettings ToDomain()
    {
        IReadOnlyDictionary<string, BlocklistGroupOverride>? groups = null;
        if (Groups is { Count: > 0 })
        {
            groups = Groups.ToDictionary(
                kv => kv.Key,
                kv => new BlocklistGroupOverride(
                    Enabled: kv.Value.Enabled,
                    ActionOverride: kv.Value.ActionOverride,
                    DisabledPatterns: kv.Value.DisabledPatterns));
        }

        IReadOnlyList<BlocklistCustomPattern>? customs = null;
        if (CustomPatterns is { Count: > 0 })
        {
            customs = CustomPatterns.Select(c => new BlocklistCustomPattern(
                Id: c.Id,
                Type: c.Type,
                Pattern: c.Pattern,
                Action: c.Action,
                WholeWord: c.WholeWord,
                CaseSensitive: c.CaseSensitive)).ToList();
        }

        return new BlocklistSettings(
            Enabled: Enabled,
            ScanInput: ScanInput,
            ScanOutput: ScanOutput,
            Replacement: Replacement,
            AuditBlocks: AuditBlocks,
            Groups: groups,
            CustomPatterns: customs);
    }
}

public sealed record BlocklistGroupOverrideInput(
    bool Enabled,
    BlocklistAction? ActionOverride,
    List<string>? DisabledPatterns);

public sealed record BlocklistCustomPatternInput(
    string Id,
    BlocklistPatternType Type,
    string Pattern,
    BlocklistAction Action,
    bool WholeWord,
    bool CaseSensitive);
