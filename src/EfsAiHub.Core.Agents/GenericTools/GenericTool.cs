using System.Text.Json;
using System.Text.RegularExpressions;
using EfsAiHub.Core.Abstractions.Exceptions;

namespace EfsAiHub.Core.Agents.GenericTools;

/// <summary>
/// Tool HTTP genérica cadastrada por projeto. Materializada em runtime como
/// <c>AIFunction</c> dinâmica e exposta ao LLM via merge de path/query/body em
/// um único schema. Strictamente owner-only — nunca cross-project.
/// </summary>
public sealed class GenericTool
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required string TenantId { get; init; }
    public required string Name { get; set; }
    public required HttpMethodType HttpMethod { get; init; }
    public required string UrlTemplate { get; set; }

    public IReadOnlyDictionary<string, ParamDefinition> PathParams { get; set; }
        = new Dictionary<string, ParamDefinition>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ParamDefinition> QueryParams { get; set; }
        = new Dictionary<string, ParamDefinition>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> CustomHeaders { get; set; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    public InputContentType InputContentType { get; set; } = InputContentType.None;

    /// <summary>JSON Schema raw do body. Null quando InputContentType é None ou Text.</summary>
    public string? InputSchema { get; set; }

    public OutputContentType OutputContentType { get; set; } = OutputContentType.Json;

    /// <summary>JSON Schema raw da resposta. Null quando OutputContentType é Text.</summary>
    public string? OutputSchema { get; set; }

    /// <summary>
    /// Controla validação e projeção do response contra <see cref="OutputSchema"/>
    /// antes do LLM receber. Default <c>Off</c> preserva o comportamento legado
    /// (tools cadastradas antes da feature). Admin opta in via UI.
    /// </summary>
    public OutputProjectionMode OutputProjectionMode { get; set; } = OutputProjectionMode.Off;

    /// <summary>
    /// Override por-tool do timeout em segundos. Quando null, executor aplica o
    /// default global (<c>GenericToolsOptions.DefaultTimeoutSeconds</c>). Limitado
    /// pelo máximo configurado — validado no service via <see cref="EnsureWithinTimeoutCeiling"/>.
    /// </summary>
    public int? TimeoutSecondsOverride { get; set; }

    /// <summary>
    /// Quando true, a tool é "exclusiva do usuário": o executor anexa
    /// <c>app_origin</c> e <c>access_token</c> da request original na chamada
    /// downstream (forward de credenciais). O provedor da tool autoriza contra
    /// o token do user — 401/403 vira "sem permissão pra essa ferramenta".
    /// Quando false (default), a chamada vai sem essas credenciais — tool
    /// considerada "geral", autorizada via CustomHeaders fixos ou pública.
    /// </summary>
    public bool IsExclusive { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public static readonly Regex PlaceholderRegex =
        new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    /// <summary>
    /// Pattern aceito pra Name. Espelha a regra de function name do OpenAI/
    /// Anthropic — o Name é exposto ao LLM como nome da função no schema.
    /// Snake_case ou kebab-case, sem espaços, acentos ou unicode. Max 64.
    /// </summary>
    public static readonly Regex NameRegex =
        new(@"^[a-zA-Z_][a-zA-Z0-9_-]{0,63}$", RegexOptions.Compiled);

    private static readonly HashSet<string> ReservedHeaders =
        new(StringComparer.OrdinalIgnoreCase) { "Content-Type", "Accept" };

    public void EnsureInvariants()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new DomainException("GenericTool.Id é obrigatório.");
        if (string.IsNullOrWhiteSpace(ProjectId))
            throw new DomainException("GenericTool.ProjectId é obrigatório.");
        if (string.IsNullOrWhiteSpace(TenantId))
            throw new DomainException("GenericTool.TenantId é obrigatório.");
        if (string.IsNullOrWhiteSpace(Name))
            throw new DomainException("GenericTool.Name é obrigatório.");
        if (!NameRegex.IsMatch(Name))
            throw new DomainException(
                $"GenericTool.Name '{Name}' inválido. Aceita letras/dígitos/underscore/hífen, " +
                "começa com letra ou underscore, máximo 64 chars (ex: 'get_quote', 'lookup-user').");
        if (string.IsNullOrWhiteSpace(UrlTemplate))
            throw new DomainException("GenericTool.UrlTemplate é obrigatório.");

        var placeholders = ExtractPlaceholders(UrlTemplate);
        var pathKeys = new HashSet<string>(PathParams.Keys, StringComparer.Ordinal);

        var missingDefs = placeholders.Where(p => !pathKeys.Contains(p)).ToArray();
        if (missingDefs.Length > 0)
            throw new DomainException(
                $"GenericTool.UrlTemplate referencia placeholders sem PathParam correspondente: {string.Join(", ", missingDefs)}.");

        var orphanDefs = pathKeys.Where(k => !placeholders.Contains(k)).ToArray();
        if (orphanDefs.Length > 0)
            throw new DomainException(
                $"GenericTool.PathParams declara chaves ausentes no UrlTemplate: {string.Join(", ", orphanDefs)}.");

        foreach (var key in CustomHeaders.Keys)
        {
            if (ReservedHeaders.Contains(key))
                throw new DomainException(
                    $"GenericTool.CustomHeaders não pode definir '{key}' — header reservado (atribuído pelo executor).");
        }

        if (HttpMethod == HttpMethodType.GET
            && InputContentType is InputContentType.FormUrlEncoded or InputContentType.Text)
        {
            // GET aceita None (sem body) ou Json (vira query string flattened).
            // FormUrlEncoded e Text não fazem sentido sem body — bloqueio explícito.
            throw new DomainException(
                "GenericTool com HttpMethod=GET só aceita InputContentType=None ou Json.");
        }

        switch (InputContentType)
        {
            case InputContentType.None:
            case InputContentType.Text:
                break;
            case InputContentType.Json:
                EnsureSchemaPresent(nameof(InputSchema), InputSchema);
                NormalizationGuard.AssertCanonical(InputSchema, nameof(InputSchema));
                break;
            case InputContentType.FormUrlEncoded:
                EnsureSchemaPresent(nameof(InputSchema), InputSchema);
                EnsureFlatSchema(InputSchema!);
                NormalizationGuard.AssertCanonical(InputSchema, nameof(InputSchema));
                break;
        }

        if (OutputContentType is OutputContentType.Json or OutputContentType.Csv)
        {
            EnsureSchemaPresent(nameof(OutputSchema), OutputSchema);
            // Json/Csv sempre projeta — o LLM nunca vê response cru de API
            // estruturada. Schema canonicalizado upstream pelo SchemaNormalizer;
            // o guard aqui é defesa em profundidade contra escrita direta no DB.
            if (OutputProjectionMode != OutputProjectionMode.Project)
                throw new DomainException(
                    "GenericTool.OutputProjectionMode deve ser 'Project' quando OutputContentType é Json ou Csv.");
            NormalizationGuard.AssertCanonical(OutputSchema, nameof(OutputSchema));
        }
        else if (OutputProjectionMode != OutputProjectionMode.Off)
        {
            // Text não tem shape pra projetar — força Off explicitamente.
            throw new DomainException(
                "GenericTool.OutputProjectionMode deve ser 'Off' quando OutputContentType é Text.");
        }

        if (TimeoutSecondsOverride is int t && t <= 0)
            throw new DomainException(
                "GenericTool.TimeoutSecondsOverride deve ser maior que zero quando presente.");
    }

    /// <summary>
    /// Confirma que o override (quando definido) respeita o teto vindo da configuração.
    /// Separado do <see cref="EnsureInvariants"/> porque o teto vive em
    /// <c>GenericToolsOptions</c> e não no domain.
    /// </summary>
    public void EnsureWithinTimeoutCeiling(int maxTimeoutSeconds)
    {
        if (TimeoutSecondsOverride is int t && t > maxTimeoutSeconds)
            throw new DomainException(
                $"GenericTool.TimeoutSecondsOverride={t} excede o limite global ({maxTimeoutSeconds}s).");
    }

    public static IReadOnlySet<string> ExtractPlaceholders(string urlTemplate)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in PlaceholderRegex.Matches(urlTemplate))
            set.Add(match.Groups[1].Value);
        return set;
    }

    private static void EnsureSchemaPresent(string field, string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema))
            throw new DomainException($"GenericTool.{field} é obrigatório pra esse Content-Type.");
    }

    private static void EnsureFlatSchema(string schemaJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(schemaJson);
        }
        catch (JsonException ex)
        {
            throw new DomainException(
                $"GenericTool.InputSchema não é um JSON válido: {ex.Message}");
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("properties", out var props)
                || props.ValueKind != JsonValueKind.Object)
                return;

            foreach (var prop in props.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                if (!prop.Value.TryGetProperty("type", out var typeNode)) continue;

                var type = typeNode.ValueKind == JsonValueKind.String ? typeNode.GetString() : null;
                if (type is "object" or "array")
                    throw new DomainException(
                        $"GenericTool.InputSchema com FormUrlEncoded precisa ser plano — propriedade '{prop.Name}' tem type='{type}'.");
            }
        }
    }
}
