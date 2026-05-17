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
    public string Description { get; set; } = string.Empty;
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
    /// Texto livre opcional que documenta quando o agente deve invocar essa tool —
    /// repassado pro system prompt como gatilho de uso ("Use quando: ..."). Não
    /// afeta runtime nem validação; apenas orientação semântica pro LLM.
    /// </summary>
    public string? WhenToUse { get; set; }

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

        if (HttpMethod == HttpMethodType.GET && InputContentType != InputContentType.None)
            throw new DomainException(
                "GenericTool com HttpMethod=GET deve ter InputContentType=None.");

        switch (InputContentType)
        {
            case InputContentType.None:
            case InputContentType.Text:
                break;
            case InputContentType.Json:
                EnsureSchemaPresent(nameof(InputSchema), InputSchema);
                break;
            case InputContentType.FormUrlEncoded:
                EnsureSchemaPresent(nameof(InputSchema), InputSchema);
                EnsureFlatSchema(InputSchema!);
                break;
        }

        if (OutputContentType is OutputContentType.Json or OutputContentType.Csv)
            EnsureSchemaPresent(nameof(OutputSchema), OutputSchema);

        // Output projection requer schema declarativo de verdade. Text não tem
        // shape pra projetar; schemas vazios não declaram contrato algum;
        // oneOf/anyOf introduzem ambiguidade no drop-extras (qual variant
        // aplicar?), reservados pra V2.
        if (OutputProjectionMode != OutputProjectionMode.Off)
        {
            if (OutputContentType == OutputContentType.Text)
                throw new DomainException(
                    "GenericTool.OutputProjectionMode != Off é incompatível com OutputContentType=Text.");
            EnsureSchemaIsProjectable(OutputSchema);
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

    /// <summary>
    /// Para tools com <see cref="OutputProjectionMode"/> != Off: o schema
    /// precisa declarar um contrato útil. Rejeita JSON inválido, schemas
    /// vazios ({} / sem <c>properties</c> em type=object), e keywords
    /// <c>oneOf</c>/<c>anyOf</c> em qualquer nível (V1 não suporta — drop-extras
    /// fica ambíguo sobre qual variant aplicar).
    /// </summary>
    private static void EnsureSchemaIsProjectable(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
            throw new DomainException(
                "GenericTool.OutputSchema é obrigatório quando OutputProjectionMode != Off.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(schemaJson);
        }
        catch (JsonException ex)
        {
            throw new DomainException(
                $"GenericTool.OutputSchema não é um JSON válido: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new DomainException(
                    "GenericTool.OutputSchema deve ser um objeto JSON Schema.");

            ScanForUnsupportedKeywords(root);

            // Schema vazio ({}, ou type=object sem properties) não declara
            // contrato algum — projeção viraria no-op silencioso.
            if (IsEmptyOrTrivialObjectSchema(root))
                throw new DomainException(
                    "GenericTool.OutputSchema não pode ser vazio quando OutputProjectionMode != Off — declare ao menos uma propriedade.");
        }
    }

    /// <summary>
    /// Conjuntos de keywords de JSON Schema que aceitam um sub-schema como
    /// valor — é onde precisamos descer recursivamente. Demais campos
    /// (description, enum, default, etc.) podem ter qualquer conteúdo
    /// arbitrário (incluindo strings literais "oneOf") sem que isso seja
    /// uma keyword de schema. Descer só nesses containers evita falso
    /// positivo em property literal nomeada "oneOf"/"anyOf"/"$ref".
    /// </summary>
    private static readonly HashSet<string> SchemaContainerKeywords =
        new(StringComparer.Ordinal)
        {
            "items", "additionalProperties", "contains", "if", "then", "else", "not",
            "propertyNames", "unevaluatedItems", "unevaluatedProperties",
        };
    private static readonly HashSet<string> SchemaMapKeywords =
        new(StringComparer.Ordinal)
        {
            "properties", "patternProperties", "definitions", "$defs", "dependentSchemas",
        };
    private static readonly HashSet<string> SchemaArrayKeywords =
        new(StringComparer.Ordinal) { "prefixItems", "allOf" };

    private static void ScanForUnsupportedKeywords(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object) return;

        // Detecta keyword de schema unsupported NESTE nível.
        if (node.TryGetProperty("oneOf", out _))
            throw new DomainException(
                "GenericTool.OutputSchema usa 'oneOf', não suportado em OutputProjectionMode != Off — reescreva o schema com type/properties explícitos.");
        if (node.TryGetProperty("anyOf", out _))
            throw new DomainException(
                "GenericTool.OutputSchema usa 'anyOf', não suportado em OutputProjectionMode != Off — reescreva o schema com type/properties explícitos.");
        if (node.TryGetProperty("$ref", out _))
            throw new DomainException(
                "GenericTool.OutputSchema usa '$ref', não suportado em OutputProjectionMode != Off — inline o sub-schema referenciado pra evitar fetch externo em runtime.");

        // Descer apenas nos containers que reconhecidamente carregam sub-schema.
        foreach (var prop in node.EnumerateObject())
        {
            if (SchemaContainerKeywords.Contains(prop.Name))
            {
                if (prop.Value.ValueKind == JsonValueKind.Object)
                    ScanForUnsupportedKeywords(prop.Value);
            }
            else if (SchemaMapKeywords.Contains(prop.Name))
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var sub in prop.Value.EnumerateObject())
                    if (sub.Value.ValueKind == JsonValueKind.Object)
                        ScanForUnsupportedKeywords(sub.Value);
            }
            else if (SchemaArrayKeywords.Contains(prop.Name))
            {
                if (prop.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var sub in prop.Value.EnumerateArray())
                    if (sub.ValueKind == JsonValueKind.Object)
                        ScanForUnsupportedKeywords(sub);
            }
        }
    }

    private static bool IsEmptyOrTrivialObjectSchema(JsonElement root)
    {
        if (root.EnumerateObject().Any() == false) return true;

        var hasType = root.TryGetProperty("type", out var typeNode);
        var typeStr = hasType && typeNode.ValueKind == JsonValueKind.String ? typeNode.GetString() : null;

        // Arrays: aceita se tem `items` declarado.
        if (typeStr == "array")
            return !root.TryGetProperty("items", out _);

        // Object explícito sem properties: trivial.
        if (typeStr == "object" || typeStr is null)
        {
            if (!root.TryGetProperty("properties", out var props)
                || props.ValueKind != JsonValueKind.Object
                || !props.EnumerateObject().Any())
                return true;
        }

        return false;
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
