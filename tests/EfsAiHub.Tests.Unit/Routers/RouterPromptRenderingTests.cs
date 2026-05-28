using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.RouterIntents;
using EfsAiHub.Core.Agents.Services;

namespace EfsAiHub.Tests.Unit.Routers;

/// <summary>
/// Cobre PR 2 do plano de ambiguidade: o PromptRenderer emite a hierarquia de
/// saídas (intent específica → needs_clarification → out_of_scope), o bloco
/// <c>&lt;ambiguity_handling&gt;</c> com regra de dominância (gap 0.25 / top
/// 0.6), o caso especial de clarification follow-up em
/// <c>&lt;multi_turn_classification&gt;</c> e o loop guard de
/// <c>clarification_depth</c> no <c>&lt;memory_rules&gt;</c>.
///
/// Testes são string-matching deliberado: o prompt é contrato com o LLM, e
/// regressões aqui silenciariam o comportamento de ambiguidade inteiro.
/// </summary>
[Trait("Category", "Unit")]
public class RouterPromptRenderingTests
{
    private static IReadOnlyList<RouterIntent> BuildIntents() => new[]
    {
        new RouterIntent
        {
            Id = "biz-investir-rf",
            TenantId = "default",
            ProjectId = "default",
            Name = "investir_renda_fixa",
            Description = "Aplicar em renda fixa (CDB, LCI, Tesouro Direto).",
            Examples = new[] { "quero investir em CDB", "aplicar em renda fixa" },
        },
        new RouterIntent
        {
            Id = "biz-investir-rv",
            TenantId = "default",
            ProjectId = "default",
            Name = "investir_renda_variavel",
            Description = "Aplicar em renda variável (ações, fundos, ETFs).",
            Examples = new[] { "quero comprar ações" },
        },
        new RouterIntent
        {
            Id = "sys-oos-default",
            TenantId = "default",
            ProjectId = "default",
            Name = SystemIntents.OutOfScopeName,
            Description = "Fora do escopo declarado.",
            IsSystem = true,
        },
        new RouterIntent
        {
            Id = "sys-nc-default",
            TenantId = "default",
            ProjectId = "default",
            Name = SystemIntents.NeedsClarificationName,
            Description = "Mensagem ambígua dentro do produto.",
            IsSystem = true,
        },
    };

    private static string RenderRouter() =>
        PromptRenderer.Render(
            AgentType.Router,
            authorInstructions: "Você é um classificador.",
            metadata: null,
            routerIntents: BuildIntents(),
            skills: null,
            hasOperationalMemory: true)!;

    [Fact]
    public void Render_Router_EmiteBlocosEsperadosNaOrdemCanonica()
    {
        var prompt = RenderRouter();

        // Busca por tag estruturada (com newlines em volta) pra não casar
        // menções textuais dentro de outros blocos. ORDEM ATUAL — multi_turn
        // antes de ambiguity_handling pra que continuação seja a primeira
        // pergunta a fazer (primacy). Sem essa ordem, mensagens de resposta
        // curta a perguntas do assistant viravam needs_clarification.
        var intentsIdx = prompt.IndexOf("\n<intents>\n", StringComparison.Ordinal);
        var multiTurnIdx = prompt.IndexOf("\n<multi_turn_classification>\n", StringComparison.Ordinal);
        var ambiguityIdx = prompt.IndexOf("\n<ambiguity_handling>\n", StringComparison.Ordinal);
        var memoryIdx = prompt.IndexOf("\n<memory_rules>\n", StringComparison.Ordinal);

        intentsIdx.Should().BePositive("bloco <intents> precisa estar presente");
        multiTurnIdx.Should().BeGreaterThan(intentsIdx,
            "<multi_turn_classification> vem ANTES de <ambiguity_handling> (primacy: continuação prevalece)");
        ambiguityIdx.Should().BeGreaterThan(multiTurnIdx,
            "<ambiguity_handling> vem depois de <multi_turn_classification>");
        memoryIdx.Should().BeGreaterThan(ambiguityIdx,
            "<memory_rules> vem por último (recency: regra de copy fica perto do output)");
    }

    [Fact]
    public void Render_Router_IntentsBlock_DocumentaHierarquiaTresNiveis()
    {
        var prompt = RenderRouter();

        // Hierarquia: intent de negócio → needs_clarification → out_of_scope.
        // Cada nível precisa ser citado pra LLM saber escolher.
        prompt.Should().Contain("Hierarquia de decisão");
        prompt.Should().Contain(SystemIntents.NeedsClarificationName);
        prompt.Should().Contain(SystemIntents.OutOfScopeName);
    }

    [Fact]
    public void Render_Router_AmbiguityHandling_DocumentaRegraDeDominancia()
    {
        var prompt = RenderRouter();

        // Thresholds 0.6 e 0.25 são a decisão de produto. Se mudarem, o teste
        // falha — força revisão consciente. Strings exatas pra evitar
        // regressão silenciosa (ex: alguém troca 0.25 por 0.2).
        prompt.Should().Contain("top1.confidence >= 0.6");
        prompt.Should().Contain("(top1.confidence - top2.confidence) >= 0.25");
        prompt.Should().Contain("candidate_intents");
    }

    [Fact]
    public void Render_Router_AmbiguityHandling_ListaPadroesTipicosDeAmbiguidade()
    {
        var prompt = RenderRouter();

        // Few-shot textual: padrões que o LLM deve reconhecer como ambíguos.
        // Não passamos exemplos JSON literais (LLMs decoram demais), mas sim
        // descrições do padrão.
        prompt.Should().Contain("Verbo genérico do domínio sem qualificação");
        prompt.Should().Contain("investir");
        prompt.Should().Contain("transferir");
    }

    [Fact]
    public void Render_Router_MultiTurn_DocumentaContinuationDeClarification()
    {
        var prompt = RenderRouter();

        // Quando last_intent == needs_clarification, próxima mensagem resolve.
        // Sem essa regra explícita, o LLM costuma repetir needs_clarification.
        prompt.Should().Contain("Caso especial");
        prompt.Should().Contain($"`last_intent` == `{SystemIntents.NeedsClarificationName}`");
        prompt.Should().Contain("resolve a ambiguidade");
    }

    [Fact]
    public void Render_Router_MultiTurn_DocumentaRegraPrimariaDeAnaliseConjunta()
    {
        var prompt = RenderRouter();

        // Regra primária: o LLM SEMPRE precisa avaliar mensagem atual + histórico
        // + operationalMemory injetado em CONJUNTO antes de classificar. Sem essa
        // âncora, mensagens curtas de resposta (ex: "12345" depois de "qual conta?")
        // eram classificadas como needs_clarification por causa do bias do
        // <ambiguity_handling>.
        prompt.Should().Contain("REGRA PRIMÁRIA");
        prompt.Should().Contain("EM CONJUNTO");
        prompt.Should().Contain("CONTINUAÇÃO vs NOVO ASSUNTO antes");
    }

    [Fact]
    public void Render_Router_MultiTurn_DocumentaRegraDuraDeRespostaCurta()
    {
        var prompt = RenderRouter();

        // Regra dura — fix do bug de produção (PR pós-revisão do Clarifier):
        // "conta 12345" como resposta direta a "qual conta?" do assistant DEVE
        // ser continuação do last_intent. Sem essa regra explícita o LLM achava
        // que a resposta curta era ambígua e caía em needs_clarification.
        prompt.Should().Contain("REGRA DURA");
        prompt.Should().Contain("resposta curta a pergunta do assistant");
        prompt.Should().Contain($"JAMAIS use `{SystemIntents.NeedsClarificationName}`");
        prompt.Should().Contain("Continue o intent anterior");
    }

    [Fact]
    public void Render_Router_AmbiguityHandling_TemPreRequisitoDeMultiTurnPrimeiro()
    {
        var prompt = RenderRouter();

        // Guard: se a mensagem é continuação (resolvida em <multi_turn>), o LLM
        // deve IGNORAR <ambiguity_handling>. Sem isso, mesmo com a ordem
        // invertida o LLM tenta aplicar a regra de dominância em mensagens já
        // decididas como continuação.
        prompt.Should().Contain("PRÉ-REQUISITO");
        prompt.Should().Contain("ignore este bloco inteiro");
    }

    [Fact]
    public void Render_Router_OperationalMemory_DocumentaClarificationDepthELoopGuard()
    {
        var prompt = RenderRouter();

        // Loop guard agora é server-enforced (RouterDecisionTelemetryChatClient
        // como pre-memory middleware). Prompt explicita a regra como reforço
        // pro LLM ainda assim preferir não emitir needs_clarification em
        // sequência. Mensagem "loop guard triggered" é o reason canônico que o
        // servidor cita ao reescrever.
        prompt.Should().Contain("clarification_depth");
        prompt.Should().Contain("Loop guard");
        prompt.Should().Contain("loop guard triggered");
    }

    [Fact]
    public void Render_Router_OperationalMemory_DocumentaLastCandidateIntents()
    {
        var prompt = RenderRouter();

        // last_candidate_intents é o que permite ao Router resolver clarification
        // follow-up no turno seguinte: sem isso, o LLM precisaria inferir as
        // candidatas do texto do Clarifier no histórico — frágil.
        prompt.Should().Contain("last_candidate_intents");
        prompt.Should().Contain("nomes exatos do enum");
    }

    [Fact]
    public void Render_Router_OperationalMemory_DocumentaResetCondicional()
    {
        var prompt = RenderRouter();

        // Regra de reset: depth = 0 quando intent != needs_clarification.
        // Sem isso, o contador vaza pra turnos não-relacionados e dispara o
        // loop guard fora de contexto.
        prompt.Should().Contain($"intent` != `{SystemIntents.NeedsClarificationName}`");
        prompt.Should().Contain("zere para 0");
    }

    [Fact]
    public void Render_NonRouter_NaoEmiteBlocosDeRouter()
    {
        // Sanity check: blocos de Router só aparecem pra AgentType.Router.
        // Sem isso, Worker/Conversational/ToolRunner viriam poluídos.
        var prompt = PromptRenderer.Render(
            AgentType.Worker,
            authorInstructions: "Worker",
            metadata: new Dictionary<string, string>
            {
                [AgentDefinition.WorkerScopeMetadataKey] = "análise de risco",
            },
            routerIntents: BuildIntents(),
            skills: null,
            hasOperationalMemory: false);

        prompt.Should().NotBeNull();
        prompt!.Should().NotContain("<intents>");
        prompt.Should().NotContain("<ambiguity_handling>");
        prompt.Should().NotContain("<multi_turn_classification>");
    }

    [Fact]
    public void Render_Router_SemIntents_NaoEmiteBlocos()
    {
        // Pre-condition do auto-link: na prática, Router sempre tem >= 2
        // intents quando passa por ValidateRouterAsync. Mas o renderer
        // precisa ser defensivo — null/empty == no-op em vez de exception.
        var prompt = PromptRenderer.Render(
            AgentType.Router,
            authorInstructions: "Router sem intents",
            metadata: null,
            routerIntents: Array.Empty<RouterIntent>(),
            skills: null,
            hasOperationalMemory: true);

        prompt.Should().NotBeNull();
        prompt!.Should().NotContain("<intents>");
        prompt.Should().NotContain("<ambiguity_handling>");
    }

    [Fact]
    public void Render_Router_CatalogoListaIntentsNaOrdemRecebida()
    {
        var prompt = RenderRouter();

        // Ordem importa: o pool atual lista intents de negócio antes das
        // canônicas. Se isso mudar, queremos ver — pode afetar primacy bias do
        // LLM (primeiras intents do enum ganham viés positivo em uncertainty).
        var idxRf = prompt.IndexOf("`investir_renda_fixa`", StringComparison.Ordinal);
        var idxRv = prompt.IndexOf("`investir_renda_variavel`", StringComparison.Ordinal);
        var idxNc = prompt.IndexOf(
            $"`{SystemIntents.NeedsClarificationName}` — Mensagem ambígua",
            StringComparison.Ordinal);

        idxRf.Should().BePositive();
        idxRv.Should().BeGreaterThan(idxRf);
        idxNc.Should().BeGreaterThan(idxRv);
    }
}
