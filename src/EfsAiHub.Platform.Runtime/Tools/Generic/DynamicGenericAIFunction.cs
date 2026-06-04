using System.Text.Json;
using System.Text.RegularExpressions;
using EfsAiHub.Core.Agents.GenericTools;
using Microsoft.Extensions.AI;
using EfsAiHub.Core.Abstractions.Persistence;

namespace EfsAiHub.Platform.Runtime.Tools.Generic;

/// <summary>
/// <see cref="AIFunction"/> dinâmica criada em runtime a partir de um
/// <see cref="GenericTool"/>. O LLM enxerga uma função única com schema unificado
/// (path + query + body merged); o invoke chama o <see cref="IGenericToolExecutor"/>
/// que monta o request HTTP correto. Toda falha do tool externo vira envelope
/// <see cref="ToolExecutionResult"/> serializado — agente recebe JSON e segue.
/// </summary>
public sealed class DynamicGenericAIFunction : AIFunction
{
    // Provedores (OpenAI, Anthropic) exigem nome batendo
    // ^[a-zA-Z0-9_-]{1,64}$ no schema da function. Names autorais devem ser
    // validados no save via GenericTool.EnsureInvariants; este regex serve de
    // defesa em profundidade pra rows herdadas (pré-validação) — substitui
    // qualquer char fora do conjunto por underscore antes de expor ao LLM.
    private static readonly Regex InvalidNameCharsRegex = new(@"[^a-zA-Z0-9_-]", RegexOptions.Compiled);

    private readonly GenericTool _tool;
    private readonly IGenericToolExecutor _executor;
    private readonly JsonElement _schema;
    private readonly string _description;
    private readonly string _name;

    public DynamicGenericAIFunction(GenericTool tool, IGenericToolExecutor executor)
    {
        _tool = tool;
        _executor = executor;
        _schema = GenericToolSchemaBuilder.Build(tool);
        _name = SanitizeName(tool.Name, fallback: tool.Id);
        // Description semântica vive no prompt do agente; aqui só o gist
        // técnico (nome + método) pro framework MEAI saber chamar.
        _description = $"Generic HTTP tool '{_name}' ({tool.HttpMethod})";
    }

    public override string Name => _name;
    public override string Description => _description;
    public override JsonElement JsonSchema => _schema;

    /// <summary>
    /// Sanitiza o nome pra bater na regex de function name dos provedores. Quando
    /// <paramref name="raw"/> está vazio ou vira string vazia após sanitização,
    /// cai pra <paramref name="fallback"/> (Id do tool — sempre UUID válido).
    /// Aplica truncamento defensivo a 64 chars.
    /// </summary>
    private static string SanitizeName(string raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return TruncateToLimit(fallback);
        var sanitized = InvalidNameCharsRegex.Replace(raw, "_");
        if (sanitized.Length > 0 && !char.IsLetter(sanitized[0]) && sanitized[0] != '_')
            sanitized = "_" + sanitized;
        return string.IsNullOrWhiteSpace(sanitized)
            ? TruncateToLimit(fallback)
            : TruncateToLimit(sanitized);
    }

    private static string TruncateToLimit(string s) => s.Length <= 64 ? s : s[..64];

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var args = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
            args[key] = value;

        try
        {
            var result = await _executor.ExecuteAsync(_tool, args, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonDefaults.Domain);
        }
        catch (ResponseSchemaViolationException ex)
        {
            // Sem isso o framework MEAI usaria ex.Message (texto humano) como
            // tool-result — quebra a promessa de "erro estruturado pro LLM".
            // Aqui serializamos o ToJson() já com {error, tool, details, hint}
            // que o LLM consegue decompor pra decidir próximo passo.
            return ex.ToJson();
        }
    }
}
