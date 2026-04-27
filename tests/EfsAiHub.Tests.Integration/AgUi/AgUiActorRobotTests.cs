using System.Net.Http.Headers;
using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Platform.Runtime.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Tests.Integration.AgUi;

/// <summary>
/// Integration tests do caminho actor=robot no AG-UI stream:
/// - Happy path: persiste sem disparar workflow, emite SSE sintético.
/// - Misuse 400: actor=robot fora da última posição.
/// - Misuse 400: actor inválido (string fora de "human"/"robot").
/// - Spec-compat: actor ausente segue caminho normal (workflow dispara — não testado
///   aqui pra evitar dependência de LLM real, já coberto por outros testes).
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class AgUiActorRobotTests : IAsyncLifetime
{
    private const string TestWorkflowId = "wf-actor-robot-test";

    private readonly HttpClient _client;
    private readonly IChatMessageRepository _msgRepo;
    private readonly IConversationRepository _convRepo;
    private readonly IWorkflowDefinitionRepository _wfRepo;
    private readonly IHumanInteractionService _hitlService;

    public AgUiActorRobotTests(IntegrationWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _msgRepo = factory.Services.GetRequiredService<IChatMessageRepository>();
        _convRepo = factory.Services.GetRequiredService<IConversationRepository>();
        _wfRepo = factory.Services.GetRequiredService<IWorkflowDefinitionRepository>();
        _hitlService = factory.Services.GetRequiredService<IHumanInteractionService>();
    }

    public async Task InitializeAsync()
    {
        // Registra um workflow mínimo no banco de teste — actor=robot faz short-circuit
        // antes do trigger, então o workflow nunca executa de fato.
        await _wfRepo.UpsertAsync(new WorkflowDefinition
        {
            Id = TestWorkflowId,
            Name = "Actor Robot Test",
            OrchestrationMode = OrchestrationMode.Sequential,
            Agents = [],
            Configuration = new WorkflowConfiguration { InputMode = "Chat" }
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Post_ActorRobot_PersistEMantemSemDisparoDeWorkflow()
    {
        // Arrange — pré-criar conversa com threadId controlado pra conseguir consultar
        // mensagens persistidas no assert. Se deixar o endpoint criar, ele gera ID novo
        // e perdemos a referência (resposta SSE não retorna threadId estruturado).
        var threadId = $"conv-{Guid.NewGuid():N}";
        await _convRepo.CreateAsync(new ConversationSession
        {
            ConversationId = threadId,
            UserId = "test-robot",
            WorkflowId = TestWorkflowId
        });

        var payload = new
        {
            threadId,
            messages = new[]
            {
                new { role = "user", content = "{\"saldo\":12480}", actor = "robot" }
            }
        };

        // Act — workflowId não é executado (short-circuit roda antes do trigger).
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/ag-ui/stream")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        // Assert HTTP — 200 com SSE, não 4xx
        var diagBody = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: $"resp body: {diagBody}");
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        // Drena o stream — não deve bloquear (short-circuit emite eventos imediatos).
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"type\":\"RUN_STARTED\"");
        body.Should().Contain("\"customName\":\"actor.persisted\"");
        body.Should().Contain("\"type\":\"MESSAGES_SNAPSHOT\"");
        body.Should().Contain("\"type\":\"RUN_FINISHED\"");

        // Banco — mensagem persistida com Actor=Robot, Role=user.
        var saved = await _msgRepo.ListAsync(threadId);
        saved.Should().HaveCount(1);
        saved[0].Role.Should().Be("user");
        saved[0].Actor.Should().Be(Actor.Robot);
        saved[0].Content.Should().Be("{\"saldo\":12480}");
    }

    [Fact]
    public async Task Post_ActorRobotForaDaUltimaPosicao_Retorna400()
    {
        // Arrange — actor=robot no meio do batch é misuse: robot "fecha turno".
        var payload = new
        {
            messages = new object[]
            {
                new { role = "user", content = "{\"x\":1}", actor = "robot" },
                new { role = "user", content = "olá" }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/chat/ag-ui/stream", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("última mensagem");
    }

    [Fact]
    public async Task Post_ActorInvalido_Retorna400()
    {
        // Arrange — string fora de "human"/"robot" deve falhar explicitamente
        // (sem silent default pra human, que mascara bug de cliente).
        var payload = new
        {
            messages = new[]
            {
                new { role = "user", content = "olá", actor = "alien" }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/chat/ag-ui/stream", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("actor inválido");
    }

    [Fact]
    public async Task Post_ActorRobotComHitlPendente_Retorna400()
    {
        // Arrange — conversa com execução em curso e HITL pendente.
        // actor=robot durante HITL é misuse: HITL programático deve usar /resolve-hitl,
        // não /stream com actor=robot.
        var threadId = $"conv-{Guid.NewGuid():N}";
        var executionId = $"exec-{Guid.NewGuid():N}";
        await _convRepo.CreateAsync(new ConversationSession
        {
            ConversationId = threadId,
            UserId = "test-robot",
            WorkflowId = TestWorkflowId,
            ActiveExecutionId = executionId
        });

        _hitlService.InjectForRecovery(new HumanInteractionRequest
        {
            InteractionId = $"hitl-{Guid.NewGuid():N}",
            ExecutionId = executionId,
            WorkflowId = TestWorkflowId,
            Prompt = "Aprovar boleta?",
            InteractionType = InteractionType.Approval,
            Status = HumanInteractionStatus.Pending
        });

        var payload = new
        {
            threadId,
            messages = new[]
            {
                new { role = "user", content = "{\"approved\":true}", actor = "robot" }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/chat/ag-ui/stream", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("resolve-hitl");

        // Assert — banco não persistiu mensagem (validação rejeitou antes).
        var saved = await _msgRepo.ListAsync(threadId);
        saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_ActorHuman_Aceita()
    {
        // Arrange — actor=human explícito é spec-compat. Não dispara short-circuit;
        // entra no caminho normal de workflow trigger. Verificação: response não é 400.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/ag-ui/stream");
        request.Headers.Add("x-efs-workflow-id", TestWorkflowId);
        request.Content = JsonContent.Create(new
        {
            messages = new[]
            {
                new { role = "user", content = "olá", actor = "human" }
            }
        });

        var response = await _client.SendAsync(request);

        // Não deve ser 400 (validação de actor passa). Workflow trigger pode falhar por
        // outras razões (LLM, etc) — só checamos que não é o nosso 400.
        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
    }
}
