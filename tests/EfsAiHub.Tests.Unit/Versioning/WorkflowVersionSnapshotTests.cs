namespace EfsAiHub.Tests.Unit.Versioning;

[Trait("Category", "Unit")]
public class WorkflowVersionSnapshotTests
{
    private static WorkflowDefinition BuildDefinition(string id = "wf-test") => new()
    {
        Id = id,
        Name = "Workflow Teste",
        OrchestrationMode = OrchestrationMode.Sequential,
        Agents = [new WorkflowAgentReference { AgentId = "agent-1" }],
    };

    [Fact]
    public void FromDefinition_CapturaId()
    {
        var def = BuildDefinition("wf-abc");
        var version = WorkflowVersion.FromDefinition(def, revision: 1);

        version.WorkflowDefinitionId.Should().Be("wf-abc");
        version.WorkflowVersionId.Should().NotBeNullOrEmpty();
        version.Revision.Should().Be(1);
    }

    [Fact]
    public void FromDefinition_ContentHashReproduzivel()
    {
        var def = BuildDefinition();
        var v1 = WorkflowVersion.FromDefinition(def, revision: 1);
        var v2 = WorkflowVersion.FromDefinition(def, revision: 1);

        v1.ContentHash.Should().Be(v2.ContentHash);
    }

    [Fact]
    public void FromDefinition_ConteudoDiferente_HashDiferente()
    {
        var def1 = BuildDefinition("wf-1");
        var def2 = new WorkflowDefinition
        {
            Id = def1.Id,
            Name = "Outro Nome Completamente Diferente",
            OrchestrationMode = OrchestrationMode.Sequential,
            Agents = [new WorkflowAgentReference { AgentId = "agent-1" }],
        };

        var v1 = WorkflowVersion.FromDefinition(def1, revision: 1);
        var v2 = WorkflowVersion.FromDefinition(def2, revision: 1);

        v1.ContentHash.Should().NotBe(v2.ContentHash);
    }

    [Fact]
    public void FromDefinition_DefinitionSnapshotNaoNulo()
    {
        var def = BuildDefinition();
        var version = WorkflowVersion.FromDefinition(def, revision: 1);

        version.DefinitionSnapshot.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void FromDefinition_DefinitionSnapshot_DeserializaCorretamente()
    {
        var def = BuildDefinition("wf-serial");
        var version = WorkflowVersion.FromDefinition(def, revision: 1);

        var opts = new System.Text.Json.JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        var restored = System.Text.Json.JsonSerializer.Deserialize<WorkflowDefinition>(
            version.DefinitionSnapshot!, opts);

        restored.Should().NotBeNull();
        restored!.Id.Should().Be("wf-serial");
        restored.Name.Should().Be("Workflow Teste");
    }

    [Fact]
    public void FromDefinition_StatusEDraft()
    {
        var def = BuildDefinition();
        var version = WorkflowVersion.FromDefinition(def, revision: 1);

        version.Status.Should().Be(WorkflowVersionStatus.Published);
    }

    [Fact]
    public void FromDefinition_RevisaoDiferenteMesmoConteudo_MesmoHash()
    {
        var def = BuildDefinition();
        var v1 = WorkflowVersion.FromDefinition(def, revision: 1);
        var v2 = WorkflowVersion.FromDefinition(def, revision: 5);

        v1.ContentHash.Should().Be(v2.ContentHash);
    }
}
