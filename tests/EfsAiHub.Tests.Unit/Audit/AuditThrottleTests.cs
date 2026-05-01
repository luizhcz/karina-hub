using EfsAiHub.Platform.Runtime.Audit;

namespace EfsAiHub.Tests.Unit.Audit;

[Trait("Category", "Unit")]
public class AuditThrottleTests
{
    [Fact]
    public void ShouldLog_PrimeiraChamada_RetornaTrue()
    {
        var throttle = new AuditThrottle(window: TimeSpan.FromSeconds(60), maxEntries: 100);

        throttle.ShouldLog("k1").Should().BeTrue();
    }

    [Fact]
    public void ShouldLog_SegundaChamadaDentroJanela_RetornaFalse()
    {
        var throttle = new AuditThrottle(window: TimeSpan.FromSeconds(60), maxEntries: 100);

        throttle.ShouldLog("k1").Should().BeTrue();
        throttle.ShouldLog("k1").Should().BeFalse();
    }

    [Fact]
    public void ShouldLog_ChavesDistintas_RetornaTrueParaCada()
    {
        var throttle = new AuditThrottle(window: TimeSpan.FromSeconds(60), maxEntries: 100);

        throttle.ShouldLog("k1").Should().BeTrue();
        throttle.ShouldLog("k2").Should().BeTrue();
        throttle.ShouldLog("k3").Should().BeTrue();
    }

    [Fact]
    public void ShouldLog_AposJanelaExpirar_RetornaTrueDeNovo()
    {
        // janela 1ms pra forçar expiração imediata
        var throttle = new AuditThrottle(window: TimeSpan.FromMilliseconds(1), maxEntries: 100);

        throttle.ShouldLog("k1").Should().BeTrue();
        Thread.Sleep(5);
        throttle.ShouldLog("k1").Should().BeTrue();
    }

    [Fact]
    public void EvictionCallback_Disparado_QuandoCapacidadeExcedida()
    {
        var evictionCount = 0;
        var throttle = new AuditThrottle(
            window: TimeSpan.FromSeconds(60),
            maxEntries: 16,
            onEviction: () => evictionCount++);

        // 17 entries únicas — capacity é 16, deve evictar 1.
        for (var i = 0; i < 17; i++)
            throttle.ShouldLog($"k{i}");

        evictionCount.Should().BeGreaterOrEqualTo(1);
        throttle.Count.Should().BeLessOrEqualTo(16);
    }
}
