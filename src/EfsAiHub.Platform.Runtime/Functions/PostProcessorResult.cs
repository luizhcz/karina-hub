using EfsAiHub.Core.Agents.Trading;

namespace EfsAiHub.Platform.Runtime.Functions;

/// <summary>
/// Output tipado do <c>service_post_processor</c>. Expõe <see cref="HasErrors"/> como
/// campo discriminador estruturado — predicate de Switch usa <c>$.hasErrors</c> em vez
/// de substring "errors" sobre o JSON serializado.
/// </summary>
public sealed record PostProcessorResult
{
    public bool HasErrors { get; init; }
    public List<string> Errors { get; init; } = [];
    public string? OriginalOutput { get; init; }
    public OutputAtendimento? Output { get; init; }
    public string? Envelope { get; init; }
    public bool IsEnvelope { get; init; }
}
