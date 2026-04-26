using EfsAiHub.Host.Api.Models.Responses;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/functions")]
[Produces("application/json")]
public class FunctionsController : ControllerBase
{
    private readonly IFunctionToolRegistry    _functionRegistry;
    private readonly ICodeExecutorRegistry    _executorRegistry;
    private readonly IAgentMiddlewareRegistry _middlewareRegistry;
    private readonly IEnumerable<ILlmClientProvider> _llmProviders;

    public FunctionsController(
        IFunctionToolRegistry functionRegistry,
        ICodeExecutorRegistry executorRegistry,
        IAgentMiddlewareRegistry middlewareRegistry,
        IEnumerable<ILlmClientProvider> llmProviders)
    {
        _functionRegistry   = functionRegistry;
        _executorRegistry   = executorRegistry;
        _middlewareRegistry = middlewareRegistry;
        _llmProviders       = llmProviders;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista todas as funções, executores e middlewares registrados no sistema")]
    [ProducesResponseType(typeof(AvailableFunctionsResponse), StatusCodes.Status200OK)]
    public IActionResult GetAll()
    {
        var typeInfo = _executorRegistry.GetTypeInfo();
        var schemas = _executorRegistry.GetSchemas();

        var functionTools = _functionRegistry.GetAll()
            .Select(kv => new FunctionToolInfo
            {
                Name        = kv.Key,
                Description = kv.Value.Description,
                JsonSchema  = kv.Value.JsonSchema,
                Fingerprint = FunctionToolRegistry.ComputeFingerprint(kv.Value),
            })
            .OrderBy(f => f.Name)
            .ToList();

        var codeExecutors = _executorRegistry.GetRegisteredNames()
            .Select(name =>
            {
                typeInfo.TryGetValue(name, out var types);
                schemas.TryGetValue(name, out var schemaInfo);
                return new CodeExecutorInfo
                {
                    Name                = name,
                    InputType           = types.InputType,
                    OutputType          = types.OutputType,
                    InputSchema         = schemaInfo?.InputSchema,
                    OutputSchema        = schemaInfo?.OutputSchema,
                    OutputSchemaVersion = schemaInfo?.OutputSchemaVersion,
                };
            })
            .OrderBy(e => e.Name)
            .ToList();

        var middlewareTypes = _middlewareRegistry.GetRegisteredMetadata()
            .Select(m => new MiddlewareTypeInfo
            {
                Name        = m.Type,
                Phase       = m.Phase,
                Label       = m.Label,
                Description = m.Description,
                Settings    = m.Settings.Select(s => new MiddlewareSettingInfoDto
                {
                    Key          = s.Key,
                    Label        = s.Label,
                    Type         = s.Type,
                    Options      = s.Options?.Select(o => new MiddlewareSettingOptionDto
                    {
                        Value = o.Value,
                        Label = o.Label,
                    }).ToList(),
                    DefaultValue = s.DefaultValue,
                }).ToList(),
            })
            .ToList();

        var providers = _llmProviders
            .Select(p => new LlmProviderInfo { Type = p.ProviderType })
            .OrderBy(p => p.Type)
            .ToList();

        return Ok(new AvailableFunctionsResponse
        {
            FunctionTools      = functionTools,
            CodeExecutors      = codeExecutors,
            MiddlewareTypes    = middlewareTypes,
            AvailableProviders = providers,
        });
    }
}
