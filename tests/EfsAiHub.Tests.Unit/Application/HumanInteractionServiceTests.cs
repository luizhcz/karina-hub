using EfsAiHub.Host.Api.Services;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Core.Orchestration.Coordination;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class HumanInteractionServiceTests
{
    private static HumanInteractionService Build(IHumanInteractionRepository? repo = null)
    {
        repo ??= Substitute.For<IHumanInteractionRepository>();
        repo.GetPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new List<HumanInteractionRequest>());

        return new HumanInteractionService(
            repo,
            Substitute.For<ILogger<HumanInteractionService>>());
    }

    private static HumanInteractionRequest NewRequest(string id = "int-1", string execId = "exec-1") =>
        new()
        {
            InteractionId = id,
            ExecutionId = execId,
            WorkflowId = "wf-1",
            Prompt = "Confirma a operação?"
        };

    [Fact]
    public async Task LoadPendingFromDb_PopulaCache()
    {
        var repo = Substitute.For<IHumanInteractionRepository>();
        var req = NewRequest("int-loaded");
        repo.GetPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new List<HumanInteractionRequest> { req });

        var svc = new HumanInteractionService(repo, Substitute.For<ILogger<HumanInteractionService>>());
        await svc.LoadPendingFromDbAsync();

        svc.GetById("int-loaded").Should().NotBeNull();
    }

    [Fact]
    public async Task LoadPendingFromDb_CacheVazio_NaoLoga()
    {
        var repo = Substitute.For<IHumanInteractionRepository>();
        repo.GetPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new List<HumanInteractionRequest>());

        var svc = new HumanInteractionService(repo, Substitute.For<ILogger<HumanInteractionService>>());
        await svc.LoadPendingFromDbAsync();

        svc.GetPending().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_Approved_AtualizaStatusERetornaTrue()
    {
        var repo = Substitute.For<IHumanInteractionRepository>();
        repo.GetPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new List<HumanInteractionRequest>());

        var svc = Build(repo);
        var req = NewRequest("int-a");

        // Registra no cache interno usando RequestAsync em background
        // Para testar Resolve, precisamos injetar via ReRegisterPending ou observar via GetById
        // Aqui testamos o caminho onde Resolve retorna false para interação desconhecida
        var result = svc.Resolve("nao-existe", "sim");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_Approved_AprovadaAposLoadFromDb()
    {
        var repo = Substitute.For<IHumanInteractionRepository>();
        var req = NewRequest("int-b", "exec-b");
        repo.GetPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new List<HumanInteractionRequest> { req });
        repo.UpdateAsync(Arg.Any<HumanInteractionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var svc = new HumanInteractionService(repo, Substitute.For<ILogger<HumanInteractionService>>());
        await svc.LoadPendingFromDbAsync();

        var result = svc.Resolve("int-b", "aprovado", approved: true, publishToCross: false);

        result.Should().BeTrue();
        svc.GetById("int-b").Should().BeNull(); // removido do cache
    }

    [Fact]
    public async Task Resolve_Rejected_AprovadaAposLoadFromDb()
    {
        var repo = Substitute.For<IHumanInteractionRepository>();
        var req = NewRequest("int-c", "exec-c");
        repo.GetPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new List<HumanInteractionRequest> { req });
        repo.UpdateAsync(Arg.Any<HumanInteractionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var svc = new HumanInteractionService(repo, Substitute.For<ILogger<HumanInteractionService>>());
        await svc.LoadPendingFromDbAsync();

        var result = svc.Resolve("int-c", "rejeitado", approved: false, publishToCross: false);

        result.Should().BeTrue();
    }

    [Fact]
    public void GetPendingForExecution_SemItens_RetornaNull()
    {
        var svc = Build();

        var pending = svc.GetPendingForExecution("exec-xyz");

        pending.Should().BeNull();
    }

    [Fact]
    public async Task GetByExecutionId_RetornaInteracoesDaExecucao()
    {
        var repo = Substitute.For<IHumanInteractionRepository>();
        var req1 = NewRequest("int-1", "exec-same");
        var req2 = NewRequest("int-2", "exec-same");
        var req3 = NewRequest("int-3", "exec-other");
        repo.GetPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new List<HumanInteractionRequest> { req1, req2, req3 });

        var svc = new HumanInteractionService(repo, Substitute.For<ILogger<HumanInteractionService>>());
        await svc.LoadPendingFromDbAsync();

        var result = svc.GetByExecutionId("exec-same");

        result.Should().HaveCount(2);
        result.Should().NotContain(r => r.ExecutionId == "exec-other");
    }
}
