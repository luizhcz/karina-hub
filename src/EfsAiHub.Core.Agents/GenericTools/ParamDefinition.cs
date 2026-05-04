namespace EfsAiHub.Core.Agents.GenericTools;

/// <summary>
/// Descritor de um path/query param: tipo no JSON Schema (string|number|integer|boolean),
/// descrição lida pelo LLM e flag de obrigatoriedade.
/// </summary>
public sealed record ParamDefinition(string Type, string Description, bool Required);
