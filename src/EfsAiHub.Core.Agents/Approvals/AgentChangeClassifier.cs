using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;

namespace EfsAiHub.Core.Agents.Approvals;

/// <summary>
/// Classifica a diferença entre o estado atual de um <see cref="AgentDefinition"/>
/// publicado e o <see cref="AgentDraftPayload"/> de um edit-draft em duas categorias:
/// <list type="bullet">
///   <item><b>Cosmetic</b>: só Description ou Metadata mudaram. Sem impacto comportamental — auto-aprovável.</item>
///   <item><b>Behavioral</b>: qualquer outro campo (Name, Instructions, Tools, Model, Provider,
///         StructuredOutput, Middlewares, SkillRefs, Visibility, AllowedProjectIds,
///         FallbackProvider, Resilience, CostBudget, RegressionTestSetId,
///         RegressionEvaluatorConfigVersionId). Exige revisão humana.</item>
/// </list>
/// O classifier é defensivo — quando incerto, devolve Behavioral.
/// </summary>
public static class AgentChangeClassifier
{
    public static AgentChangeTier Classify(AgentDefinition before, AgentDraftPayload after)
    {
        // AgentDraftPayload é parcial — campos null em `after` significam "não mexer"
        // (mantém valor de `before`). Por isso só comparamos quando o campo veio
        // explicitamente preenchido. Bool? segue mesma semântica: null = inalterado.
        if (after.Name is not null && NotEqualString(before.Name, after.Name)) return AgentChangeTier.Behavioral;
        // Compara texto cru autoral — é o que o owner edita. Composto vive
        // em campo separado e é recriado pelo composer; não faz sentido
        // comparar pra detectar mudança behavioral.
        if (after.AuthorInstructions is not null && NotEqualString(before.AuthorInstructions, after.AuthorInstructions)) return AgentChangeTier.Behavioral;
        if (after.Visibility is not null && NotEqualString(before.Visibility, after.Visibility)) return AgentChangeTier.Behavioral;
        if (after.Enabled.HasValue && before.Enabled != after.Enabled.Value) return AgentChangeTier.Behavioral;
        if (after.RegressionTestSetId is not null && NotEqualString(before.RegressionTestSetId, after.RegressionTestSetId)) return AgentChangeTier.Behavioral;
        if (after.RegressionEvaluatorConfigVersionId is not null && NotEqualString(before.RegressionEvaluatorConfigVersionId, after.RegressionEvaluatorConfigVersionId)) return AgentChangeTier.Behavioral;
        if (after.AllowedProjectIds is not null && NotEqualStringList(before.AllowedProjectIds, after.AllowedProjectIds)) return AgentChangeTier.Behavioral;

        if (after.Model is not null && NotEqualJson(before.Model, after.Model)) return AgentChangeTier.Behavioral;
        if (after.Provider is not null && NotEqualJson(before.Provider, after.Provider)) return AgentChangeTier.Behavioral;
        if (after.FallbackProvider is not null && NotEqualJson(before.FallbackProvider, after.FallbackProvider)) return AgentChangeTier.Behavioral;
        if (after.Tools is not null && NotEqualJson(before.Tools, after.Tools)) return AgentChangeTier.Behavioral;
        if (after.StructuredOutput is not null && NotEqualJson(before.StructuredOutput, after.StructuredOutput)) return AgentChangeTier.Behavioral;
        if (after.Middlewares is not null && NotEqualJson(before.Middlewares, after.Middlewares)) return AgentChangeTier.Behavioral;
        if (after.Resilience is not null && NotEqualJson(before.Resilience, after.Resilience)) return AgentChangeTier.Behavioral;
        if (after.CostBudget is not null && NotEqualJson(before.CostBudget, after.CostBudget)) return AgentChangeTier.Behavioral;
        if (after.SkillRefs is not null && NotEqualJson(before.SkillRefs, after.SkillRefs)) return AgentChangeTier.Behavioral;

        return AgentChangeTier.Cosmetic;
    }

    private static bool NotEqualString(string? a, string? b)
    {
        // Trim defensivo. Trata null/empty como equivalentes — patch de "" pra null
        // é cosmético no schema do payload (não tem semântica diferente).
        var aa = string.IsNullOrEmpty(a) ? string.Empty : a.Trim();
        var bb = string.IsNullOrEmpty(b) ? string.Empty : b.Trim();
        return !string.Equals(aa, bb, System.StringComparison.Ordinal);
    }

    private static bool NotEqualBool(bool a, bool? b)
    {
        return a != (b ?? true);
    }

    private static bool NotEqualStringList(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        var aa = a ?? System.Array.Empty<string>();
        var bb = b ?? System.Array.Empty<string>();
        if (aa.Count != bb.Count) return true;
        for (int i = 0; i < aa.Count; i++)
        {
            if (!string.Equals(aa[i], bb[i], System.StringComparison.Ordinal)) return true;
        }
        return false;
    }

    // Comparação por canonical JSON. Dois objetos com mesma estrutura/valores
    // serializam idêntico (PropertyNamingPolicy=null preserva casing). Se algum
    // serialize falhar (objeto não suportado), trata como diferente — fail-secure.
    private static bool NotEqualJson(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return false;
        if (a is null && b is null) return false;
        if (a is null || b is null)
        {
            // null vs default-construído (ex.: Provider {} ) — empate via re-roundtrip.
            return SerializeOrEmpty(a) != SerializeOrEmpty(b);
        }
        return SerializeOrEmpty(a) != SerializeOrEmpty(b);
    }

    private static string SerializeOrEmpty(object? value)
    {
        if (value is null) return "null";
        try
        {
            return JsonSerializer.Serialize(value, JsonDefaults.Domain);
        }
        catch
        {
            return System.Guid.NewGuid().ToString("N");
        }
    }
}
