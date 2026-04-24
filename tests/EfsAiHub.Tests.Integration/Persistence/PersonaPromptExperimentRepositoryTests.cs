using EfsAiHub.Core.Abstractions.Identity.Persona;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Tests.Integration.Persistence;

/// <summary>
/// F6 — integration tests do experiment repository. Cobre o flow completo
/// Create → GetActive (UNIQUE parcial) → End → recriação pro mesmo scope.
/// Resultados agregados ficam cobertos via fixture do controller em outro
/// teste (env sem dados de llm_token_usage simulados aqui).
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class PersonaPromptExperimentRepositoryTests(IntegrationWebApplicationFactory factory)
{
    private IPersonaPromptExperimentRepository ExpRepo =>
        factory.Services.GetRequiredService<IPersonaPromptExperimentRepository>();

    private IPersonaPromptTemplateRepository TplRepo =>
        factory.Services.GetRequiredService<IPersonaPromptTemplateRepository>();

    private static string UniqueScope(string suffix = "cliente") =>
        $"project:exp-test-{Guid.NewGuid():N}:{suffix}";

    private static string UniqueProject() => $"exp-{Guid.NewGuid():N}";

    private async Task<(Guid vA, Guid vB, int templateId)> SeedVersionsAsync()
    {
        var scope = UniqueScope();
        var v1 = await TplRepo.UpsertAsync(new PersonaPromptTemplate
        {
            Scope = scope, Name = "seed", Template = "variantA-content",
        });
        var versionA = v1.ActiveVersionId!.Value;

        var v2 = await TplRepo.UpsertAsync(new PersonaPromptTemplate
        {
            Scope = scope, Name = "seed", Template = "variantB-content",
        });
        var versionB = v2.ActiveVersionId!.Value;
        return (versionA, versionB, v2.Id);
    }

    [Fact]
    public async Task Create_NovoScope_FicaAtivo_GetActiveRetorna()
    {
        var (vA, vB, _) = await SeedVersionsAsync();
        var projectId = UniqueProject();
        var scope = UniqueScope();

        var created = await ExpRepo.CreateAsync(new PersonaPromptExperiment
        {
            ProjectId = projectId,
            Scope = scope,
            Name = "test",
            VariantAVersionId = vA,
            VariantBVersionId = vB,
            TrafficSplitB = 50,
            Metric = "cost_usd",
        });

        created.Id.Should().BeGreaterThan(0);
        created.IsActive.Should().BeTrue();

        var active = await ExpRepo.GetActiveAsync(projectId, scope);
        active.Should().NotBeNull();
        active!.Id.Should().Be(created.Id);

        await ExpRepo.DeleteAsync(created.Id);
    }

    [Fact]
    public async Task Create_DoisAtivosMesmoScope_ViolaConstraint()
    {
        // UNIQUE(ProjectId, Scope) WHERE EndedAt IS NULL — o segundo insert
        // deve falhar. O controller cuida de devolver Conflict antes, mas o
        // DB precisa ser a defesa final.
        var (vA, vB, _) = await SeedVersionsAsync();
        var projectId = UniqueProject();
        var scope = UniqueScope();

        var first = await ExpRepo.CreateAsync(new PersonaPromptExperiment
        {
            ProjectId = projectId, Scope = scope, Name = "a",
            VariantAVersionId = vA, VariantBVersionId = vB,
            TrafficSplitB = 10, Metric = "cost_usd",
        });

        var act = async () => await ExpRepo.CreateAsync(new PersonaPromptExperiment
        {
            ProjectId = projectId, Scope = scope, Name = "b",
            VariantAVersionId = vA, VariantBVersionId = vB,
            TrafficSplitB = 20, Metric = "cost_usd",
        });

        var ex = await act.Should().ThrowAsync<Exception>();
        // EF envelopa PostgresException com mensagem genérica; o "duplicate
        // key" real vem na inner chain. Bate em qualquer nível.
        var allMessages = new List<string>();
        for (Exception? e = ex.Subject.First(); e is not null; e = e.InnerException)
            allMessages.Add(e.Message);
        string.Join(" || ", allMessages).Should().Contain("duplicate key");

        await ExpRepo.DeleteAsync(first.Id);
    }

    [Fact]
    public async Task EndAsync_MesmoScopeAceitaNovoExperiment()
    {
        // Após End, UNIQUE parcial libera — histórico fica preservado e um
        // novo experiment pode rodar no mesmo scope.
        var (vA, vB, _) = await SeedVersionsAsync();
        var projectId = UniqueProject();
        var scope = UniqueScope();

        var first = await ExpRepo.CreateAsync(new PersonaPromptExperiment
        {
            ProjectId = projectId, Scope = scope, Name = "first",
            VariantAVersionId = vA, VariantBVersionId = vB,
            TrafficSplitB = 10, Metric = "cost_usd",
        });

        var ended = await ExpRepo.EndAsync(first.Id);
        ended.Should().BeTrue();

        var shouldNotReturnActive = await ExpRepo.GetActiveAsync(projectId, scope);
        shouldNotReturnActive.Should().BeNull();

        // Recriar no mesmo scope agora deve funcionar.
        var second = await ExpRepo.CreateAsync(new PersonaPromptExperiment
        {
            ProjectId = projectId, Scope = scope, Name = "second",
            VariantAVersionId = vA, VariantBVersionId = vB,
            TrafficSplitB = 25, Metric = "cost_usd",
        });

        (await ExpRepo.GetActiveAsync(projectId, scope))!.Id.Should().Be(second.Id);

        await ExpRepo.DeleteAsync(first.Id);
        await ExpRepo.DeleteAsync(second.Id);
    }

    [Fact]
    public async Task EndAsync_Idempotente()
    {
        var (vA, vB, _) = await SeedVersionsAsync();
        var exp = await ExpRepo.CreateAsync(new PersonaPromptExperiment
        {
            ProjectId = UniqueProject(), Scope = UniqueScope(), Name = "x",
            VariantAVersionId = vA, VariantBVersionId = vB,
            TrafficSplitB = 0, Metric = "cost_usd",
        });

        (await ExpRepo.EndAsync(exp.Id)).Should().BeTrue();
        (await ExpRepo.EndAsync(exp.Id)).Should().BeTrue(); // no-op

        await ExpRepo.DeleteAsync(exp.Id);
    }

    [Fact]
    public async Task GetByProjectAsync_IsolaEntreProjects()
    {
        var (vA, vB, _) = await SeedVersionsAsync();
        var projA = UniqueProject();
        var projB = UniqueProject();

        var eA = await ExpRepo.CreateAsync(new PersonaPromptExperiment
        {
            ProjectId = projA, Scope = UniqueScope(), Name = "A",
            VariantAVersionId = vA, VariantBVersionId = vB,
            TrafficSplitB = 50, Metric = "cost_usd",
        });
        var eB = await ExpRepo.CreateAsync(new PersonaPromptExperiment
        {
            ProjectId = projB, Scope = UniqueScope(), Name = "B",
            VariantAVersionId = vA, VariantBVersionId = vB,
            TrafficSplitB = 50, Metric = "cost_usd",
        });

        var byA = await ExpRepo.GetByProjectAsync(projA);
        byA.Should().ContainSingle(e => e.Id == eA.Id);
        byA.Should().NotContain(e => e.Id == eB.Id);

        await ExpRepo.DeleteAsync(eA.Id);
        await ExpRepo.DeleteAsync(eB.Id);
    }
}
