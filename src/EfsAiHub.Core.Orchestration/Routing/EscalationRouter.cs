using System.Text.RegularExpressions;
using EfsAiHub.Core.Agents.Signals;
using EfsAiHub.Core.Orchestration.Workflows;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Core.Orchestration.Routing;

/// <summary>
/// Fase 7 — Implementação padrão do router. Avalia regras em ordem de prioridade
/// (desc) e retorna o primeiro match. Formatos de <c>Match</c>:
///   category:&lt;name&gt; | tag:&lt;name&gt; | regex:&lt;pattern&gt; | any
/// </summary>
public sealed class EscalationRouter : IEscalationRouter
{
    private readonly ILogger<EscalationRouter> _logger;
    private readonly Action<string?, bool>? _metricSink;

    public EscalationRouter(ILogger<EscalationRouter> logger, Action<string?, bool>? metricSink = null)
    {
        _logger = logger;
        _metricSink = metricSink;
    }

    public string? Route(AgentEscalationSignal signal, IReadOnlyList<RoutingRule> rules)
    {
        string? target = null;
        if (rules is not null && rules.Count > 0)
        {
            foreach (var rule in rules.OrderByDescending(r => r.Priority))
            {
                if (Matches(rule.Match, signal))
                {
                    target = rule.TargetNodeId;
                    _logger.LogInformation(
                        "[EscalationRouter] signal category='{Category}' reason='{Reason}' → node '{Target}' (rule '{Match}', prio={Prio})",
                        signal.Category, signal.Reason, target, rule.Match, rule.Priority);
                    break;
                }
            }
        }

        if (target is null)
            _logger.LogWarning(
                "[EscalationRouter] nenhum match para signal category='{Category}' (reason='{Reason}') — sem destino.",
                signal.Category, signal.Reason);

        _metricSink?.Invoke(signal.Category, target is not null);

        return target;
    }

    private static bool Matches(string match, AgentEscalationSignal signal)
    {
        if (string.IsNullOrWhiteSpace(match)) return false;
        if (match.Equals("any", StringComparison.OrdinalIgnoreCase)) return true;

        var sepIdx = match.IndexOf(':');
        if (sepIdx <= 0) return false;

        var kind = match[..sepIdx].Trim();
        var value = match[(sepIdx + 1)..].Trim();

        return kind.ToLowerInvariant() switch
        {
            "category" => string.Equals(signal.Category, value, StringComparison.OrdinalIgnoreCase),
            "tag" => signal.SuggestedTargetTags.Any(t => string.Equals(t, value, StringComparison.OrdinalIgnoreCase)),
            "regex" => SafeRegex(value, signal.Reason),
            _ => false
        };
    }

    private static bool SafeRegex(string pattern, string input)
    {
        try { return Regex.IsMatch(input ?? string.Empty, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)); }
        catch { return false; }
    }
}
