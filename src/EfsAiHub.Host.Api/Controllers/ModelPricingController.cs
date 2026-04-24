using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Platform.Runtime.Execution;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/admin/model-pricing")]
[Produces("application/json")]
public class ModelPricingController : ControllerBase
{
    private readonly IModelPricingRepository _repo;
    private readonly IModelPricingCache _pricingCache;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;

    public ModelPricingController(
        IModelPricingRepository repo,
        IModelPricingCache pricingCache,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext)
    {
        _repo = repo;
        _pricingCache = pricingCache;
        _audit = audit;
        _auditContext = auditContext;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista todos os registros de pricing de modelos LLM")]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (pageSize > 200) pageSize = 200;
        var items = await _repo.GetAllAsync(page, pageSize, ct);
        var total = await _repo.CountAsync(ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(Summary = "Retorna um registro de pricing por ID")]
    [ProducesResponseType(typeof(ModelPricing), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var item = await _repo.GetByIdAsync(id, ct);
        if (item is null) return NotFound();
        return Ok(item);
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Cria ou atualiza um registro de pricing")]
    [ProducesResponseType(typeof(ModelPricing), StatusCodes.Status200OK)]
    public async Task<IActionResult> Upsert([FromBody] ModelPricingRequest request, CancellationToken ct)
    {
        var pricing = new ModelPricing
        {
            Id = request.Id ?? 0,
            ModelId = request.ModelId,
            Provider = request.Provider,
            PricePerInputToken = request.PricePerInputToken,
            PricePerOutputToken = request.PricePerOutputToken,
            Currency = request.Currency ?? "USD",
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo
        };

        // Id=0 = create (Postgres gera identity); Id>0 = update.
        var action = (request.Id ?? 0) == 0 ? AdminAuditActions.Create : AdminAuditActions.Update;
        var before = (request.Id ?? 0) == 0 ? null : AdminAuditContext.Snapshot(await _repo.GetByIdAsync(request.Id!.Value, ct));

        var result = await _repo.UpsertAsync(pricing, ct);
        await _pricingCache.InvalidateAsync(pricing.ModelId);
        await _audit.RecordAsync(_auditContext.Build(
            action,
            AdminAuditResources.ModelPricing,
            result.Id.ToString(),
            payloadBefore: before,
            payloadAfter: AdminAuditContext.Snapshot(result)), ct);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(Summary = "Remove um registro de pricing")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var existing = await _repo.GetByIdAsync(id, ct);
        var deleted = await _repo.DeleteAsync(id, ct);
        if (!deleted) return NotFound();

        if (existing is not null)
            await _pricingCache.InvalidateAsync(existing.ModelId);

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Delete,
            AdminAuditResources.ModelPricing,
            id.ToString(),
            payloadBefore: AdminAuditContext.Snapshot(existing)), ct);
        return NoContent();
    }

    [HttpPost("refresh-view")]
    [SwaggerOperation(Summary = "Atualiza a materialized view v_llm_cost com dados mais recentes")]
    public async Task<IActionResult> RefreshCostView(CancellationToken ct)
    {
        await _repo.RefreshMaterializedViewAsync(ct);
        return Ok(new { message = "Materialized view v_llm_cost atualizada." });
    }
}
