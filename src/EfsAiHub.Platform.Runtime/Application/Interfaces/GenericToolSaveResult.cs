using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Platform.Runtime.Tools.Generic.Schema;

namespace EfsAiHub.Platform.Runtime.Interfaces;

/// <summary>
/// Resultado de um save (create/update) de Generic Tool. <see cref="Tool"/>
/// é o estado persistido (schemas já canonicalizados). <see cref="Warnings"/>
/// lista transformações lossy aplicadas durante a normalização — UI mostra
/// banner pro autor saber o que mudou (ex.: <c>oneOf</c> colapsado em superset).
/// </summary>
/// <param name="Tool">Tool com schemas canônicos persistidos.</param>
/// <param name="Warnings">Warnings agregados do save de Input + Output schema.</param>
public sealed record GenericToolSaveResult(
    GenericTool Tool,
    IReadOnlyList<NormalizationWarning> Warnings);
