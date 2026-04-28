using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EfsAiHub.Core.Abstractions.Secrets;
using EfsAiHub.Infra.Secrets;
using EfsAiHub.Infra.Secrets.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Endpoints administrativos pra gestão de referências do AWS Secrets Manager.
/// Gateado por AdminGateMiddleware (não está na lista de rotas públicas).
/// </summary>
[ApiController]
[Route("api/secrets")]
[Produces("application/json")]
public class SecretsController : ControllerBase
{
    private readonly IAmazonSecretsManager _aws;
    private readonly ISecretCacheService _cache;
    private readonly AwsSecretsOptions _options;
    private readonly ILogger<SecretsController> _logger;

    public SecretsController(
        IAmazonSecretsManager aws,
        ISecretCacheService cache,
        IOptions<AwsSecretsOptions> options,
        ILogger<SecretsController> logger)
    {
        _aws = aws;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public sealed record ValidateRequest(string Reference);
    public sealed record ValidateResponse(
        bool Exists,
        DateTime? LastChanged,
        IReadOnlyDictionary<string, string>? Tags,
        string? Error);

    [HttpPost("validate")]
    [SwaggerOperation(Summary = "Valida que uma referência AWS Secrets Manager existe e é acessível pelo backend")]
    [ProducesResponseType(typeof(ValidateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Validate([FromBody] ValidateRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Reference))
            return BadRequest(new { error = "Reference is required." });

        var reference = SecretReference.Parse(body.Reference);
        if (reference is not AwsSecretReference aws)
        {
            return BadRequest(new
            {
                error = $"Invalid AWS reference. Expected prefix '{SecretReference.AwsPrefix}'."
            });
        }

        try
        {
            var resp = await _aws.DescribeSecretAsync(
                new DescribeSecretRequest { SecretId = aws.Identifier }, ct);

            var tags = resp.Tags?.ToDictionary(t => t.Key, t => t.Value);
            return Ok(new ValidateResponse(
                Exists: true,
                LastChanged: resp.LastChangedDate,
                Tags: tags,
                Error: null));
        }
        catch (ResourceNotFoundException)
        {
            return Ok(new ValidateResponse(false, null, null, "Secret not found."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SecretsController] DescribeSecret '{Identifier}' falhou.", aws.Identifier);
            return Ok(new ValidateResponse(false, null, null, ex.Message));
        }
    }

    [HttpDelete("cache")]
    [SwaggerOperation(Summary = "Invalida o cache 2-tier (L1+L2) para uma referência específica — usar após rotação no AWS Console")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InvalidateCache([FromQuery] string reference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return BadRequest(new { error = "reference query param is required." });

        var parsed = SecretReference.Parse(reference);
        if (parsed is not AwsSecretReference aws)
        {
            return BadRequest(new
            {
                error = $"Invalid AWS reference. Expected prefix '{SecretReference.AwsPrefix}'."
            });
        }

        await _cache.InvalidateAsync(aws.Identifier);
        return NoContent();
    }

    public sealed record HealthResponse(bool AwsReachable, string? Error, string? CanaryReference);

    [HttpGet("health")]
    [SwaggerOperation(Summary = "Health check do AWS Secrets Manager via DescribeSecret no canary configurado")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        var canaryRaw = _options.HealthCheckCanaryReference;
        if (string.IsNullOrWhiteSpace(canaryRaw))
            return Ok(new HealthResponse(false, "No canary reference configured (Secrets:Aws:HealthCheckCanaryReference).", null));

        var reference = SecretReference.Parse(canaryRaw);
        if (reference is not AwsSecretReference aws)
            return Ok(new HealthResponse(false, $"Canary reference is not a valid AWS reference (expected prefix '{SecretReference.AwsPrefix}').", canaryRaw));

        try
        {
            await _aws.DescribeSecretAsync(new DescribeSecretRequest { SecretId = aws.Identifier }, ct);
            return Ok(new HealthResponse(true, null, canaryRaw));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SecretsController] AWS canary probe '{Identifier}' falhou.", aws.Identifier);
            return Ok(new HealthResponse(false, ex.Message, canaryRaw));
        }
    }
}
