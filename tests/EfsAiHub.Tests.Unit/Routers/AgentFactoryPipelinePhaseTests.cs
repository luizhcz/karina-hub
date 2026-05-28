using EfsAiHub.Platform.Runtime.Factories;

namespace EfsAiHub.Tests.Unit.Routers;

/// <summary>
/// Cobre P0-#1 do plano de ambiguidade: o tipo
/// <c>RouterDecisionTelemetry</c> é classificado como middleware de
/// FASE PRE-MEMORY pelo <see cref="AgentFactory"/>, o que faz ele ser
/// wrappado MAIS INTERNO que o <c>OperationalMemoryChatClient</c>. Sem isso,
/// o rewrite de needs_clarification inválido aconteceria DEPOIS do OpMem
/// persistir o estado ruim no banco — quebrando o loop guard entre turnos.
///
/// <para>
/// Testar o wrap real exige construir uma <see cref="AgentFactory"/>
/// (≥20 dependências). O método <see cref="AgentFactory.IsPreMemoryPhase"/>
/// é pura função do nome do middleware, exposto <c>internal</c> pra que
/// regressão no set seja capturada sem montar o factory completo.
/// </para>
/// </summary>
[Trait("Category", "Unit")]
public class AgentFactoryPipelinePhaseTests
{
    [Fact]
    public void IsPreMemoryPhase_RouterDecisionTelemetry_RetornaTrue()
    {
        AgentFactory.IsPreMemoryPhase("RouterDecisionTelemetry").Should().BeTrue();
    }

    [Fact]
    public void IsPreMemoryPhase_CaseInsensitive()
    {
        // Migration 013 e Apply normalizam case via OrdinalIgnoreCase.
        AgentFactory.IsPreMemoryPhase("routerdecisiontelemetry").Should().BeTrue();
        AgentFactory.IsPreMemoryPhase("ROUTERDECISIONTELEMETRY").Should().BeTrue();
    }

    [Theory]
    [InlineData("StructuredOutputState")]
    [InlineData("AccountGuard")]
    [InlineData("SecurityGuardrails")]
    [InlineData("OperationalMemory")]
    public void IsPreMemoryPhase_OutrosMiddlewares_RetornaFalse(string type)
    {
        // Middlewares legados continuam wrappados APÓS OpMem (post-memory):
        // veem output já strippado, STATE_DELTA do StructuredOutputState
        // carrega o payload final.
        AgentFactory.IsPreMemoryPhase(type).Should().BeFalse();
    }

    [Fact]
    public void IsPreMemoryPhase_TypeDesconhecido_RetornaFalse()
    {
        AgentFactory.IsPreMemoryPhase("typo-do-admin").Should().BeFalse();
    }
}
