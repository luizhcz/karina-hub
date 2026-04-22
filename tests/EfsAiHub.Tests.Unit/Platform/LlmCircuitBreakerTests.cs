using EfsAiHub.Platform.Runtime.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.Platform;

[Trait("Category", "Unit")]
public class LlmCircuitBreakerTests
{
    private static LlmCircuitBreaker Build(CircuitBreakerOptions options)
        => new(Options.Create(options), NullLogger<LlmCircuitBreaker>.Instance);

    [Fact]
    public void RecordFailure_SingleReplica_AbrePorDefault()
    {
        var sut = Build(new CircuitBreakerOptions
        {
            FailureThreshold = 5,
            EffectiveReplicaCount = 1
        });

        for (var i = 0; i < 4; i++) sut.RecordFailure("openai");
        sut.GetStatus("openai").Should().Be(CircuitStatus.Closed);

        sut.RecordFailure("openai");
        sut.GetStatus("openai").Should().Be(CircuitStatus.Open);
    }

    [Fact]
    public void RecordFailure_TresReplicas_AbreProporcionalmente()
    {
        // Com FailureThreshold=6 e 3 réplicas, cada pod abre após 2 falhas.
        var sut = Build(new CircuitBreakerOptions
        {
            FailureThreshold = 6,
            EffectiveReplicaCount = 3
        });

        sut.RecordFailure("openai");
        sut.GetStatus("openai").Should().Be(CircuitStatus.Closed);

        sut.RecordFailure("openai");
        sut.GetStatus("openai").Should().Be(CircuitStatus.Open);
    }

    [Fact]
    public void RecordFailure_ThresholdMenorQueReplicas_PreservaUmMinimo()
    {
        // Com FailureThreshold=2 e 5 réplicas, a divisão 2/5=0 seria inválida.
        // O código garante effectiveThreshold >= 1.
        var sut = Build(new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            EffectiveReplicaCount = 5
        });

        sut.RecordFailure("openai");
        sut.GetStatus("openai").Should().Be(CircuitStatus.Open);
    }

    [Fact]
    public void RecordSuccess_ResetaEstadoAposFalhas()
    {
        var sut = Build(new CircuitBreakerOptions
        {
            FailureThreshold = 6,
            EffectiveReplicaCount = 3
        });

        sut.RecordFailure("openai");
        sut.RecordSuccess("openai");

        sut.RecordFailure("openai");
        // Após reset, precisa de 2 falhas novas — 1 não basta
        sut.GetStatus("openai").Should().Be(CircuitStatus.Closed);
    }

    [Fact]
    public void RecordFailure_DisabledCircuitBreaker_NaoAbre()
    {
        var sut = Build(new CircuitBreakerOptions
        {
            Enabled = false,
            FailureThreshold = 1,
            EffectiveReplicaCount = 1
        });

        sut.RecordFailure("openai");
        sut.RecordFailure("openai");
        sut.GetStatus("openai").Should().Be(CircuitStatus.Closed);
    }
}
