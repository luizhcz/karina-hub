using System.Text.Json;
using EfsAiHub.Host.Api.Chat.AgUi.Models;

namespace EfsAiHub.Host.Api.Chat.AgUi.Handlers;

/// <summary>
/// Frontend tools permitem que o agente peça para o frontend
/// executar uma ação — ex: navegar para tela, abrir modal, preencher formulário.
/// O agente não controla o DOM diretamente. Ele solicita, o frontend decide se executa.
///
/// Registra frontend tools declaradas pelo cliente como AITool-like stubs
/// que retornam um sinal "_frontendAction" para o frontend interpretar.
/// </summary>
public sealed class AgUiFrontendToolHandler
{
    /// <summary>
    /// Converte frontend tools declaradas pelo cliente em funções stub
    /// que retornam um payload com _frontendAction = true.
    /// Retorna um dicionário nome → description para injetar no prompt do agente.
    /// </summary>
    public IReadOnlyList<FrontendToolStub> RegisterFrontendTools(
        AgUiFrontendTool[]? frontendTools)
    {
        if (frontendTools is null || frontendTools.Length == 0)
            return [];

        return frontendTools.Select(ft => new FrontendToolStub(
            Name: $"frontend_{ft.Name}",
            Description: $"[Frontend tool] {ft.Description}",
            ParametersSchema: ft.Parameters,
            OriginalName: ft.Name
        )).ToList();
    }

    /// <summary>
    /// Gera o resultado de uma frontend tool call — sinal para o frontend.
    /// </summary>
    public static string CreateFrontendActionResult(string toolName, string argsJson)
    {
        return JsonSerializer.Serialize(new
        {
            _frontendAction = true,
            tool = toolName,
            args = JsonSerializer.Deserialize<JsonElement>(argsJson)
        });
    }
}

/// <summary>
/// Stub representando uma frontend tool registrada pelo cliente.
/// </summary>
public sealed record FrontendToolStub(
    string Name,
    string Description,
    JsonElement? ParametersSchema,
    string OriginalName);
