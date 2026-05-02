using EfsAiHub.Core.Abstractions.Exceptions;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class AgentDraftPayloadTests
{
    [Fact]
    public void ToAgentDefinition_PayloadCompleto_RoundtripDeterministico()
    {
        var payload = new AgentDraftPayload
        {
            Name = "Coletor",
            Description = "agente coletor",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o", Temperature = 0.5f },
            Provider = new AgentProviderConfig { Type = "AzureOpenAI" },
            Instructions = "Você é coletor.",
            Visibility = "global",
            Enabled = true,
        };

        var def = payload.ToAgentDefinition(
            id: "agente-coletor",
            projectId: "owner",
            tenantId: "tenant-a");

        def.Id.Should().Be("agente-coletor");
        def.Name.Should().Be("Coletor");
        def.Model.DeploymentName.Should().Be("gpt-4o");
        def.Visibility.Should().Be("global");
        def.ProjectId.Should().Be("owner");
        def.TenantId.Should().Be("tenant-a");
        def.Enabled.Should().BeTrue();
    }

    [Fact]
    public void ToAgentDefinition_NameVazio_LancaDomainException()
    {
        var payload = new AgentDraftPayload
        {
            Name = null,
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
        };

        Action act = () => payload.ToAgentDefinition("id-x", "p", "t");

        act.Should().Throw<DomainException>()
            .WithMessage("*Name*");
    }

    [Fact]
    public void ToAgentDefinition_ModelDeploymentNameVazio_LancaDomainException()
    {
        var payload = new AgentDraftPayload
        {
            Name = "X",
            Model = null,
        };

        Action act = () => payload.ToAgentDefinition("id-x", "p", "t");

        act.Should().Throw<DomainException>()
            .WithMessage("*DeploymentName*");
    }

    [Fact]
    public void ToAgentDefinition_DefaultsAplicados_QuandoCamposOpcionaisAusentes()
    {
        var payload = new AgentDraftPayload
        {
            Name = "Mínimo",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
        };

        var def = payload.ToAgentDefinition("id-min", "p", "t");

        def.Visibility.Should().Be("project");
        def.Enabled.Should().BeTrue();
        def.Tools.Should().BeEmpty();
        def.Middlewares.Should().BeEmpty();
        def.SkillRefs.Should().BeEmpty();
        def.AllowedProjectIds.Should().BeNull();
    }

    [Fact]
    public void FromAgentDefinition_PreservaCamposPersistiveis()
    {
        var def = new AgentDefinition
        {
            Id = "src",
            Name = "Source",
            Description = "Origem",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            Visibility = "global",
            Enabled = false,
            ProjectId = "owner",
            TenantId = "tenant-a",
            AllowedProjectIds = new[] { "p2", "p3" },
        };

        var payload = AgentDraftPayload.FromAgentDefinition(def);

        payload.Name.Should().Be("Source");
        payload.Description.Should().Be("Origem");
        payload.Visibility.Should().Be("global");
        payload.Enabled.Should().BeFalse();
        payload.AllowedProjectIds.Should().BeEquivalentTo(new[] { "p2", "p3" });
    }

    [Fact]
    public void ToAgentDefinition_AllowedProjectIdsComVisibilityProject_LancaDomainException()
    {
        // Invariante de AgentDefinition: AllowedProjectIds só é válido quando Visibility=global.
        // Combinar AllowedProjectIds + Visibility=null (default project) tem que falhar no publish.
        var payload = new AgentDraftPayload
        {
            Name = "X",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            Visibility = "project",
            AllowedProjectIds = new[] { "p1" },
        };

        Action act = () => payload.ToAgentDefinition("id-x", "p", "t");

        act.Should().Throw<DomainException>()
            .WithMessage("*AllowedProjectIds*");
    }

    [Fact]
    public void IsEditDraft_BaseAgentIdSetado_RetornaTrue()
    {
        var draftCriacao = new AgentDraft
        {
            Id = "novo",
            Name = "Novo",
            Payload = new AgentDraftPayload(),
            ProjectId = "p",
            TenantId = "t",
        };

        var draftEdit = new AgentDraft
        {
            Id = "agent-x",
            Name = "Edit X",
            Payload = new AgentDraftPayload(),
            ProjectId = "p",
            TenantId = "t",
            BaseAgentId = "agent-x",
            BaseRevision = 3,
        };

        draftCriacao.IsEditDraft.Should().BeFalse();
        draftEdit.IsEditDraft.Should().BeTrue();
    }

    [Fact]
    public void ToAgentDefinition_PayloadParcial_PersisteSemValidacao()
    {
        // Pré-publish, draft pode ter campos faltando — round-trip via JSON
        // (simulando o ciclo PUT → DB → GET) deve preservar o estado parcial
        // sem rodar invariantes.
        var payload = new AgentDraftPayload { Name = "WIP", Description = "rascunho" };

        var json = System.Text.Json.JsonSerializer.Serialize(payload,
            EfsAiHub.Core.Abstractions.Persistence.JsonDefaults.Domain);
        var roundtrip = System.Text.Json.JsonSerializer.Deserialize<AgentDraftPayload>(json,
            EfsAiHub.Core.Abstractions.Persistence.JsonDefaults.Domain);

        roundtrip.Should().NotBeNull();
        roundtrip!.Name.Should().Be("WIP");
        roundtrip.Description.Should().Be("rascunho");
        roundtrip.Model.Should().BeNull();
    }
}
