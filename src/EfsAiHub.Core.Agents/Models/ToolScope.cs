namespace EfsAiHub.Core.Agents;

/// <summary>
/// Define o escopo de visibilidade de uma tool registrada no FunctionToolRegistry.
/// </summary>
public enum ToolScope
{
    /// <summary>Visível em todos os projetos (registro estático em Program.cs).</summary>
    Global,

    /// <summary>Visível apenas dentro de um projeto específico.</summary>
    Project,

    /// <summary>Registrada dinamicamente para um agente específico.</summary>
    Agent
}
