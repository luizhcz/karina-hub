using System.Text.Json;
using EfsAiHub.Core.Agents.GenericTools;
using Microsoft.Extensions.AI;

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
    private readonly GenericTool _tool;
    private readonly IGenericToolExecutor _executor;
    private readonly JsonElement _schema;
    private readonly string _description;

    public DynamicGenericAIFunction(GenericTool tool, IGenericToolExecutor executor)
    {
        _tool = tool;
        _executor = executor;
        _schema = GenericToolSchemaBuilder.Build(tool);
        _description = string.IsNullOrWhiteSpace(tool.Description)
            ? $"Generic HTTP tool '{tool.Name}' ({tool.HttpMethod})"
            : tool.Description;
    }

    public override string Name => _tool.Id;
    public override string Description => _description;
    public override JsonElement JsonSchema => _schema;

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
            return JsonSerializer.Serialize(result);
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
