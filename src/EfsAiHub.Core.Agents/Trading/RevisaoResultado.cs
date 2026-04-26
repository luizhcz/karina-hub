using System.ComponentModel;

namespace EfsAiHub.Core.Agents.Trading;

/// <summary>
/// Output tipado de <c>revisao_classificador</c>. Wrappa o texto livre do agente
/// <c>revisor-analise-ativo</c> num envelope com campo discriminador <see cref="Status"/>
/// que alimenta o predicate de Switch (path <c>$.status</c>).
/// </summary>
[Description("Resultado da revisão do agente revisor-analise-ativo classificado para roteamento tipado.")]
public sealed record RevisaoResultado
{
    [Description("Status da revisão: APROVADO ou REPROVADO.")]
    public required string Status { get; init; }

    [Description("Motivo da reprovação. Vazio quando aprovado.")]
    public string? Reasoning { get; init; }

    [Description("Payload do ativo aprovado, presente apenas quando Status=APROVADO.")]
    public Ativo? AprovadoPayload { get; init; }
}
