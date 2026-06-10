using EfsAiHub.Core.Agents.DocumentIntelligence;

namespace EfsAiHub.Tests.Unit.Platform;

/// <summary>
/// Cobre a heurística de roteamento de erros do pipeline Document Intelligence.
/// Essas duas classificações decidem como o caller (IngestionJobHandler) trata
/// uma extração que falhou: permanente (não retenta), backpressure (re-enfileira
/// sem consumir tentativa) ou transiente (retenta contando contra MaxAttempts).
/// </summary>
[Trait("Category", "Unit")]
public class ExtractionErrorCodeTests
{
    [Theory]
    [InlineData(ExtractionErrorCode.UnreadablePdf)]
    [InlineData(ExtractionErrorCode.SourceUnavailable)]
    [InlineData(ExtractionErrorCode.PageLimitExceeded)]
    public void IsPermanent_InputErrors_True(string code)
        => ExtractionErrorCode.IsPermanent(code).Should().BeTrue();

    [Theory]
    [InlineData(ExtractionErrorCode.AzureDiFailure)]
    [InlineData(ExtractionErrorCode.Timeout)]
    [InlineData(ExtractionErrorCode.GateTimeout)]
    [InlineData(ExtractionErrorCode.Cancelled)]
    [InlineData(null)]
    public void IsPermanent_TransientOrBackpressure_False(string? code)
        => ExtractionErrorCode.IsPermanent(code).Should().BeFalse();

    [Fact]
    public void IsCapacityBackpressure_GateTimeout_True()
        => ExtractionErrorCode.IsCapacityBackpressure(ExtractionErrorCode.GateTimeout).Should().BeTrue();

    [Theory]
    [InlineData(ExtractionErrorCode.Timeout)]
    [InlineData(ExtractionErrorCode.AzureDiFailure)]
    [InlineData(ExtractionErrorCode.UnreadablePdf)]
    [InlineData(ExtractionErrorCode.Cancelled)]
    [InlineData(null)]
    public void IsCapacityBackpressure_EverythingElse_False(string? code)
        => ExtractionErrorCode.IsCapacityBackpressure(code).Should().BeFalse();

    [Fact]
    public void GateTimeout_IsBackpressureButNotPermanent()
    {
        // Invariante que sustenta o fix: gate cheio re-enfileira (backpressure)
        // sem consumir tentativa, e NUNCA é tratado como falha permanente.
        ExtractionErrorCode.IsCapacityBackpressure(ExtractionErrorCode.GateTimeout).Should().BeTrue();
        ExtractionErrorCode.IsPermanent(ExtractionErrorCode.GateTimeout).Should().BeFalse();
    }
}
