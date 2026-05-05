using EfsAiHub.Core.Agents.Evaluation;

namespace EfsAiHub.Platform.Runtime.Evaluation;

/// <summary>
/// Presets de avaliação automática usados pelo <see cref="EvaluationAutoDeployService"/>.
/// Hardcoded por design: mudança de preset é mudança de produto, versionada via Git/PR.
/// Quando crescer pra "presets por projeto" ou customizáveis por compliance, virar tabela.
/// </summary>
public enum AutoDeployPreset
{
    Basic,
    Medium,
    Advanced,
}

public sealed record AutoDeployPresetSpec(
    AutoDeployPreset Preset,
    int CaseCount,
    IReadOnlyList<EvaluatorBinding> Bindings,
    SplitterStrategy Splitter,
    int NumRepetitions,
    decimal EstimatedCostUsd,
    int EstimatedDurationSeconds);

public static class PresetCatalog
{
    private static readonly AutoDeployPresetSpec _basic = new(
        Preset: AutoDeployPreset.Basic,
        CaseCount: 5,
        Bindings: new[]
        {
            new EvaluatorBinding(EvaluatorKind.Local, "ContainsExpected", null, Enabled: true, Weight: 1.0, BindingIndex: 0),
            new EvaluatorBinding(EvaluatorKind.Local, "ToolCalledCheck",  null, Enabled: true, Weight: 1.0, BindingIndex: 0),
        },
        Splitter: SplitterStrategy.LastTurn,
        NumRepetitions: 1,
        EstimatedCostUsd: 0m,
        EstimatedDurationSeconds: 30);

    private static readonly AutoDeployPresetSpec _medium = new(
        Preset: AutoDeployPreset.Medium,
        CaseCount: 5,
        Bindings: new[]
        {
            new EvaluatorBinding(EvaluatorKind.Meai, "Relevance",        null, Enabled: true, Weight: 1.0, BindingIndex: 0),
            new EvaluatorBinding(EvaluatorKind.Meai, "Coherence",        null, Enabled: true, Weight: 1.0, BindingIndex: 0),
            new EvaluatorBinding(EvaluatorKind.Meai, "ToolCallAccuracy", null, Enabled: true, Weight: 1.0, BindingIndex: 0),
            new EvaluatorBinding(EvaluatorKind.Meai, "TaskAdherence",    null, Enabled: true, Weight: 1.0, BindingIndex: 0),
        },
        Splitter: SplitterStrategy.LastTurn,
        NumRepetitions: 1,
        EstimatedCostUsd: 0.10m,
        EstimatedDurationSeconds: 60);

    private static readonly AutoDeployPresetSpec _advanced = new(
        Preset: AutoDeployPreset.Advanced,
        CaseCount: 15,
        Bindings: new[]
        {
            new EvaluatorBinding(EvaluatorKind.Meai, "Relevance",        null, Enabled: true, Weight: 1.0, BindingIndex: 0),
            new EvaluatorBinding(EvaluatorKind.Meai, "Coherence",        null, Enabled: true, Weight: 1.0, BindingIndex: 0),
            new EvaluatorBinding(EvaluatorKind.Meai, "ToolCallAccuracy", null, Enabled: true, Weight: 1.0, BindingIndex: 0),
            new EvaluatorBinding(EvaluatorKind.Meai, "TaskAdherence",    null, Enabled: true, Weight: 1.0, BindingIndex: 0),
            new EvaluatorBinding(EvaluatorKind.Meai, "Fluency",          null, Enabled: true, Weight: 1.0, BindingIndex: 0),
            new EvaluatorBinding(EvaluatorKind.Meai, "Completeness",     null, Enabled: true, Weight: 1.0, BindingIndex: 0),
            new EvaluatorBinding(EvaluatorKind.Meai, "Equivalence",      null, Enabled: true, Weight: 1.0, BindingIndex: 0),
            new EvaluatorBinding(EvaluatorKind.Meai, "IntentResolution", null, Enabled: true, Weight: 1.0, BindingIndex: 0),
        },
        Splitter: SplitterStrategy.LastTurn,
        NumRepetitions: 1,
        EstimatedCostUsd: 0.50m,
        EstimatedDurationSeconds: 180);

    public static AutoDeployPresetSpec Get(AutoDeployPreset preset) => preset switch
    {
        AutoDeployPreset.Basic    => _basic,
        AutoDeployPreset.Medium   => _medium,
        AutoDeployPreset.Advanced => _advanced,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Preset desconhecido."),
    };

    public static bool TryParse(string value, out AutoDeployPreset preset)
    {
        if (Enum.TryParse(value, ignoreCase: true, out preset)) return true;
        preset = AutoDeployPreset.Basic;
        return false;
    }
}
