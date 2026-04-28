using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;

namespace EfsAiHub.Core.Agents.Evaluation;

/// <summary>
/// Snapshot imutável de configuração de evaluators (append-only por revision).
/// EvaluationRun pina EvaluatorConfigVersionId (não o header) — mudar bindings
/// cria nova revision, jamais reescreve histórico.
/// </summary>
public sealed record EvaluatorConfigVersion(
    string EvaluatorConfigVersionId,
    string EvaluatorConfigId,
    int Revision,
    EvaluatorConfigVersionStatus Status,
    string ContentHash,
    IReadOnlyList<EvaluatorBinding> Bindings,
    SplitterStrategy Splitter,
    int NumRepetitions,
    DateTime CreatedAt,
    string? CreatedBy,
    string? ChangeReason)
{
    public const int DefaultNumRepetitions = 3;
    public const SplitterStrategy DefaultSplitter = SplitterStrategy.LastTurn;

    // Bindings canonicalizados por (Kind, Name, BindingIndex) antes do hash.
    public static EvaluatorConfigVersion Build(
        string evaluatorConfigId,
        int revision,
        IReadOnlyList<EvaluatorBinding> bindings,
        SplitterStrategy splitter = DefaultSplitter,
        int numRepetitions = DefaultNumRepetitions,
        string? createdBy = null,
        string? changeReason = null)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            bindings = bindings
                .OrderBy(b => b.Kind)
                .ThenBy(b => b.Name, StringComparer.Ordinal)
                .ThenBy(b => b.BindingIndex)
                .Select(b => new
                {
                    kind = b.Kind.ToString(),
                    name = b.Name,
                    @params = b.Params?.RootElement.GetRawText(),
                    enabled = b.Enabled,
                    weight = b.Weight,
                    bindingIndex = b.BindingIndex
                }),
            splitter = splitter.ToString(),
            numRepetitions
        }, JsonDefaults.Domain);

        return new EvaluatorConfigVersion(
            EvaluatorConfigVersionId: Guid.NewGuid().ToString("N"),
            EvaluatorConfigId: evaluatorConfigId,
            Revision: revision,
            Status: EvaluatorConfigVersionStatus.Draft,
            ContentHash: ComputeSha256(canonical),
            Bindings: bindings,
            Splitter: splitter,
            NumRepetitions: numRepetitions,
            CreatedAt: DateTime.UtcNow,
            CreatedBy: createdBy,
            ChangeReason: changeReason);
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
