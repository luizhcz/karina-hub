using EfsAiHub.Host.Api.Controllers;
using EfsAiHub.Host.Api.Models.Responses;
using Microsoft.AspNetCore.Mvc;

namespace EfsAiHub.Tests.Unit.Controllers;

public class EnumsControllerTests
{
    private readonly EnumsController _sut = new();

    [Fact]
    public void GetAll_Returns200()
    {
        var result = _sut.GetAll();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
    }

    [Fact]
    public void GetAll_ContainsAllEnumCategories()
    {
        var result = _sut.GetAll();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<EnumsResponse>().Subject;

        response.OrchestrationModes.Should().NotBeEmpty();
        response.EdgeTypes.Should().NotBeEmpty();
        response.ExecutionStatuses.Should().NotBeEmpty();
        response.HitlStatuses.Should().NotBeEmpty();
        response.MiddlewarePhases.Should().NotBeEmpty();
    }

    [Fact]
    public void GetAll_OrchestrationModes_ContainsKnownValues()
    {
        var result = _sut.GetAll();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<EnumsResponse>().Subject;

        response.OrchestrationModes.Should().Contain("Sequential");
    }
}
