using System.Text.Json;

namespace EfsAiHub.Core.Orchestration.Executors;

/// <summary>
/// Schema JSON dos tipos de input/output de um code executor tipado.
/// Gerado via System.Text.Json.Schema.JsonSchemaExporter (.NET 9+).
/// Consumido pela UI de edges tipados (frontend descobre campos disponíveis
/// pra Conditional/Switch via OutputSchema do nó produtor).
/// </summary>
/// <param name="InputSchema">JSON Schema do tipo TInput.</param>
/// <param name="OutputSchema">JSON Schema do tipo TOutput.</param>
/// <param name="OutputSchemaVersion">
/// Hash sha256 (12 chars) do OutputSchema serializado — usado como invalidador de cache quando
/// o produtor muda schema. <b>TODO:</b> a estabilidade depende de System.Text.Json preservar
/// ordem de propriedades; canonicalizar via JCS (RFC 8785) quando o épico de edges tipados
/// (PR 3+) tornar isso contrato cross-deploy. Hoje suficiente porque é consumido só intra-deploy.
/// </param>
public sealed record ExecutorSchemaInfo(
    JsonElement InputSchema,
    JsonElement OutputSchema,
    string OutputSchemaVersion);
