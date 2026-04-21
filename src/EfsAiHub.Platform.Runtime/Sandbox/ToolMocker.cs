using System.Text.Json;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Platform.Runtime.Sandbox;

/// <summary>
/// Wraps an AIFunction to return a mock response instead of executing the real tool.
/// Used in Sandbox mode to prevent side effects (API calls, DB writes, etc.)
/// while still allowing the LLM to exercise the tool-calling flow.
/// </summary>
public sealed class MockedAIFunction : AIFunction
{
    private readonly AIFunction _inner;

    public MockedAIFunction(AIFunction inner)
    {
        _inner = inner;
    }

    public override string Name => _inner.Name;
    public override string Description => _inner.Description;
    public override JsonElement JsonSchema => _inner.JsonSchema;
    public override JsonSerializerOptions JsonSerializerOptions => _inner.JsonSerializerOptions;

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var result = new
        {
            _mocked = true,
            tool = _inner.Name,
            message = $"[SANDBOX] Tool '{_inner.Name}' was called but not executed. Arguments received successfully.",
            arguments = arguments.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value?.ToString() ?? "null")
        };

        return new ValueTask<object?>(JsonSerializer.Serialize(result));
    }
}

/// <summary>
/// Factory to wrap a list of AITools with mock implementations for Sandbox mode.
/// Tools listed in <paramref name="toolsToMock"/> are replaced; others pass through unchanged.
/// If toolsToMock is null or empty, ALL tools are mocked.
/// </summary>
public static class ToolMocker
{
    public static List<AITool> MockTools(
        IList<AITool> tools,
        IReadOnlySet<string>? toolsToMock = null)
    {
        var result = new List<AITool>(tools.Count);
        foreach (var tool in tools)
        {
            if (tool is AIFunction fn && ShouldMock(fn.Name, toolsToMock))
                result.Add(new MockedAIFunction(fn));
            else
                result.Add(tool);
        }
        return result;
    }

    private static bool ShouldMock(string toolName, IReadOnlySet<string>? toolsToMock)
    {
        // If no specific list, mock all tools
        if (toolsToMock is null || toolsToMock.Count == 0)
            return true;
        return toolsToMock.Contains(toolName);
    }
}
