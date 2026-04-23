using EfsAiHub.Core.Abstractions.Identity.Persona;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Tests.Integration.Persistence;

/// <summary>
/// F5.5 — testes de integração do versionamento de templates. Cobrem o flow
/// transacional do Upsert + Rollback, que não é trivial de mockar (depende
/// de transaction + FK CASCADE + geração de Id).
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class PersonaPromptTemplateVersioningTests(IntegrationWebApplicationFactory factory)
{
    private IPersonaPromptTemplateRepository Repo =>
        factory.Services.GetRequiredService<IPersonaPromptTemplateRepository>();

    private static string UniqueScope(string suffix = "cliente") =>
        $"agent:ver-test-{Guid.NewGuid():N}:{suffix}";

    [Fact]
    public async Task UpsertAsync_Create_CriaTemplateEVersionInicial()
    {
        var scope = UniqueScope();
        var saved = await Repo.UpsertAsync(new PersonaPromptTemplate
        {
            Scope = scope,
            Name = "F5.5 create",
            Template = "v1 {{business_segment}}",
        });

        saved.ActiveVersionId.Should().NotBeNull();
        var versions = await Repo.GetVersionsAsync(saved.Id);
        versions.Should().HaveCount(1);
        versions[0].Template.Should().Be("v1 {{business_segment}}");
        versions[0].VersionId.Should().Be(saved.ActiveVersionId!.Value);

        await Repo.DeleteAsync(saved.Id);
    }

    [Fact]
    public async Task UpsertAsync_UpdateContent_CriaNovaVersionEMoveActive()
    {
        var scope = UniqueScope();
        var v1 = await Repo.UpsertAsync(new PersonaPromptTemplate
        {
            Scope = scope, Name = "X", Template = "v1",
        });
        var v1VersionId = v1.ActiveVersionId!.Value;

        var v2 = await Repo.UpsertAsync(new PersonaPromptTemplate
        {
            Scope = scope, Name = "X edited", Template = "v2 new content",
        });

        v2.ActiveVersionId.Should().NotBeNull();
        v2.ActiveVersionId!.Value.Should().NotBe(v1VersionId);

        var versions = await Repo.GetVersionsAsync(v2.Id);
        versions.Should().HaveCount(2);

        await Repo.DeleteAsync(v2.Id);
    }

    [Fact]
    public async Task UpsertAsync_UpdateSameContent_NaoCriaVersionNova()
    {
        // Re-upsert com mesmo Template é no-op no histórico (só atualiza Name).
        // Evita poluir histórico com saves "falsos".
        var scope = UniqueScope();
        var v1 = await Repo.UpsertAsync(new PersonaPromptTemplate
        {
            Scope = scope, Name = "A", Template = "content",
        });

        await Repo.UpsertAsync(new PersonaPromptTemplate
        {
            Scope = scope, Name = "B (name only change)", Template = "content",
        });

        var versions = await Repo.GetVersionsAsync(v1.Id);
        versions.Should().HaveCount(1);

        await Repo.DeleteAsync(v1.Id);
    }

    [Fact]
    public async Task RollbackAsync_CriaNovaVersion_NaoRepointa()
    {
        var scope = UniqueScope();
        var created = await Repo.UpsertAsync(new PersonaPromptTemplate
        {
            Scope = scope, Name = "X", Template = "v1",
        });
        var v1VersionId = created.ActiveVersionId!.Value;

        await Repo.UpsertAsync(new PersonaPromptTemplate
        {
            Scope = scope, Name = "X", Template = "v2",
        });
        await Repo.UpsertAsync(new PersonaPromptTemplate
        {
            Scope = scope, Name = "X", Template = "v3",
        });

        var rolled = await Repo.RollbackAsync(created.Id, v1VersionId);

        rolled.Should().NotBeNull();
        rolled!.Template.Should().Be("v1");
        // Contrato ADR 004: rollback NÃO repointa pra version antiga —
        // cria uma nova apontada pro conteúdo de v1.
        rolled.ActiveVersionId.Should().NotBe(v1VersionId);

        var versions = await Repo.GetVersionsAsync(created.Id);
        versions.Should().HaveCount(4); // v1, v2, v3, v4=rollback-copia-v1
        versions[0].VersionId.Should().Be(rolled.ActiveVersionId!.Value);
        versions[0].ChangeReason.Should().Contain("rollback");

        await Repo.DeleteAsync(created.Id);
    }

    [Fact]
    public async Task RollbackAsync_VersionIdForaDoTemplate_RetornaNull()
    {
        var scopeA = UniqueScope();
        var scopeB = UniqueScope();
        var templateA = await Repo.UpsertAsync(new PersonaPromptTemplate
        {
            Scope = scopeA, Name = "A", Template = "a1",
        });
        var templateB = await Repo.UpsertAsync(new PersonaPromptTemplate
        {
            Scope = scopeB, Name = "B", Template = "b1",
        });

        // Tentativa de rollback cross-template — versionId de B contra templateId de A.
        var result = await Repo.RollbackAsync(templateA.Id, templateB.ActiveVersionId!.Value);

        result.Should().BeNull();

        await Repo.DeleteAsync(templateA.Id);
        await Repo.DeleteAsync(templateB.Id);
    }

    [Fact]
    public async Task DeleteAsync_CascadeRemoveVersions()
    {
        var scope = UniqueScope();
        var created = await Repo.UpsertAsync(new PersonaPromptTemplate
        {
            Scope = scope, Name = "X", Template = "v1",
        });
        await Repo.UpsertAsync(new PersonaPromptTemplate
        {
            Scope = scope, Name = "X", Template = "v2",
        });

        var versionsBefore = await Repo.GetVersionsAsync(created.Id);
        versionsBefore.Should().HaveCount(2);

        await Repo.DeleteAsync(created.Id);

        var versionsAfter = await Repo.GetVersionsAsync(created.Id);
        versionsAfter.Should().BeEmpty();
    }
}
