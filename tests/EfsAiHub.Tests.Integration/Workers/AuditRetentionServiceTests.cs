using System.Reflection;
using EfsAiHub.Host.Worker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EfsAiHub.Tests.Integration.Workers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class AuditRetentionServiceTests(IntegrationWebApplicationFactory factory)
{
    private static readonly MethodInfo ParseUpperBoundMethod =
        typeof(AuditRetentionService)
            .GetMethod("ParseUpperBound",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static DateTime? InvokeParseUpperBound(string? expr) =>
        (DateTime?)ParseUpperBoundMethod.Invoke(null, [expr]);

    // ── ParseUpperBound ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("FOR VALUES FROM ('2025-03-01') TO ('2025-04-01')", 2025, 4, 1)]
    [InlineData("FOR VALUES FROM ('2020-01-01') TO ('2020-02-01')", 2020, 2, 1)]
    [InlineData("FOR VALUES FROM ('2023-11-01') TO ('2023-12-01')", 2023, 12, 1)]
    public void ParseUpperBound_ExpressionValida_ExtraiDataCorretamente(
        string expr, int year, int month, int day)
    {
        var result = InvokeParseUpperBound(expr);

        result.Should().NotBeNull();
        result!.Value.Year.Should().Be(year);
        result.Value.Month.Should().Be(month);
        result.Value.Day.Should().Be(day);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("INVALID EXPRESSION")]
    [InlineData("FOR VALUES WITHOUT TO marker")]
    public void ParseUpperBound_ExpressaoInvalida_RetornaNull(string? expr)
    {
        var result = InvokeParseUpperBound(expr);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseUpperBound_ExpressionSemAspas_RetornaNull()
    {
        // TO without quoted date value
        var result = InvokeParseUpperBound("FOR VALUES FROM (2025-03-01) TO (2025-04-01)");

        result.Should().BeNull();
    }

    // ── RunOnceAsync (via reflection) ─────────────────────────────────────────

    [Fact]
    public async Task RunOnceAsync_BancoSemParticoesAntigas_NaoLancaExcecao()
    {
        var service = factory.Services
            .GetServices<IHostedService>()
            .OfType<AuditRetentionService>()
            .Single();

        var method = typeof(AuditRetentionService)
            .GetMethod("RunOnceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var act = async () => await (Task)method.Invoke(service, [CancellationToken.None])!;

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunOnceAsync_DuasExecucoesConsecutivas_AmbaNaoLancam()
    {
        var service = factory.Services
            .GetServices<IHostedService>()
            .OfType<AuditRetentionService>()
            .Single();

        var method = typeof(AuditRetentionService)
            .GetMethod("RunOnceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Idempotent — running twice should be safe
        await (Task)method.Invoke(service, [CancellationToken.None])!;
        var act = async () => await (Task)method.Invoke(service, [CancellationToken.None])!;

        await act.Should().NotThrowAsync();
    }
}
