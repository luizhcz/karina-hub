namespace EfsAiHub.Core.Agents;

public static class AgentTypeExtensions
{
    /// <summary>
    /// Indica se mudanças nesse tipo de agente exigem fluxo de aprovação
    /// (draft → submit → review humano). Tipos que retornam <c>false</c>
    /// publicam direto via auto-approve no submit (Tier=TypeBypass).
    ///
    /// Regra de produto: Router publica direto — sua edição é
    /// responsabilidade do owner e o audit registra o motivo informado.
    /// Demais tipos seguem o fluxo Cosmetic/Behavioral default.
    ///
    /// Para promover outro tipo a bypass, basta adicioná-lo ao switch.
    /// Mantém-se a extensão pura (sem state) pra evitar dependências
    /// ocultas e simplificar testes.
    /// </summary>
    public static bool RequiresApproval(this AgentType type) => type switch
    {
        AgentType.Router => false,
        _ => true,
    };
}
