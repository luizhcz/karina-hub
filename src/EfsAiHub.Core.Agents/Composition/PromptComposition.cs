namespace EfsAiHub.Core.Agents.Composition;

/// <summary>
/// AsyncLocal ambient pro <see cref="PromptComposition"/> do turno corrente.
/// Setado pelo <c>LlmInvocationCaptureChatClient</c> quando captura está ON
/// (e null caso contrário — contribuidores fazem no-op). Contribuidores leem
/// via <see cref="Current"/> sem precisar receber o objeto por parâmetro.
///
/// Por que AsyncLocal estático em vez de propagar via ExecutionContext:
///   - ExecutionContext é positional record imutável; alterar a árvore de
///     construtores tocaria todo o pipeline.
///   - PromptComposition é opt-in (só ativa quando captura está ON);
///     AsyncLocal mantém zero alocação no caminho frio.
/// </summary>
public static class PromptCompositionAmbient
{
    private static readonly AsyncLocal<PromptComposition?> _current = new();

    public static PromptComposition? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    /// <summary>
    /// Helper pra contribuidores: chama <see cref="PromptComposition.Track"/>
    /// quando há composition ativa, no-op caso contrário. Mantém o callsite
    /// limpo (sem null-check no contributor).
    /// </summary>
    public static void Track(
        string source,
        string contributorType,
        int? messageIndex = null,
        int? charOffset = null,
        int? charLength = null,
        string? note = null)
    {
        _current.Value?.Track(source, contributorType, messageIndex, charOffset, charLength, note);
    }
}

/// <summary>
/// Acumulador de "quem injetou cada bloco" no prompt final enviado ao LLM.
/// Vive no <c>ExecutionContext</c> (propagado por AsyncLocal) e cada
/// contribuidor (SystemMessageBuilder, ChatOptionsBuilder, persona composer,
/// OperationalMemoryChatClient, AgentFactory) chama <see cref="Track"/>
/// quando adiciona seu pedaço. Capturado posteriormente pelo
/// <c>LlmInvocationCaptureChatClient</c> e persistido em
/// <c>aihub.llm_invocation_log.Composition</c>.
///
/// <para>
/// Modos de marcação (sem mistura por section):
/// </para>
/// <list type="bullet">
///   <item><b>Per-section (char range)</b>: contribuidores que concatenam
///   texto num system message único informam <c>CharOffset</c> + <c>CharLength</c>
///   pra UI fazer highlight colorido. Usado por agent.instructions,
///   agent.routerIntents, agent.workerScope, persona.system,
///   persona.userReinforcement.</item>
///   <item><b>Per-message (whole ChatMessage)</b>: contribuidores que
///   INSEREM uma <c>ChatMessage</c> inteira (system/user/assistant/tool)
///   informam <c>MessageIndex</c>. Usado por operationalMemory.preamble,
///   operationalMemory.state, history.*, input.user.</item>
/// </list>
///
/// <para>
/// Thread-safe — middlewares podem rodar em ordem assíncrona dentro do mesmo
/// turno e a captura final lê snapshot consistente.
/// </para>
/// </summary>
public sealed class PromptComposition
{
    private readonly List<PromptSection> _sections = new();
    private readonly Lock _lock = new();

    public IReadOnlyList<PromptSection> Sections
    {
        get
        {
            lock (_lock)
            {
                return _sections.ToArray();
            }
        }
    }

    public void Track(
        string source,
        string contributorType,
        int? messageIndex = null,
        int? charOffset = null,
        int? charLength = null,
        string? note = null)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(contributorType))
            return;

        lock (_lock)
        {
            _sections.Add(new PromptSection(
                Source: source,
                ContributorType: contributorType,
                MessageIndex: messageIndex,
                CharOffset: charOffset,
                CharLength: charLength,
                Note: note));
        }
    }
}

/// <summary>
/// Marcação individual de uma seção do prompt — pode ser uma faixa de chars
/// dentro de um system message concatenado ou uma <c>ChatMessage</c> inteira
/// inserida no array <c>messages</c>.
/// </summary>
/// <param name="Source">
/// Identificador semântico curto do contribuidor. Convenções:
/// <c>agent.instructions</c>, <c>agent.routerIntents</c>,
/// <c>agent.workerScope</c>, <c>persona.system</c>,
/// <c>persona.userReinforcement</c>, <c>operationalMemory.preamble</c>,
/// <c>operationalMemory.state</c>, <c>history.user</c>, <c>history.assistant</c>,
/// <c>input.user</c>.
/// </param>
/// <param name="ContributorType">Tipo C# do componente que tracou — ajuda
/// debug quando a mesma <c>Source</c> tem múltiplos contribuidores potenciais.</param>
/// <param name="MessageIndex">Index 0-based no array <c>messages</c> enviado
/// ao LLM. Null quando a marcação é por char range dentro de um message.</param>
/// <param name="CharOffset">Offset 0-based dentro do texto do message.
/// Null quando a marcação é o message inteiro.</param>
/// <param name="CharLength">Tamanho da faixa em chars. Null quando a marcação
/// é o message inteiro.</param>
/// <param name="Note">Texto curto opcional que aparece no hover da UI —
/// ex: "5 intents resolvidas", "memory v=3", "history truncado em 5".</param>
public sealed record PromptSection(
    string Source,
    string ContributorType,
    int? MessageIndex,
    int? CharOffset,
    int? CharLength,
    string? Note);
