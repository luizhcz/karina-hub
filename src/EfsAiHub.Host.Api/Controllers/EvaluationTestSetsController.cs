using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Agents.Evaluation;
using EfsAiHub.Host.Api.Models.Requests.Evaluation;
using EfsAiHub.Host.Api.Models.Responses.Evaluation;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Host.Api.Services.Evaluation;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// CRUD de <see cref="EvaluationTestSet"/> + versions append-only + cases.
/// Visibility=global exige permissão admin (validador no controller).
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class EvaluationTestSetsController : ControllerBase
{
    private readonly IEvaluationTestSetRepository _testSetRepo;
    private readonly IEvaluationTestSetVersionRepository _testSetVersionRepo;
    private readonly IEvaluationTestCaseRepository _testCaseRepo;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;
    private readonly ILogger<EvaluationTestSetsController> _logger;

    public EvaluationTestSetsController(
        IEvaluationTestSetRepository testSetRepo,
        IEvaluationTestSetVersionRepository testSetVersionRepo,
        IEvaluationTestCaseRepository testCaseRepo,
        IProjectContextAccessor projectAccessor,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext,
        ILogger<EvaluationTestSetsController> logger)
    {
        _testSetRepo = testSetRepo;
        _testSetVersionRepo = testSetVersionRepo;
        _testCaseRepo = testCaseRepo;
        _projectAccessor = projectAccessor;
        _audit = audit;
        _auditContext = auditContext;
        _logger = logger;
    }

    [HttpPost("api/projects/{projectId}/evaluation-test-sets")]
    [SwaggerOperation(Summary = "Cria um TestSet (header)")]
    [ProducesResponseType(typeof(EvaluationTestSetResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        string projectId, [FromBody] CreateTestSetRequest request, CancellationToken ct)
    {
        if (!TryParseVisibility(request.Visibility, out var visibility, out var error))
            return BadRequest(new { error });

        // Visibility=global exige autorização admin que ainda não está modelada
        // aqui — bloqueia criação direta como global.
        if (visibility == TestSetVisibility.Global)
            return Forbid("Visibility=global requer permissão admin (não suportado nesta versão).");

        var testSet = new EvaluationTestSet(
            Id: $"ts-{Guid.NewGuid():N}",
            ProjectId: projectId,
            Name: request.Name,
            Description: request.Description,
            Visibility: visibility,
            CurrentVersionId: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            CreatedBy: ResolveActorUserId());

        await _testSetRepo.UpsertAsync(testSet, ct);
        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Create,
            "EvaluationTestSet",
            testSet.Id,
            payloadAfter: AdminAuditContext.Snapshot(EvaluationTestSetResponse.FromDomain(testSet))), ct);

        return CreatedAtAction(nameof(GetById), new { id = testSet.Id }, EvaluationTestSetResponse.FromDomain(testSet));
    }

    [HttpGet("api/evaluation-test-sets/{id}")]
    [SwaggerOperation(Summary = "Header + versions de um TestSet")]
    [ProducesResponseType(typeof(TestSetWithVersionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var testSet = await _testSetRepo.GetByIdAsync(id, ct);
        if (!IsAccessible(testSet)) return NotFound();

        var versions = await _testSetVersionRepo.ListByTestSetAsync(id, ct);
        return Ok(new TestSetWithVersionsResponse(
            EvaluationTestSetResponse.FromDomain(testSet!),
            versions.Select(EvaluationTestSetVersionResponse.FromDomain).ToList()));
    }

    [HttpGet("api/projects/{projectId}/evaluation-test-sets")]
    [SwaggerOperation(Summary = "Lista TestSets do projeto (+ globais se includeGlobal=true)")]
    [ProducesResponseType(typeof(IReadOnlyList<EvaluationTestSetResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByProject(
        string projectId,
        [FromQuery] bool includeGlobal = true,
        CancellationToken ct = default)
    {
        var sets = await _testSetRepo.ListByProjectAsync(projectId, includeGlobal, ct);
        return Ok(sets.Select(EvaluationTestSetResponse.FromDomain));
    }

    [HttpPost("api/evaluation-test-sets/{id}/versions")]
    [SwaggerOperation(Summary = "Publica nova versão (snapshot append-only) com cases inline")]
    [ProducesResponseType(typeof(EvaluationTestSetVersionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PublishVersion(
        string id, [FromBody] PublishTestSetVersionRequest request, CancellationToken ct)
    {
        var testSet = await _testSetRepo.GetByIdAsync(id, ct);
        if (!IsAccessible(testSet)) return NotFound();

        if (request.Cases is null || request.Cases.Count == 0)
            return BadRequest(new { error = "Cases não pode ser vazio." });

        var revision = await _testSetVersionRepo.GetNextRevisionAsync(testSet.Id, ct);

        var temp = new List<EvaluationTestCase>(request.Cases.Count);
        for (int i = 0; i < request.Cases.Count; i++)
        {
            var c = request.Cases[i];
            JsonDocument? toolCalls = null;
            if (c.ExpectedToolCalls.HasValue && c.ExpectedToolCalls.Value.ValueKind != JsonValueKind.Null)
                toolCalls = JsonDocument.Parse(c.ExpectedToolCalls.Value.GetRawText());

            temp.Add(new EvaluationTestCase(
                CaseId: Guid.NewGuid().ToString("N"),
                TestSetVersionId: "<placeholder>",
                Index: i,
                Input: c.Input,
                ExpectedOutput: c.ExpectedOutput,
                ExpectedToolCalls: toolCalls,
                Tags: c.Tags ?? Array.Empty<string>(),
                Weight: c.Weight ?? 1.0,
                CreatedAt: DateTime.UtcNow));
        }

        var version = EvaluationTestSetVersion.Build(
            testSetId: testSet.Id,
            revision: revision,
            cases: temp,
            createdBy: ResolveActorUserId(),
            changeReason: request.ChangeReason);
        var rebound = temp.Select(c => c with { TestSetVersionId = version.TestSetVersionId }).ToList();

        var persisted = await _testSetVersionRepo.AppendAsync(version, rebound, ct);

        // Marca como Published (Build cria como Draft).
        if (persisted.Status != TestSetVersionStatus.Published)
            await _testSetVersionRepo.SetStatusAsync(persisted.TestSetVersionId, TestSetVersionStatus.Published, ct);

        await _testSetRepo.SetCurrentVersionAsync(testSet.Id, persisted.TestSetVersionId, ct);

        var refreshed = persisted with { Status = TestSetVersionStatus.Published };
        return CreatedAtAction(nameof(ListVersionCases),
            new { vid = refreshed.TestSetVersionId },
            EvaluationTestSetVersionResponse.FromDomain(refreshed));
    }

    [HttpPost("api/evaluation-test-sets/{id}/versions/import")]
    [SwaggerOperation(Summary = "Importa cases de CSV e publica nova versão")]
    [ProducesResponseType(typeof(EvaluationTestSetVersionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportCsv(
        string id,
        [FromForm] CsvImportRequest request,
        CancellationToken ct)
    {
        var testSet = await _testSetRepo.GetByIdAsync(id, ct);
        if (!IsAccessible(testSet)) return NotFound();

        if (request.File is null || request.File.Length == 0)
            return BadRequest(new { error = "Arquivo CSV ausente ou vazio." });

        IReadOnlyList<EvaluationTestCase> cases;
        try
        {
            await using var stream = request.File.OpenReadStream();
            // Parser usa placeholder para TestSetVersionId; cases são re-vinculados
            // após a version ser construída.
            cases = CsvTestCaseParser.Parse(testSetVersionId: "<placeholder>", stream);
        }
        catch (CsvTestCaseParser.CsvParseException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var revision = await _testSetVersionRepo.GetNextRevisionAsync(testSet.Id, ct);
        var draft = EvaluationTestSetVersion.Build(
            testSetId: testSet.Id,
            revision: revision,
            cases: cases,
            createdBy: ResolveActorUserId(),
            changeReason: request.ChangeReason);

        // Re-vincula cases ao TestSetVersionId real.
        var rebound = cases.Select(c => c with { TestSetVersionId = draft.TestSetVersionId }).ToList();
        var persisted = await _testSetVersionRepo.AppendAsync(draft, rebound, ct);
        if (persisted.Status != TestSetVersionStatus.Published)
            await _testSetVersionRepo.SetStatusAsync(persisted.TestSetVersionId, TestSetVersionStatus.Published, ct);
        await _testSetRepo.SetCurrentVersionAsync(testSet.Id, persisted.TestSetVersionId, ct);

        var refreshed = persisted with { Status = TestSetVersionStatus.Published };
        return CreatedAtAction(nameof(ListVersionCases),
            new { vid = refreshed.TestSetVersionId },
            EvaluationTestSetVersionResponse.FromDomain(refreshed));
    }

    [HttpGet("api/evaluation-test-sets/versions/{vid}/cases")]
    [SwaggerOperation(Summary = "Lista cases de uma version (paginado)")]
    [ProducesResponseType(typeof(IReadOnlyList<EvaluationTestCaseResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListVersionCases(
        string vid,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null,
        CancellationToken ct = default)
    {
        var cases = await _testCaseRepo.ListByVersionAsync(vid, skip, take, ct);
        return Ok(cases.Select(EvaluationTestCaseResponse.FromDomain));
    }

    [HttpPut("api/evaluation-test-sets/{id}/versions/{vid}/status")]
    [SwaggerOperation(Summary = "Altera status de uma version (Draft|Published|Deprecated)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateVersionStatus(
        string id, string vid,
        [FromBody] UpdateTestSetVersionStatusRequest request,
        CancellationToken ct)
    {
        if (!Enum.TryParse<TestSetVersionStatus>(request.Status, ignoreCase: true, out var status))
            return BadRequest(new { error = $"Status inválido: '{request.Status}'. Use Draft|Published|Deprecated." });

        var testSet = await _testSetRepo.GetByIdAsync(id, ct);
        if (!IsAccessible(testSet)) return NotFound();

        var version = await _testSetVersionRepo.GetByIdAsync(vid, ct);
        if (version is null || version.TestSetId != id) return NotFound();

        await _testSetVersionRepo.SetStatusAsync(vid, status, ct);

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Update,
            "EvaluationTestSetVersion",
            vid,
            payloadAfter: AdminAuditContext.Snapshot(new { Status = status.ToString() })), ct);

        return NoContent();
    }

    [HttpPost("api/evaluation-test-sets/{id}/copy")]
    [SwaggerOperation(Summary = "Copia testset (header + versions + cases) pra outro projeto")]
    [ProducesResponseType(typeof(EvaluationTestSetResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Copy(
        string id,
        [FromQuery] string targetProject,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetProject))
            return BadRequest(new { error = "targetProject é obrigatório." });

        var source = await _testSetRepo.GetByIdAsync(id, ct);
        if (!IsAccessible(source)) return NotFound();

        var clone = source! with
        {
            Id = $"ts-{Guid.NewGuid():N}",
            ProjectId = targetProject,
            Visibility = TestSetVisibility.Project, // cópia sempre cai como project
            CurrentVersionId = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = ResolveActorUserId()
        };
        await _testSetRepo.UpsertAsync(clone, ct);

         // Copia versions Published com seus cases. Skip Draft/Deprecated do source.
         var versions = await _testSetVersionRepo.ListByTestSetAsync(source!.Id, ct);
        string? lastVersionId = null;
        foreach (var v in versions.Where(v => v.Status == TestSetVersionStatus.Published).OrderBy(v => v.Revision))
        {
            var cases = await _testCaseRepo.ListByVersionAsync(v.TestSetVersionId, ct: ct);
            var newRevision = await _testSetVersionRepo.GetNextRevisionAsync(clone.Id, ct);
            var newVersion = EvaluationTestSetVersion.Build(
                testSetId: clone.Id,
                revision: newRevision,
                cases: cases,
                createdBy: ResolveActorUserId(),
                changeReason: $"Copiado de {source!.Id}/v{v.Revision}");
            var newCases = cases.Select(c => c with
            {
                CaseId = Guid.NewGuid().ToString("N"),
                TestSetVersionId = newVersion.TestSetVersionId
            }).ToList();
            var persisted = await _testSetVersionRepo.AppendAsync(newVersion, newCases, ct);
            await _testSetVersionRepo.SetStatusAsync(persisted.TestSetVersionId, TestSetVersionStatus.Published, ct);
            lastVersionId = persisted.TestSetVersionId;
        }
        if (lastVersionId is not null)
            await _testSetRepo.SetCurrentVersionAsync(clone.Id, lastVersionId, ct);

        var final = await _testSetRepo.GetByIdAsync(clone.Id, ct);
        return CreatedAtAction(nameof(GetById), new { id = clone.Id }, EvaluationTestSetResponse.FromDomain(final!));
    }

    private static bool TryParseVisibility(string s, out TestSetVisibility result, out string? error)
    {
        if (string.Equals(s, "project", StringComparison.OrdinalIgnoreCase))
        {
            result = TestSetVisibility.Project; error = null; return true;
        }
        if (string.Equals(s, "global", StringComparison.OrdinalIgnoreCase))
        {
            result = TestSetVisibility.Global; error = null; return true;
        }
        result = TestSetVisibility.Project;
        error = $"Visibility inválido: '{s}'. Use project|global.";
        return false;
    }

    public sealed record TestSetWithVersionsResponse(
        EvaluationTestSetResponse TestSet,
        IReadOnlyList<EvaluationTestSetVersionResponse> Versions);

    private IActionResult Forbid(string reason) => StatusCode(StatusCodes.Status403Forbidden, new { error = reason });

    /// <summary>
    /// Tenant guard. Retorna <c>true</c> se o testset existe e é Global ou
    /// pertence ao project context atual. Caller responde <c>404</c> quando false
    /// (não distingue "não existe" de "existe noutro tenant" — defesa contra enumeration).
    /// </summary>
    private bool IsAccessible(EvaluationTestSet? testSet)
    {
        if (testSet is null) return false;
        if (testSet.Visibility == TestSetVisibility.Global) return true;
        return testSet.ProjectId == _projectAccessor.Current.ProjectId;
    }

    private string? ResolveActorUserId()
    {
        var headers = HttpContext?.Request?.Headers;
        if (headers is null) return null;
        var resolver = HttpContext!.RequestServices.GetService<UserIdentityResolver>();
        if (resolver is null) return null;
        var identity = resolver.TryResolve(headers, out _);
        return identity?.UserId;
    }
}
