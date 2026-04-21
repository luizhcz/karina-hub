using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Platform.Runtime.Factories;

/// <summary>
/// Decorator que instrumenta chamadas de AIFunction para rastreamento de tool calls.
/// Envia cada invocação para um Channel de persistência em background.
/// </summary>
public sealed class TrackedAIFunction : AIFunction
{
    private readonly AIFunction _inner;
    private readonly string _agentId;
    private readonly ChannelWriter<ToolInvocation> _invocationWriter;
    private readonly ILogger<TrackedAIFunction> _logger;

    public TrackedAIFunction(
        AIFunction inner,
        string agentId,
        ChannelWriter<ToolInvocation> invocationWriter,
        ILogger<TrackedAIFunction> logger)
    {
        _inner = inner;
        _agentId = agentId;
        _invocationWriter = invocationWriter;
        _logger = logger;
    }

    public override string Name => _inner.Name;
    public override string Description => _inner.Description;
    public override JsonElement JsonSchema => _inner.JsonSchema;

    // Parâmetros sensíveis cujo valor deve bater com o userId da sessão em modo ClientLocked.
    private static readonly string[] SensitiveAccountParams = { "conta", "account" };

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySources.ToolCallSource.StartActivity("ToolCall");
        activity?.SetTag("tool.name", Name);
        activity?.SetTag("agent.id", _agentId);

        var ctx = DelegateExecutor.Current.Value;
        ApplyAccountGuard(arguments, ctx);

        var executionId = ctx?.ExecutionId;
        var sw = Stopwatch.StartNew();
        string? argsJson = null;

        try
        {
            argsJson = JsonSerializer.Serialize(arguments.ToDictionary(kv => kv.Key, kv => kv.Value));
        }
        catch (Exception ex2) { argsJson = $"[serialize-error: {ex2.Message}]"; }

        try
        {
            var result = await _inner.InvokeAsync(arguments, cancellationToken);
            sw.Stop();

            activity?.SetTag("duration_ms", sw.Elapsed.TotalMilliseconds);
            activity?.SetTag("success", true);

            PersistInvocation(executionId, argsJson, result?.ToString(), sw.Elapsed.TotalMilliseconds, true, null);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetTag("duration_ms", sw.Elapsed.TotalMilliseconds);
            activity?.SetTag("success", false);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            var friendlyError = $"[Tool Error] Erro ao chamar '{Name}': {ex.Message}. Verifique os parâmetros obrigatórios e tente novamente.";
            _logger.LogWarning(ex, "Tool call '{ToolName}' falhou para agent '{AgentId}'. Args: {Args}. Retornando erro amigável ao LLM.", Name, _agentId, argsJson ?? "null");

            PersistInvocation(executionId, argsJson, friendlyError, sw.Elapsed.TotalMilliseconds, false, ex.Message);
            return friendlyError;
        }
    }

    private void ApplyAccountGuard(AIFunctionArguments arguments, EfsAiHub.Core.Agents.Execution.ExecutionContext? ctx)
    {
        if (ctx is null) return;
        if (ctx.GuardMode == EfsAiHub.Core.Agents.Execution.AccountGuardMode.None) return;
        if (string.IsNullOrEmpty(ctx.UserId)) return;

        foreach (var paramName in SensitiveAccountParams)
        {
            if (!arguments.TryGetValue(paramName, out var raw) || raw is null) continue;
            var asString = raw.ToString();
            if (string.Equals(asString, ctx.UserId, StringComparison.Ordinal)) continue;

            switch (ctx.GuardMode)
            {
                case EfsAiHub.Core.Agents.Execution.AccountGuardMode.ClientLocked:
                    arguments[paramName] = ctx.UserId;
                    MetricsRegistry.ToolAccountOverrides.Add(1,
                        new KeyValuePair<string, object?>("tool.name", Name),
                        new KeyValuePair<string, object?>("param", paramName));
                    _logger.LogWarning(
                        "[AccountGuard] Tool '{Tool}' agente '{Agent}' — parâmetro '{Param}' reescrito de '{Orig}' para '{Expected}' (ClientLocked).",
                        Name, _agentId, paramName, asString, ctx.UserId);
                    break;
                case EfsAiHub.Core.Agents.Execution.AccountGuardMode.AssessorLogOnly:
                    _logger.LogInformation(
                        "[AccountGuard] Tool '{Tool}' agente '{Agent}' — '{Param}'='{Value}' difere de userId da sessão '{Expected}' (AssessorLogOnly).",
                        Name, _agentId, paramName, asString, ctx.UserId);
                    break;
            }
        }
    }

    private void PersistInvocation(
        string? executionId, string? args, string? result,
        double durationMs, bool success, string? errorMessage)
    {
        if (executionId is null) return;

        _invocationWriter.TryWrite(new ToolInvocation
        {
            ExecutionId = executionId,
            AgentId = _agentId,
            ToolName = Name,
            Arguments = args,
            Result = result,
            DurationMs = durationMs,
            Success = success,
            ErrorMessage = errorMessage
        });
    }
}
