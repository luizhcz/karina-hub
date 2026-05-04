namespace EfsAiHub.Platform.Runtime.Tools.Generic;

/// <summary>
/// Resolve as Generic Tools referenciadas por um <c>AgentDefinition</c> e as
/// registra como <see cref="DynamicGenericAIFunction"/> project-scoped no
/// <c>IFunctionToolRegistry</c>. Chamado pelo <c>AgentFactory</c> antes de
/// montar as <c>ChatOptions</c>.
/// </summary>
public interface IGenericToolBinder
{
    Task BindAsync(EfsAiHub.Core.Agents.AgentDefinition definition, CancellationToken ct = default);
}
