using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Core.Orchestration.Workflows;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class DomainInvariantsTests
{
    // ── WorkflowDefinition.Create ────────────────────────────────────────────

    [Fact]
    public void WorkflowDefinition_Create_Sequential_ComAgentes_Valida()
    {
        var wf = WorkflowDefinition.Create(
            id: "wf-1",
            name: "w1",
            orchestrationMode: OrchestrationMode.Sequential,
            agents: new List<WorkflowAgentReference>
            {
                new() { AgentId = "a1" }
            });

        wf.Id.Should().Be("wf-1");
        wf.Agents.Should().HaveCount(1);
    }

    [Fact]
    public void WorkflowDefinition_Create_Sequential_SemAgentes_LancaDomainException()
    {
        Action act = () => WorkflowDefinition.Create(
            id: "wf-1",
            name: "w1",
            orchestrationMode: OrchestrationMode.Sequential,
            agents: []);

        act.Should().Throw<DomainException>()
            .WithMessage("*Sequential exige ao menos um agente*");
    }

    [Fact]
    public void WorkflowDefinition_Create_Graph_SemEdges_LancaDomainException()
    {
        Action act = () => WorkflowDefinition.Create(
            id: "wf-g",
            name: "graph-wf",
            orchestrationMode: OrchestrationMode.Graph,
            agents: [],
            edges: []);

        act.Should().Throw<DomainException>()
            .WithMessage("*Graph exige ao menos uma edge*");
    }

    [Fact]
    public void WorkflowDefinition_Create_Graph_EdgeNodeInexistente_LancaDomainException()
    {
        Action act = () => WorkflowDefinition.Create(
            id: "wf-g",
            name: "graph-wf",
            orchestrationMode: OrchestrationMode.Graph,
            agents: new List<WorkflowAgentReference>
            {
                new() { AgentId = "a1" }
            },
            edges: new List<WorkflowEdge>
            {
                new() { From = "a1", To = "fantasma" }
            });

        act.Should().Throw<DomainException>()
            .WithMessage("*fantasma*");
    }

    [Fact]
    public void WorkflowDefinition_Create_IdVazio_LancaDomainException()
    {
        Action act = () => WorkflowDefinition.Create(
            id: "  ",
            name: "ok",
            orchestrationMode: OrchestrationMode.Sequential,
            agents: new List<WorkflowAgentReference>
            {
                new() { AgentId = "a1" }
            });

        act.Should().Throw<DomainException>().WithMessage("*Id é obrigatório*");
    }

    [Fact]
    public void WorkflowDefinition_EnsureInvariants_Idempotente()
    {
        var wf = WorkflowDefinition.Create(
            id: "wf-1",
            name: "w1",
            orchestrationMode: OrchestrationMode.Sequential,
            agents: new List<WorkflowAgentReference>
            {
                new() { AgentId = "a1" }
            });

        // Deve poder ser chamado múltiplas vezes sem efeito colateral
        wf.EnsureInvariants();
        wf.EnsureInvariants();
        wf.EnsureInvariants();
    }

    // ── Project.Create ──────────────────────────────────────────────────────

    [Fact]
    public void Project_Create_MinimoValido()
    {
        var p = Project.Create(id: "p-1", name: "Projeto", tenantId: "tenant-X");
        p.Id.Should().Be("p-1");
        p.TenantId.Should().Be("tenant-X");
    }

    [Fact]
    public void Project_Create_TenantVazio_LancaDomainException()
    {
        Action act = () => Project.Create(id: "p-1", name: "P", tenantId: "");
        act.Should().Throw<DomainException>().WithMessage("*TenantId*");
    }

    [Fact]
    public void Project_Create_BudgetNegativo_LancaDomainException()
    {
        Action act = () => Project.Create(
            id: "p-1",
            name: "P",
            tenantId: "t",
            settings: new ProjectSettings { MaxCostUsdPerDay = -5m });
        act.Should().Throw<DomainException>().WithMessage("*MaxCostUsdPerDay*");
    }

    // ── AgentDefinition.Create ──────────────────────────────────────────────

    [Fact]
    public void AgentDefinition_Create_MinimoValido()
    {
        var a = AgentDefinition.Create(
            id: "a-1",
            name: "Agent",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" });
        a.Id.Should().Be("a-1");
    }

    [Fact]
    public void AgentDefinition_Create_SemDeploymentName_LancaDomainException()
    {
        Action act = () => AgentDefinition.Create(
            id: "a-1",
            name: "A",
            model: new AgentModelConfig { DeploymentName = "" });
        act.Should().Throw<DomainException>().WithMessage("*DeploymentName*");
    }

    [Fact]
    public void AgentDefinition_Create_TemperatureForaDoRange_LancaDomainException()
    {
        Action act = () => AgentDefinition.Create(
            id: "a-1",
            name: "A",
            model: new AgentModelConfig
            {
                DeploymentName = "gpt-4o",
                Temperature = 5.0f
            });
        act.Should().Throw<DomainException>().WithMessage("*Temperature*");
    }
}
