namespace EfsAiHub.Core.Agents.Evaluation;

// Header mutável; versions são append-only e referenciadas pelo ponteiro CurrentVersionId.
// Promoção a Visibility=Global exige permissão admin no controller.
public sealed record EvaluationTestSet(
    string Id,
    string ProjectId,
    string Name,
    string? Description,
    TestSetVisibility Visibility,
    string? CurrentVersionId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? CreatedBy);
