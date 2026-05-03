using EfsAiHub.Core.Agents;

namespace EfsAiHub.Platform.Runtime.Tools.PredefinedModels;

/// <summary>
/// Quando um agent tem <c>Model.PredefinedModelId</c> setado, resolve o preset
/// no catálogo global e retorna nova <see cref="AgentDefinition"/> com
/// <c>Provider</c>+<c>Model</c> hidratados pelos valores do preset. Mudança no
/// preset propaga em runtime — agents pegam o estado atual sem rebuild.
/// </summary>
public interface IPredefinedModelBinder
{
    Task<AgentDefinition> BindAsync(AgentDefinition definition, CancellationToken ct = default);
}
