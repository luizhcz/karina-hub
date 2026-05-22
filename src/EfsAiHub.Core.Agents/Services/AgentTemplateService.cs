using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Core.Agents.Services;

/// <summary>
/// Aplica templates determinísticos por <see cref="AgentType"/> sobre uma
/// <see cref="AgentDefinition"/> recém-materializada do payload, antes da
/// validação e persistência. O serviço é a fonte da verdade pros campos
/// auto-gerados por tipo — frontends e callers genéricos não precisam
/// conhecer o shape canônico exigido por cada tipo.
/// </summary>
public interface IAgentTemplateService
{
    /// <summary>
    /// Retorna uma cópia da definição com os campos auto-gerados do tipo
    /// preenchidos. Idempotente: chamar duas vezes seguidas produz o mesmo
    /// resultado, e payloads já normalizados (wrap legado, middleware
    /// duplicado, bloco no prompt) são detectados e tratados sem duplicar.
    /// Para tipos sem template registrado (Custom, etc.) retorna a definição
    /// inalterada.
    /// </summary>
    AgentDefinition Apply(AgentDefinition definition);
}

/// <summary>
/// Implementação pura, stateless. Seguro como singleton — nenhum cache nem
/// state mutável; cada <see cref="Apply"/> é função do input. Logger
/// injetado serve apenas pra alertar sobre detecções de schemas legados
/// que merecem revisão manual; não muda comportamento.
/// </summary>
public sealed class AgentTemplateService : IAgentTemplateService
{
    private readonly ILogger<AgentTemplateService> _logger;

    public AgentTemplateService(ILogger<AgentTemplateService> logger)
    {
        _logger = logger;
    }

    // Identificadores fixos do template Conversational. Mantidos como const
    // pra que mudanças no shape canônico fiquem centralizadas neste arquivo
    // — qualquer caller que monte schema manualmente referencia daqui.
    private const string ConversationalSchemaName = "ConversationalTurn";

    private const string ConversationalSchemaDefaultDescription =
        "Resposta canônica do Conversational: output_type (renderer family), output_status (variação), message (texto), output (payload).";

    private const string OutputTypeDescription =
        "Família de renderer que o frontend deve usar pra esta resposta. Valor único definido pelo agente.";

    private const string OutputStatusDescription =
        "Variação de status dentro do output_type. Valor escolhido entre as opções configuradas pelo agente.";

    private const string MessageDescription =
        "Texto humano em PT-BR pro usuário — curto, claro, direto.";

    // Defaults aplicados quando o agente Conversational não tem as metadata
    // keys configuradas (basic mode no MVP ou agente seedado sem config).
    private const string DefaultOutputType = "text";
    private static readonly IReadOnlyList<string> DefaultOutputStatuses = new[] { "default" };

    private const string ResponseFormatBlockHeader = "## Formato da resposta";

    // Bloco anexado ao final das instructions do Conversational quando ainda
    // não está presente. Texto idempotente — detectado via regex sobre o
    // header pra evitar duplicação em re-saves.
    private const string ResponseFormatBlockBody =
        "Responda SEMPRE em JSON com os campos top-level definidos no schema. " +
        "Não escreva texto fora do JSON; não invente campos top-level extras.";

    private const string StructuredOutputStateMiddlewareType = "StructuredOutputState";

    public AgentDefinition Apply(AgentDefinition definition)
    {
        // Invariante de visibility: Conversational e Router são sempre globais
        // por design. Aplicado aqui (e não só em AgentService.CreateAsync) pra
        // cobrir TODOS os caminhos de publicação — inclusive ApproveAsync do
        // draft, que persiste via IAgentDefinitionRepository sem passar pelo
        // service. Idempotente: re-aplicar não muda nada.
        if (definition.Type is AgentType.Conversational or AgentType.Router)
            definition.Visibility = "global";

        return definition.Type switch
        {
            AgentType.Conversational => ApplyConversational(definition),
            AgentType.Router => ApplyRouter(definition),
            _ => definition,
        };
    }

    /// <summary>
    /// Auto-defaults canônicos do Router: <c>StructuredOutput</c> com schema
    /// <c>{ intent, confidence, reason, operationalMemory }</c> e
    /// <c>OperationalMemory</c> com schema <c>{ last_intent, last_reason }</c>.
    /// Aplicado apenas quando o admin não cadastrou schema próprio — preserva
    /// customização. Idempotente: re-aplicar com canônico já presente não muda
    /// nada (o template detecta presença de "intent" no top-level).
    /// </summary>
    private AgentDefinition ApplyRouter(AgentDefinition def)
    {
        var structuredOutput = def.StructuredOutput;
        var hasCanonicalSchema = HasRouterCanonicalSchema(structuredOutput);
        if (!hasCanonicalSchema)
            structuredOutput = EfsAiHub.Core.Agents.RouterIntents.RouterDefaults.OutputSchema();

        var operationalMemory = def.OperationalMemory
            ?? EfsAiHub.Core.Agents.RouterIntents.RouterDefaults.OperationalMemoryV1();

        return new AgentDefinition
        {
            Id = def.Id,
            Name = def.Name,
            Description = def.Description,
            Type = def.Type,
            RouterIntentIds = def.RouterIntentIds,
            Model = def.Model,
            Provider = def.Provider,
            AuthorInstructions = def.AuthorInstructions,
            Instructions = def.Instructions,
            Tools = def.Tools,
            StructuredOutput = structuredOutput,
            OperationalMemory = operationalMemory,
            Middlewares = def.Middlewares,
            FallbackProvider = def.FallbackProvider,
            Resilience = def.Resilience,
            CostBudget = def.CostBudget,
            SkillRefs = def.SkillRefs,
            Metadata = def.Metadata,
            Visibility = def.Visibility,
            AllowedProjectIds = def.AllowedProjectIds,
            Enabled = def.Enabled,
            ProjectId = def.ProjectId,
            TenantId = def.TenantId,
            CreatedAt = def.CreatedAt,
            UpdatedAt = def.UpdatedAt,
            RegressionTestSetId = def.RegressionTestSetId,
            RegressionEvaluatorConfigVersionId = def.RegressionEvaluatorConfigVersionId,
        };
    }

    private static bool HasRouterCanonicalSchema(AgentStructuredOutputDefinition? structured)
    {
        if (structured?.Schema is null) return false;
        try
        {
            var root = structured.Schema.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("properties", out var props)) return false;
            // Marker mínimo: o canônico exige "intent" no top-level + "operationalMemory".
            // Se admin renomeou ou usou shape diferente, deixamos como está.
            return props.TryGetProperty("intent", out _)
                && props.TryGetProperty("operationalMemory", out _);
        }
        catch
        {
            return false;
        }
    }

    private AgentDefinition ApplyConversational(AgentDefinition def)
    {
        var outputSubSchema = ExtractOutputSubSchema(def.Id, def.StructuredOutput?.Schema);
        var outputType = ReadOutputType(def.Metadata);
        var outputStatuses = ReadOutputStatuses(def.Metadata);

        var canonicalSchema = BuildCanonicalSchema(outputType, outputStatuses, outputSubSchema);
        var structuredOutput = new AgentStructuredOutputDefinition
        {
            ResponseFormat = "json_schema",
            SchemaName = ConversationalSchemaName,
            SchemaDescription =
                string.IsNullOrWhiteSpace(def.StructuredOutput?.SchemaDescription)
                    ? ConversationalSchemaDefaultDescription
                    : def.StructuredOutput!.SchemaDescription,
            Schema = JsonDocumentFromNode(canonicalSchema),
        };

        var instructions = EnsureResponseFormatBlock(def.Instructions);
        var middlewares = EnsureStructuredOutputStateMiddleware(def.Middlewares);

        return CopyWith(def, instructions, structuredOutput, middlewares);
    }

    // Desempacota schemas que já chegaram no shape canônico
    // (re-saves sucessivos). Reconhece tanto o shape novo
    // `{output_type, output_status, message, output?}` quanto o legado
    // `{ui_component, message, output?}` pra que agentes pré-migration
    // continuem round-trippando até o backfill rodar.
    //
    // CONTRATO IMPORTANTE: agentes com Type=Conversational mas schema custom
    // sem nenhuma das chaves canônicas em properties (ex.: agentes seedados
    // direto via SQL, importados, ou tipo mal-atribuído) terão o documento
    // INTEIRO tratado como sub-schema do user — o template wrappa em
    // `{output_type, output_status, message, output: <schema antigo>}` e a
    // semântica original do agente é alterada silenciosamente. Pra esses
    // casos use Type=Custom no seed/import; um warning é emitido em runtime.
    private JsonNode? ExtractOutputSubSchema(string agentId, JsonDocument? schema)
    {
        if (schema is null) return null;
        var root = schema.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("properties", out var props)
            || props.ValueKind != JsonValueKind.Object)
        {
            // Schema sem properties: trata o documento inteiro como sub-schema
            // do user. Permite caller enviar shape simples sem o wrapper.
            return CloneAsNode(root);
        }

        var hasMessage = props.TryGetProperty("message", out _);
        var hasNewShape = props.TryGetProperty("output_type", out _)
            && props.TryGetProperty("output_status", out _)
            && hasMessage;
        var hasLegacyShape = props.TryGetProperty("ui_component", out _) && hasMessage;

        if (hasNewShape || hasLegacyShape)
        {
            // Wrap canônico reconhecido (novo ou legado). `output` ausente =
            // texto livre; presente = sub-schema do user, desempacotamos.
            return props.TryGetProperty("output", out var output)
                ? CloneAsNode(output)
                : null;
        }

        // Schema custom não bate nenhum shape canônico — Conversational com
        // schema arbitrário é caso conhecido de tipo mal-atribuído. Emite
        // warning pra que o operador revise.
        _logger.LogWarning(
            "[AgentTemplate] Agente '{AgentId}' (Conversational) tem schema custom sem 'output_type'/'output_status'/'message' " +
            "em properties — documento inteiro será wrappado como sub-schema do user. " +
            "Se o contrato esperado é diferente, use Type=Custom.",
            agentId);

        // Documento inteiro é o sub-schema do user.
        return CloneAsNode(root);
    }

    private static JsonObject BuildCanonicalSchema(
        string outputType,
        IReadOnlyList<string> outputStatuses,
        JsonNode? outputSubSchema)
    {
        var properties = new JsonObject
        {
            ["output_type"] = BuildEnumStringProperty(OutputTypeDescription, new[] { outputType }),
            ["output_status"] = BuildEnumStringProperty(OutputStatusDescription, outputStatuses),
            ["message"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = MessageDescription,
            },
        };

        var required = new JsonArray { "output_type", "output_status", "message" };
        if (outputSubSchema is not null)
        {
            properties["output"] = outputSubSchema;
            required.Add("output");
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false,
        };
    }

    private static JsonObject BuildEnumStringProperty(string description, IReadOnlyList<string> values)
    {
        var prop = new JsonObject
        {
            ["type"] = "string",
            ["description"] = description,
        };
        if (values.Count > 0)
        {
            var enumArray = new JsonArray();
            foreach (var v in values) enumArray.Add(v);
            prop["enum"] = enumArray;
        }
        return prop;
    }

    // Lê o output_type do metadata. Default "text" quando ausente, vazio ou
    // configurado com whitespace.
    private static string ReadOutputType(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null) return DefaultOutputType;
        if (!metadata.TryGetValue(AgentDefinition.ConversationalOutputTypeMetadataKey, out var raw))
            return DefaultOutputType;
        var trimmed = (raw ?? string.Empty).Trim();
        return trimmed.Length == 0 ? DefaultOutputType : trimmed;
    }

    // Lê a lista de output_statuses do metadata. Fallback pra chave legada
    // (x-conversational-ui-components) enquanto a migration 011 não rodar em
    // todos os ambientes. Lista vazia/inválida vira o default ["default"].
    private static IReadOnlyList<string> ReadOutputStatuses(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null) return DefaultOutputStatuses;
        var raw = metadata.TryGetValue(AgentDefinition.ConversationalOutputStatusesMetadataKey, out var primary)
            ? primary
#pragma warning disable CS0618 // Type or member is obsolete — leitura legacy intencional pra BC.
            : metadata.TryGetValue(AgentDefinition.ConversationalUiComponentsMetadataKey, out var legacy)
                ? legacy
                : null;
#pragma warning restore CS0618
        if (string.IsNullOrWhiteSpace(raw)) return DefaultOutputStatuses;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return DefaultOutputStatuses;
            var list = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
            }
            return list.Count == 0 ? DefaultOutputStatuses : list;
        }
        catch (JsonException)
        {
            return DefaultOutputStatuses;
        }
    }

    private static string EnsureResponseFormatBlock(string? instructions)
    {
        var current = instructions ?? string.Empty;
        // Presence check é case-sensitive — backend grava sempre o header
        // canônico em PT-BR exato. Variações ortográficas dão match falso-negativo
        // e o bloco é re-anexado, o que é aceitável (round-trip subsequente
        // converge).
        if (current.Contains(ResponseFormatBlockHeader, StringComparison.Ordinal))
        {
            return current;
        }

        var sep = string.IsNullOrWhiteSpace(current) ? string.Empty : "\n\n";
        return $"{current.TrimEnd()}{sep}{ResponseFormatBlockHeader}\n{ResponseFormatBlockBody}";
    }

    private static IReadOnlyList<AgentMiddlewareConfig> EnsureStructuredOutputStateMiddleware(
        IReadOnlyList<AgentMiddlewareConfig> middlewares)
    {
        // Entry existente com Enabled=true preserva tudo (settings, etc.).
        // Entry com Enabled=false é "promovida" pra true — Conversational
        // depende do middleware pra emitir STATE_DELTA; entry inert quebra
        // o chat sem warning visível. Caller que precise desativar tem que
        // remover o entry inteiro (sem entry, template re-injeta).
        var copy = new List<AgentMiddlewareConfig>(middlewares.Count + 1);
        var injected = false;
        foreach (var m in middlewares)
        {
            var isTarget = string.Equals(
                m.Type, StructuredOutputStateMiddlewareType, StringComparison.OrdinalIgnoreCase);
            if (isTarget && injected)
            {
                // Payload malformado com duplicata: descarta extras pra
                // garantir entry única no array final.
                continue;
            }
            if (isTarget)
            {
                if (m.Enabled)
                {
                    copy.Add(m);
                }
                else
                {
                    copy.Add(new AgentMiddlewareConfig
                    {
                        Type = m.Type,
                        Enabled = true,
                        Settings = m.Settings,
                    });
                }
                injected = true;
            }
            else
            {
                copy.Add(m);
            }
        }

        if (!injected)
        {
            copy.Add(new AgentMiddlewareConfig
            {
                Type = StructuredOutputStateMiddlewareType,
                Enabled = true,
                Settings = new Dictionary<string, string>(),
            });
        }
        return copy;
    }

    // Cópia campo a campo — AgentDefinition é classe init-only sem `with`.
    // Concentrado neste helper pra que adições de campo no domain sejam
    // detectadas como compile errors aqui.
    private static AgentDefinition CopyWith(
        AgentDefinition source,
        string? instructions,
        AgentStructuredOutputDefinition? structuredOutput,
        IReadOnlyList<AgentMiddlewareConfig> middlewares)
    {
        return new AgentDefinition
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            Type = source.Type,
            RouterIntentIds = source.RouterIntentIds,
            Model = source.Model,
            Provider = source.Provider,
            Instructions = instructions,
            Tools = source.Tools,
            StructuredOutput = structuredOutput,
            OperationalMemory = source.OperationalMemory,
            Middlewares = middlewares,
            FallbackProvider = source.FallbackProvider,
            Resilience = source.Resilience,
            CostBudget = source.CostBudget,
            SkillRefs = source.SkillRefs,
            Metadata = source.Metadata,
            Visibility = source.Visibility,
            AllowedProjectIds = source.AllowedProjectIds,
            Enabled = source.Enabled,
            ProjectId = source.ProjectId,
            TenantId = source.TenantId,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            RegressionTestSetId = source.RegressionTestSetId,
            RegressionEvaluatorConfigVersionId = source.RegressionEvaluatorConfigVersionId,
        };
    }

    private static JsonDocument JsonDocumentFromNode(JsonNode node)
    {
        return JsonDocument.Parse(node.ToJsonString());
    }

    private static JsonNode? CloneAsNode(JsonElement element)
    {
        // JsonNode.Parse aceita string JSON; serializamos via raw text pra
        // preservar todos os tipos (number/string/bool/null/array/object).
        return JsonNode.Parse(element.GetRawText());
    }
}
