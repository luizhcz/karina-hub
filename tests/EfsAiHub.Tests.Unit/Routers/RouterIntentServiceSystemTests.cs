using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Agents.RouterIntents;

namespace EfsAiHub.Tests.Unit.Routers;

/// <summary>
/// Cobre P1 pelo lado da governança de intents: a intent reservada
/// <see cref="SystemIntents.OutOfScopeName"/> NÃO pode ser deletada nem
/// editada pelo admin. Bloqueio é hard (throw) — a UI deveria desabilitar
/// os controles, mas o backend não confia: valida sempre.
/// </summary>
[Trait("Category", "Unit")]
public class RouterIntentServiceSystemTests
{
    private static RouterIntent SystemIntent() => new()
    {
        Id = "sys-oos-default",
        TenantId = "default",
        ProjectId = "default",
        Name = SystemIntents.OutOfScopeName,
        DisplayName = "Fora de escopo",
        Description = "Mensagem fora do escopo do Router",
        Examples = Array.Empty<string>(),
        IsSystem = true,
    };

    private static RouterIntent NormalIntent() => new()
    {
        Id = "biz-boleta-default",
        TenantId = "default",
        ProjectId = "default",
        Name = "boleta",
        DisplayName = "Boleta",
        Description = "Operações",
        Examples = Array.Empty<string>(),
        IsSystem = false,
    };

    private static RouterIntentService Build(out IRouterIntentRepository repo, out IAgentRouterIntentLinkRepository links)
    {
        repo = Substitute.For<IRouterIntentRepository>();
        links = Substitute.For<IAgentRouterIntentLinkRepository>();
        links.ListAgentsForIntentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RouterIntentUsage>>(Array.Empty<RouterIntentUsage>()));

        var workflowService = Substitute.For<IWorkflowService>();
        var projectAccessor = Substitute.For<IProjectContextAccessor>();
        projectAccessor.Current.Returns(new ProjectContext("default", "Default"));
        var tenantAccessor = Substitute.For<ITenantContextAccessor>();
        tenantAccessor.Current.Returns(new TenantContext("default"));

        return new RouterIntentService(
            repo, links, workflowService, projectAccessor, tenantAccessor,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RouterIntentService>.Instance);
    }

    [Fact]
    public async Task DeleteAsync_SystemIntent_ThrowsImmutable()
    {
        var sys = SystemIntent();
        var service = Build(out var repo, out _);
        repo.GetByIdAsync(sys.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<RouterIntent?>(sys));

        var act = async () => await service.DeleteAsync(sys.Id);

        await act.Should().ThrowAsync<SystemIntentImmutableException>();
        // Verifica que NÃO chegou a chamar Delete no repo.
        await repo.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_SystemIntent_ThrowsImmutable()
    {
        var sys = SystemIntent();
        var service = Build(out var repo, out _);
        repo.GetByIdAsync(sys.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<RouterIntent?>(sys));

        var patch = new RouterIntent
        {
            Id = sys.Id,
            TenantId = sys.TenantId,
            ProjectId = sys.ProjectId,
            Name = sys.Name,
            DisplayName = "Tentando renomear",
            Description = "Hack",
        };
        var act = async () => await service.UpdateAsync(sys.Id, patch);

        await act.Should().ThrowAsync<SystemIntentImmutableException>();
        await repo.DidNotReceive().UpdateAsync(Arg.Any<RouterIntent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_NormalIntent_AindaPodeSerDeletada()
    {
        // Sanity check: a regra é específica de IsSystem=true. Intent normal
        // continua deletável (modulo check de uso).
        var biz = NormalIntent();
        var service = Build(out var repo, out _);
        repo.GetByIdAsync(biz.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<RouterIntent?>(biz));
        repo.DeleteAsync(biz.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var deleted = await service.DeleteAsync(biz.Id);
        deleted.Should().BeTrue();
    }
}
