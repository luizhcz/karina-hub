using EfsAiHub.Platform.Runtime.Interfaces;
using EfsAiHub.Host.Api.Models.Responses;
using EfsAiHub.Core.Orchestration.Enums;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/enums")]
[Produces("application/json")]
public class EnumsController : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "Lista todos os enums do sistema utilizados por agentes, workflows e execuções")]
    [ProducesResponseType(typeof(EnumsResponse), StatusCodes.Status200OK)]
    public IActionResult GetAll()
    {
        var response = new EnumsResponse
        {
            OrchestrationModes = Enum.GetNames<OrchestrationMode>().ToList(),
            EdgeTypes          = Enum.GetNames<WorkflowEdgeType>().ToList(),
            ExecutionStatuses  = Enum.GetNames<WorkflowStatus>().ToList(),
            HitlStatuses       = Enum.GetNames<HumanInteractionStatus>().ToList(),
            MiddlewarePhases   = Enum.GetNames<MiddlewarePhase>().ToList(),
        };

        return Ok(response);
    }
}
