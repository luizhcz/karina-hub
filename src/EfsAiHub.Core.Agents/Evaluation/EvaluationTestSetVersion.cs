using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;

namespace EfsAiHub.Core.Agents.Evaluation;

/// <summary>
/// Snapshot imutável de uma versão de TestSet. Idempotência por ContentHash
/// (sha256 canônico dos cases ordenados por Index). Race em publish concorrente
/// é no-op via unique index parcial (TestSetId, ContentHash) WHERE Status != 'Deprecated'.
/// </summary>
public sealed record EvaluationTestSetVersion(
    string TestSetVersionId,
    string TestSetId,
    int Revision,
    TestSetVersionStatus Status,
    string ContentHash,
    DateTime CreatedAt,
    string? CreatedBy,
    string? ChangeReason)
{
    public static EvaluationTestSetVersion Build(
        string testSetId,
        int revision,
        IReadOnlyList<EvaluationTestCase> cases,
        string? createdBy = null,
        string? changeReason = null)
    {
        var canonical = JsonSerializer.Serialize(
            cases.OrderBy(c => c.Index).Select(c => new
            {
                index = c.Index,
                input = c.Input,
                expectedOutput = c.ExpectedOutput,
                expectedToolCalls = c.ExpectedToolCalls,
                tags = c.Tags.OrderBy(t => t, StringComparer.Ordinal).ToArray(),
                weight = c.Weight
            }),
            JsonDefaults.Domain);

        return new EvaluationTestSetVersion(
            TestSetVersionId: Guid.NewGuid().ToString("N"),
            TestSetId: testSetId,
            Revision: revision,
            Status: TestSetVersionStatus.Draft,
            ContentHash: ComputeSha256(canonical),
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
