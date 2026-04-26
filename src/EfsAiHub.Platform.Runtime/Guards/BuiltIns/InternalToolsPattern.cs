using EfsAiHub.Core.Agents.Interfaces;

namespace EfsAiHub.Platform.Runtime.Guards.BuiltIns;

/// <summary>
/// Built-in <c>internal_tools</c>: lista os nomes de todas as tools registradas no
/// IFunctionToolRegistry. Bloqueia o LLM de vazar nomes de funções internas
/// (ex: "executei get_client_position…") nas respostas.
///
/// Snapshot é resolvido a cada chamada — registry é populado no startup, mudanças
/// em runtime (raras) refletem na próxima recompilação do matcher.
/// </summary>
public sealed class InternalToolsPattern : IBuiltInPatternHandler
{
    private readonly IFunctionToolRegistry _registry;

    public string Id => "internal_tools";

    public IReadOnlyCollection<string> Literals => _registry.GetAll().Keys.ToArray();

    public InternalToolsPattern(IFunctionToolRegistry registry)
    {
        _registry = registry;
    }
}
