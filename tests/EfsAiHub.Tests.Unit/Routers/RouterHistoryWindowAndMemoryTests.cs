using EfsAiHub.Core.Agents.RouterIntents;
using EfsAiHub.Platform.Runtime;
using EfsAiHub.Platform.Runtime.Application.Services;
using System.Text.Json;

namespace EfsAiHub.Tests.Unit.Routers;

/// <summary>
/// Cobre P2 do redesign canônico: Router recebe apenas as últimas 5
/// mensagens do histórico (não 10/20), enquanto outros agentes do mesmo
/// workflow seguem com a janela global. <see cref="RouterDefaults.HistoryMessages"/>
/// é hardcoded no V1 — esse teste pega regressão se alguém aumentar o valor
/// pensando "deixa eu dar mais contexto".
/// </summary>
[Trait("Category", "Unit")]
public class RouterHistoryWindowAndMemoryTests
{
    private static string SerializeCtx(ChatTurnContext ctx) =>
        JsonSerializer.Serialize(ctx, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    private static ChatTurnContext BuildCtxWithHistory(int historyCount)
    {
        var history = new List<ChatTurnMessage>();
        for (int i = 0; i < historyCount; i++)
        {
            history.Add(new ChatTurnMessage
            {
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"msg-{i}",
            });
        }
        return new ChatTurnContext
        {
            UserId = "u-1",
            ConversationId = "c-1",
            Message = new ChatTurnMessage { Role = "user", Content = "msg-current" },
            History = history,
            Metadata = new Dictionary<string, string> { ["sessionId"] = "s-1" },
        };
    }

    [Fact]
    public void Resolver_Router_DefaultRetorna5_HardcodedNoV1()
    {
        // Garante que ninguém aumentou o valor sem revisão. A janela curta é
        // intencional: classificadores se confundem com contexto longo.
        var n = WorkflowAgentHistoryResolver.Resolve(
            EfsAiHub.Core.Agents.AgentType.Router,
            workflowRef: null,
            workflowConfig: null);
        n.Should().Be(5);
        n.Should().Be(RouterDefaults.HistoryMessages);
    }

    [Fact]
    public void Resolver_NonRouter_RetornaNull_SemSlice()
    {
        // Conversational/Worker/ToolRunner herdam a janela global do workflow.
        // Resolver retorna null = "não fatie, deixe o caller usar o que veio".
        WorkflowAgentHistoryResolver.Resolve(EfsAiHub.Core.Agents.AgentType.Conversational, null, null)
            .Should().BeNull();
        WorkflowAgentHistoryResolver.Resolve(EfsAiHub.Core.Agents.AgentType.Worker, null, null)
            .Should().BeNull();
    }

    [Fact]
    public void Resolver_OverrideExplicito_VenceTipoEDefault()
    {
        // Workflow declarou HistoryOverride=10 pra esse agente — override
        // domina o default per-tipo. Mesmo Router pega 10 nesse caso.
        var workflowRef = new WorkflowAgentReference
        {
            AgentId = "r",
            HistoryOverride = 10,
        };
        WorkflowAgentHistoryResolver.Resolve(
            EfsAiHub.Core.Agents.AgentType.Router, workflowRef, null)
            .Should().Be(10);
    }

    [Fact]
    public void Mapper_RouterAgent_RecebeApenasUltimas5Mensagens_EvenComHistorico20()
    {
        // P2: o slice acontece no mapper, antes do append. Workflow pode ter
        // MaxHistoryMessages=20 (default global) mas o Router só vê 5.
        var ctx = BuildCtxWithHistory(20);
        var raw = SerializeCtx(ctx);

        var expanded = ChatTurnContextMapper.TryExpand(raw, historyWindow: 5);

        expanded.Should().NotBeNull();
        // [system metadata, history (5), user atual] → 7 mensagens
        var userAssistantMessages = expanded!
            .Where(m => m.Role.Value is "user" or "assistant")
            .Where(m => m.Text?.StartsWith("msg-") == true && m.Text != "msg-current")
            .ToList();
        userAssistantMessages.Should().HaveCount(5);
        // Slice tomou as ÚLTIMAS 5 (msg-15 a msg-19) — não as primeiras.
        userAssistantMessages.First().Text.Should().Be("msg-15");
        userAssistantMessages.Last().Text.Should().Be("msg-19");
    }

    [Fact]
    public void Mapper_SemHistoryWindow_NaoFatia()
    {
        // Quando o caller não passa janela (null), o mapper preserva o
        // histórico inteiro — comportamento legacy pra Conversational/Worker.
        var ctx = BuildCtxWithHistory(8);
        var raw = SerializeCtx(ctx);

        var expanded = ChatTurnContextMapper.TryExpand(raw, historyWindow: null);

        expanded.Should().NotBeNull();
        var historyMessages = expanded!
            .Where(m => m.Role.Value is "user" or "assistant")
            .Where(m => m.Text?.StartsWith("msg-") == true && m.Text != "msg-current")
            .ToList();
        historyMessages.Should().HaveCount(8);
    }

    [Fact]
    public void Mapper_HistoryWindowMaiorQueHistorico_NaoFatia()
    {
        // Pedimos 5 mas só temos 3 — preserva todas as 3 (TakeLast tolera).
        var ctx = BuildCtxWithHistory(3);
        var raw = SerializeCtx(ctx);

        var expanded = ChatTurnContextMapper.TryExpand(raw, historyWindow: 5);

        expanded.Should().NotBeNull();
        var historyMessages = expanded!
            .Where(m => m.Role.Value is "user" or "assistant")
            .Where(m => m.Text?.StartsWith("msg-") == true && m.Text != "msg-current")
            .ToList();
        historyMessages.Should().HaveCount(3);
    }

    [Fact]
    public void RouterDefaults_OperationalMemorySchema_ContemTodosOsCamposCanonicos()
    {
        // O OperationalMemoryChatClient extrai o sub-objeto operationalMemory
        // do JSON top-level emitido pelo LLM e valida contra esse schema
        // antes de persistir. Os 4 campos precisam estar declarados como
        // required — senão LLM omite e a memória nunca grava.
        //   - last_intent / last_reason: nome + razão da última classificação
        //   - clarification_depth: loop guard counter (server-enforced também)
        //   - last_candidate_intents: nomes das candidatas no último turno
        //     needs_clarification — sem isso, follow-up de desambiguação não
        //     consegue mapear resposta do usuário
        var memory = RouterDefaults.OperationalMemoryV1();
        memory.Schema.Should().NotBeNull();
        memory.MaxBytes.Should().Be(2048);

        var root = memory.Schema!.RootElement;
        var props = root.GetProperty("properties");
        props.TryGetProperty("last_intent", out _).Should().BeTrue();
        props.TryGetProperty("last_reason", out _).Should().BeTrue();
        props.TryGetProperty("clarification_depth", out var depth).Should().BeTrue();
        depth.GetProperty("type").GetString().Should().Be("integer");
        props.TryGetProperty("last_candidate_intents", out var lci).Should().BeTrue();
        lci.GetProperty("type").GetString().Should().Be("array");
        lci.GetProperty("items").GetProperty("type").GetString().Should().Be("string");

        var required = root.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
        required.Should().BeEquivalentTo(new[]
        {
            "last_intent", "last_reason", "clarification_depth", "last_candidate_intents",
        });
    }

    [Fact]
    public void RouterDefaults_OutputSchema_ContemCandidateIntentsEClarificationDepthNoMemory()
    {
        // Schema do StructuredOutput é o contrato que o LLM precisa cumprir.
        // candidate_intents é required (default [] quando intent !=
        // needs_clarification); clarification_depth vive dentro do
        // operationalMemory sub-objeto. Sem esses campos no canônico, o
        // ChatOptionsBuilder não passa o shape correto pro provider e o LLM
        // não sabe emitir os campos de ambiguidade.
        var output = RouterDefaults.OutputSchema();
        output.Schema.Should().NotBeNull();

        var root = output.Schema!.RootElement;
        var props = root.GetProperty("properties");

        props.TryGetProperty("candidate_intents", out var candidates).Should().BeTrue();
        candidates.GetProperty("type").GetString().Should().Be("array");
        var itemProps = candidates.GetProperty("items").GetProperty("properties");
        itemProps.TryGetProperty("intent", out _).Should().BeTrue();
        itemProps.TryGetProperty("confidence", out _).Should().BeTrue();

        var memProps = props.GetProperty("operationalMemory").GetProperty("properties");
        memProps.TryGetProperty("clarification_depth", out _).Should().BeTrue();
        // last_candidate_intents espelha o que o canônico do operationalMemory
        // schema externo declara — ambos precisam estar em sync porque o
        // OperationalMemoryChatClient extrai do output pra persistir no banco.
        memProps.TryGetProperty("last_candidate_intents", out _).Should().BeTrue();

        var required = root.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
        required.Should().Contain("candidate_intents");
    }
}
