using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Tests.Integration.Persistence;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class ConversationRepositoryTests(IntegrationWebApplicationFactory factory)
{
    private IConversationRepository Repo =>
        factory.Services.GetRequiredService<IConversationRepository>();

    private static ConversationSession MakeSession(string? userId = null, string? workflowId = null) => new()
    {
        ConversationId = Guid.NewGuid().ToString(),
        UserId = userId ?? $"user-{Guid.NewGuid():N}",
        WorkflowId = workflowId ?? $"wf-{Guid.NewGuid():N}",
        Title = "Test Conversation",
        Metadata = new Dictionary<string, string> { ["source"] = "integration-test" }
    };

    // ── Create + GetById ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_GetById_RetornaCamposMapeados()
    {
        var session = MakeSession();

        await Repo.CreateAsync(session);
        var fetched = await Repo.GetByIdAsync(session.ConversationId);

        fetched.Should().NotBeNull();
        fetched!.ConversationId.Should().Be(session.ConversationId);
        fetched.UserId.Should().Be(session.UserId);
        fetched.WorkflowId.Should().Be(session.WorkflowId);
    }

    [Fact]
    public async Task CreateAsync_MetadadosJsonPreservados()
    {
        var session = MakeSession();
        session.Metadata["chave"] = "valor-teste";

        await Repo.CreateAsync(session);
        var fetched = await Repo.GetByIdAsync(session.ConversationId);

        fetched!.Metadata.Should().ContainKey("chave").WhoseValue.Should().Be("valor-teste");
        fetched.Metadata.Should().ContainKey("source").WhoseValue.Should().Be("integration-test");
    }

    [Fact]
    public async Task GetById_Inexistente_RetornaNull()
    {
        var result = await Repo.GetByIdAsync(Guid.NewGuid().ToString());

        result.Should().BeNull();
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_AlteraTitle_PersisteAlteracao()
    {
        var session = MakeSession();
        await Repo.CreateAsync(session);

        session.Title = "Título Atualizado";
        session.LastMessageAt = DateTime.UtcNow;
        await Repo.UpdateAsync(session);

        var fetched = await Repo.GetByIdAsync(session.ConversationId);

        fetched!.Title.Should().Be("Título Atualizado");
    }

    // ── GetByUserIdAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetByUserId_RetornaConversasDoUsuario()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        var s1 = MakeSession(userId);
        var s2 = MakeSession(userId);
        await Repo.CreateAsync(s1);
        await Repo.CreateAsync(s2);

        var results = await Repo.GetByUserIdAsync(userId);

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(s => s.UserId.Should().Be(userId));
    }

    [Fact]
    public async Task GetByUserId_NaoRetornaOutrosUsuarios()
    {
        var userA = $"user-a-{Guid.NewGuid():N}";
        var userB = $"user-b-{Guid.NewGuid():N}";
        await Repo.CreateAsync(MakeSession(userA));
        await Repo.CreateAsync(MakeSession(userB));

        var results = await Repo.GetByUserIdAsync(userA);

        results.Should().NotContain(s => s.UserId == userB);
    }

    [Fact]
    public async Task GetByUserId_LimitaResultados()
    {
        var userId = $"user-lim-{Guid.NewGuid():N}";
        for (var i = 0; i < 5; i++)
            await Repo.CreateAsync(MakeSession(userId));

        var results = await Repo.GetByUserIdAsync(userId, limit: 3);

        results.Should().HaveCount(3);
    }

    // ── GetAllAsync paginado ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_PaginacaoPage2_OffsetCorreto()
    {
        var workflowId = $"wf-page-{Guid.NewGuid():N}";
        for (var i = 0; i < 5; i++)
            await Repo.CreateAsync(MakeSession(workflowId: workflowId));

        var page1 = await Repo.GetAllAsync(workflowId: workflowId, page: 1, pageSize: 3);
        var page2 = await Repo.GetAllAsync(workflowId: workflowId, page: 2, pageSize: 3);

        page1.Should().HaveCount(3);
        page2.Should().HaveCount(2);

        // No overlap between pages
        var page1Ids = page1.Select(s => s.ConversationId).ToHashSet();
        page2.Should().NotContain(s => page1Ids.Contains(s.ConversationId));
    }

    [Fact]
    public async Task GetAllAsync_FiltroDeData_ExcluiForaDoPeriodo()
    {
        var session = MakeSession();
        await Repo.CreateAsync(session);

        var from = DateTime.UtcNow.AddYears(10);
        var results = await Repo.GetAllAsync(from: from);

        results.Should().NotContain(s => s.ConversationId == session.ConversationId);
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_SessaoExistente_RemoveDoBanco()
    {
        var session = MakeSession();
        await Repo.CreateAsync(session);

        await Repo.DeleteAsync(session.ConversationId);
        var fetched = await Repo.GetByIdAsync(session.ConversationId);

        fetched.Should().BeNull();
    }

    // ── CountAllAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CountAllAsync_FiltroPorUserId_RetornaContagemCorreta()
    {
        var userId = $"user-cnt-{Guid.NewGuid():N}";
        await Repo.CreateAsync(MakeSession(userId));
        await Repo.CreateAsync(MakeSession(userId));

        var count = await Repo.CountAllAsync(userId: userId);

        count.Should().Be(2);
    }
}
