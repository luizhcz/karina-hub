using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Platform.Runtime.Execution;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Admin endpoints de Document Intelligence: pricing (CRUD) + usage (read-only).
/// Análogo ao <see cref="ModelPricingController"/> mas para o provider DI que cobra
/// por página (não por token).
/// </summary>
[ApiController]
[Route("api/admin/document-intelligence")]
[Produces("application/json")]
public class DocumentIntelligenceAdminController : ControllerBase
{
    private readonly IDocumentIntelligencePricingRepository _pricingRepo;
    private readonly IDocumentIntelligencePricingCache _pricingCache;
    private readonly IDocumentIntelligenceUsageQueries _usageQueries;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;

    public DocumentIntelligenceAdminController(
        IDocumentIntelligencePricingRepository pricingRepo,
        IDocumentIntelligencePricingCache pricingCache,
        IDocumentIntelligenceUsageQueries usageQueries,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext)
    {
        _pricingRepo = pricingRepo;
        _pricingCache = pricingCache;
        _usageQueries = usageQueries;
        _audit = audit;
        _auditContext = auditContext;
    }

    // ── Usage (read-only) ────────────────────────────────────────────────────

    [HttpGet("usage")]
    [SwaggerOperation(Summary = "Agregados de uso/custo do Document Intelligence num período")]
    public async Task<IActionResult> GetUsage(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = NormalizeRange(from, to);

        var summary = await _usageQueries.GetSummaryAsync(fromUtc, toUtc, ct);
        var byDay = await _usageQueries.GetByDayAsync(fromUtc, toUtc, ct);
        var byModel = await _usageQueries.GetByModelAsync(fromUtc, toUtc, ct);

        return Ok(new
        {
            from = fromUtc,
            to = toUtc,
            summary,
            byDay,
            byModel,
        });
    }

    [HttpGet("jobs")]
    [SwaggerOperation(Summary = "Lista jobs de extração recentes (todos os status)")]
    public async Task<IActionResult> GetRecentJobs(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = NormalizeRange(from, to);
        if (limit > 200) limit = 200;
        if (limit < 1) limit = 50;

        var items = await _usageQueries.GetRecentJobsAsync(fromUtc, toUtc, limit, ct);
        return Ok(new { from = fromUtc, to = toUtc, items });
    }

    // ── Pricing (CRUD) ───────────────────────────────────────────────────────

    [HttpGet("pricing")]
    [SwaggerOperation(Summary = "Lista todos os registros de pricing do Document Intelligence")]
    public async Task<IActionResult> GetAllPricing(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (pageSize > 200) pageSize = 200;
        var items = await _pricingRepo.GetAllAsync(page, pageSize, ct);
        var total = await _pricingRepo.CountAsync(ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("pricing/{id:int}")]
    [SwaggerOperation(Summary = "Retorna um registro de pricing por ID")]
    [ProducesResponseType(typeof(DocumentIntelligencePricing), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPricingById(int id, CancellationToken ct)
    {
        var item = await _pricingRepo.GetByIdAsync(id, ct);
        if (item is null) return NotFound();
        return Ok(item);
    }

    [HttpPost("pricing")]
    [SwaggerOperation(Summary = "Cria ou atualiza um registro de pricing de Document Intelligence")]
    [ProducesResponseType(typeof(DocumentIntelligencePricing), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertPricing(
        [FromBody] DocumentIntelligencePricingRequest request, CancellationToken ct)
    {
        var pricing = new DocumentIntelligencePricing
        {
            Id = request.Id ?? 0,
            ModelId = request.ModelId,
            Provider = request.Provider,
            PricePerPage = request.PricePerPage,
            Currency = request.Currency ?? "USD",
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
        };

        var action = (request.Id ?? 0) == 0 ? AdminAuditActions.Create : AdminAuditActions.Update;
        var before = (request.Id ?? 0) == 0
            ? null
            : AdminAuditContext.Snapshot(await _pricingRepo.GetByIdAsync(request.Id!.Value, ct));

        var result = await _pricingRepo.UpsertAsync(pricing, ct);
        await _pricingCache.InvalidateAsync(pricing.ModelId, pricing.Provider);
        await _audit.RecordAsync(_auditContext.Build(
            action,
            AdminAuditResources.DocumentIntelligencePricing,
            result.Id.ToString(),
            payloadBefore: before,
            payloadAfter: AdminAuditContext.Snapshot(result)), ct);
        return Ok(result);
    }

    [HttpDelete("pricing/{id:int}")]
    [SwaggerOperation(Summary = "Remove um registro de pricing")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePricing(int id, CancellationToken ct)
    {
        var existing = await _pricingRepo.GetByIdAsync(id, ct);
        var deleted = await _pricingRepo.DeleteAsync(id, ct);
        if (!deleted) return NotFound();

        if (existing is not null)
            await _pricingCache.InvalidateAsync(existing.ModelId, existing.Provider);

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Delete,
            AdminAuditResources.DocumentIntelligencePricing,
            id.ToString(),
            payloadBefore: AdminAuditContext.Snapshot(existing)), ct);
        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Default: último 30 dias. Normaliza para UTC pra matchar created_at da tabela.
    private static (DateTime From, DateTime To) NormalizeRange(DateTime? from, DateTime? to)
    {
        var toUtc = (to ?? DateTime.UtcNow).ToUniversalTime();
        var fromUtc = (from ?? toUtc.AddDays(-30)).ToUniversalTime();
        if (fromUtc > toUtc) (fromUtc, toUtc) = (toUtc, fromUtc);
        return (fromUtc, toUtc);
    }
}
