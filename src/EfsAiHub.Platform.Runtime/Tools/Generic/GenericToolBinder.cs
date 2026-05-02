using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Interfaces;

namespace EfsAiHub.Platform.Runtime.Tools.Generic;

public sealed class GenericToolBinder : IGenericToolBinder
{
    public const string ToolType = "generic_http";

    private readonly IGenericToolRepository _repo;
    private readonly IGenericToolExecutor _executor;
    private readonly IFunctionToolRegistry _registry;
    private readonly ILogger<GenericToolBinder> _logger;

    public GenericToolBinder(
        IGenericToolRepository repo,
        IGenericToolExecutor executor,
        IFunctionToolRegistry registry,
        ILogger<GenericToolBinder> logger)
    {
        _repo = repo;
        _executor = executor;
        _registry = registry;
        _logger = logger;
    }

    public async Task BindAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        if (definition.Tools.Count == 0) return;

        var generic = definition.Tools
            .Where(t => string.Equals(t.Type, ToolType, StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrEmpty(t.GenericToolId))
            .ToList();

        if (generic.Count == 0) return;

        foreach (var toolRef in generic)
        {
            var toolId = toolRef.GenericToolId!;
            var tool = await _repo.GetByIdAsync(toolId, ct);
            if (tool is null)
            {
                _logger.LogWarning(
                    "[GenericToolBinder] Agent '{AgentId}' referencia GenericTool '{ToolId}' que não existe no projeto '{ProjectId}'. Tool ignorado.",
                    definition.Id, toolId, definition.ProjectId);
                continue;
            }

            var fn = new DynamicGenericAIFunction(tool, _executor);
            _registry.Register(toolId, fn, definition.ProjectId);
        }
    }
}
