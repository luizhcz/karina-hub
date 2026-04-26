using System.Globalization;
using System.Text;
using EfsAiHub.Platform.Runtime.Migration;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Endpoint admin read-only que detecta workflows com <c>Condition</c> legado vivo
/// em edges Conditional/Switch. O domínio não aceita mais esse campo — saída esperada
/// é zero entries. Qualquer linha indica corrupção (UPDATE manual, restore parcial,
/// bypass do save) e deve virar issue de incidente. Não modifica nada no banco.
/// </summary>
[ApiController]
[Route("api/admin/workflows/edge-migration-report")]
[Produces("application/json")]
public class EdgeMigrationReportController : ControllerBase
{
    private readonly EdgeMigrationReporter _reporter;

    public EdgeMigrationReportController(EdgeMigrationReporter reporter)
    {
        _reporter = reporter;
    }

    /// <summary>Retorna o relatório completo em JSON com sumário + lista de entradas.</summary>
    [HttpGet]
    [SwaggerOperation(Summary = "Lista edges Conditional/Switch com Condition legado nos workflows do seed (read-only).")]
    [ProducesResponseType(typeof(EdgeMigrationReport), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var report = await _reporter.GenerateAsync(ct);
        return Ok(report);
    }

    /// <summary>
    /// Retorna o relatório como CSV — formato de planilha pro tech lead anotar decisões
    /// (refatorado/migrado/removido) por linha. Content-Type: text/csv.
    /// </summary>
    [HttpGet("csv")]
    [SwaggerOperation(Summary = "Mesmo relatório em CSV.")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    public async Task<IActionResult> GetCsv(CancellationToken ct)
    {
        var report = await _reporter.GenerateAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("workflow_id,workflow_name,edge_index,edge_type,case_index,from_node_id,from_kind,has_schema,legacy_condition,recommended_action,recommendation_hint");
        foreach (var e in report.Entries)
        {
            sb.Append(Csv(e.WorkflowId)).Append(',')
              .Append(Csv(e.WorkflowName)).Append(',')
              .Append(e.EdgeIndex.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(Csv(e.EdgeType)).Append(',')
              .Append(e.CaseIndex?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
              .Append(Csv(e.FromNodeId)).Append(',')
              .Append(Csv(e.FromKind)).Append(',')
              .Append(e.HasSchema ? "yes" : "no").Append(',')
              .Append(Csv(e.LegacyCondition)).Append(',')
              .Append(Csv(e.RecommendedAction)).Append(',')
              .Append(Csv(e.RecommendationHint))
              .AppendLine();
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv; charset=utf-8", "edge-migration-report.csv");
    }

    private static string Csv(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var needsQuote = raw.IndexOfAny(['"', ',', '\n', '\r']) >= 0;
        if (!needsQuote) return raw;
        return "\"" + raw.Replace("\"", "\"\"") + "\"";
    }
}
