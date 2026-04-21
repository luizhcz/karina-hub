using EfsAiHub.Host.Api.Controllers;
using EfsAiHub.Platform.Runtime.Resilience;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.Controllers;

public class SystemControllerTests
{
    private readonly LlmCircuitBreaker _circuitBreaker;
    private readonly SystemController _sut;

    public SystemControllerTests()
    {
        _circuitBreaker = new LlmCircuitBreaker(
            Options.Create(new CircuitBreakerOptions()),
            NullLogger<LlmCircuitBreaker>.Instance);

        _sut = new SystemController(_circuitBreaker);
    }

    [Fact]
    public void GetCircuitBreakers_Returns200_WithEmptyList()
    {
        var result = _sut.GetCircuitBreakers();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<CircuitBreakersResponse>().Subject;
        response.CircuitBreakers.Should().BeEmpty();
    }

    [Fact]
    public void GetCircuitBreakers_AfterFailure_ReturnsProviderState()
    {
        _circuitBreaker.RecordFailure("openai");

        var result = _sut.GetCircuitBreakers();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<CircuitBreakersResponse>().Subject;
        response.CircuitBreakers.Should().ContainSingle(cb => cb.ProviderKey == "openai");
    }

    [Fact]
    public void GetCircuitBreakers_OrdersByProviderKey()
    {
        _circuitBreaker.RecordFailure("openai");
        _circuitBreaker.RecordFailure("azure");

        var result = _sut.GetCircuitBreakers();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<CircuitBreakersResponse>().Subject;
        response.CircuitBreakers.Should().BeInAscendingOrder(cb => cb.ProviderKey);
    }
}
