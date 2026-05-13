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
        "Resposta canônica do Conversational: ui_component (renderer), message (texto), output (payload).";

    private const string UiComponentDescription =
        "Identificador do componente UI que o frontend deve renderizar pra esta resposta.";

    private const string MessageDescription =
        "Texto humano em PT-BR pro usuário — curto, claro, direto.";

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
        return definition.Type switch
        {
            AgentType.Conversational => ApplyConversational(definition),
            _ => definition,
        };
    }

    private AgentDefinition ApplyConversational(AgentDefinition def)
    {
        var outputSubSchema = ExtractOutputSubSchema(def.Id, def.StructuredOutput?.Schema);
        var uiComponents = ReadUiComponents(def.Metadata);

        var canonicalSchema = BuildCanonicalSchema(uiComponents, outputSubSchema);
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
    // (ex.: drafts persistidos antes da centralização do template ou re-saves
    // sucessivos). Quando o schema carrega `properties.{ui_component, message}`
    // assumimos que o sub-schema do user vive em `properties.output`; sua
    // ausência indica modo texto livre (sem campo `output`) e devolve null.
    //
    // CONTRATO IMPORTANTE: agentes com Type=Conversational mas schema custom
    // sem `ui_component`/`message` em properties (ex.: agentes seedados
    // direto via SQL, importados, ou tipo mal-atribuído) terão o documento
    // INTEIRO tratado como sub-schema do user — o template wrappa em
    // `{ui_component, message, output: <schema antigo>}` e a semântica
    // original do agente é alterada silenciosamente. Pra esses casos use
    // Type=Custom no seed/import; um warning é emitido em runtime pra
    // facilitar diagnóstico.
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

        var hasUiComponent = props.TryGetProperty("ui_component", out _);
        var hasMessage = props.TryGetProperty("message", out _);
        if (hasUiComponent && hasMessage)
        {
            // Wrap canônico reconhecido. `output` ausente = texto livre legado;
            // presente = sub-schema do user, desempacotamos.
            return props.TryGetProperty("output", out var output)
                ? CloneAsNode(output)
                : null;
        }

        // Schema custom não bate o shape canônico — Conversational com schema
        // arbitrário é caso conhecido de tipo mal-atribuído. Emite warning
        // pra que o operador revise (em prod, aparece no log assim que o
        // agente passar por Update/Approve).
        _logger.LogWarning(
            "[AgentTemplate] Agente '{AgentId}' (Conversational) tem schema custom sem 'ui_component'/'message' " +
            "em properties — documento inteiro será wrappado como sub-schema do user. " +
            "Se o contrato esperado é diferente, use Type=Custom.",
            agentId);

        // Documento inteiro é o sub-schema do user.
        return CloneAsNode(root);
    }

    private static JsonObject BuildCanonicalSchema(
        IReadOnlyList<string> uiComponents,
        JsonNode? outputSubSchema)
    {
        var properties = new JsonObject
        {
            ["ui_component"] = BuildUiComponentProperty(uiComponents),
            ["message"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = MessageDescription,
            },
        };

        var required = new JsonArray { "ui_component", "message" };
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

    private static JsonObject BuildUiComponentProperty(IReadOnlyList<string> uiComponents)
    {
        var prop = new JsonObject
        {
            ["type"] = "string",
            ["description"] = UiComponentDescription,
        };
        if (uiComponents.Count > 0)
        {
            var enumArray = new JsonArray();
            foreach (var value in uiComponents)
            {
                enumArray.Add(value);
            }
            prop["enum"] = enumArray;
        }
        return prop;
    }

    // Lê e sanitiza a lista de ui_components do metadata. JSON inválido ou
    // não-array vira lista vazia (caller emite enum sem restrição); items
    // não-string ou em branco são descartados.
    private static IReadOnlyList<string> ReadUiComponents(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null) return Array.Empty<string>();
        if (!metadata.TryGetValue(AgentDefinition.ConversationalUiComponentsMetadataKey, out var raw))
        {
            return Array.Empty<string>();
        }
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
            }
            return list;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
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
