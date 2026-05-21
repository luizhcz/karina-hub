using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Core.Agents.Services;
using EfsAiHub.Platform.Runtime.Configuration;
using EfsAiHub.Platform.Runtime.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class GenericToolServiceTests
{
    private static (GenericToolService svc, IGenericToolRepository repo) Build(
        string projectId = "alpha",
        string tenantId = "tenant-a",
        int maxTimeout = 120)
    {
        var repo = Substitute.For<IGenericToolRepository>();
        var agentRepo = Substitute.For<IAgentDefinitionRepository>();
        var propagator = Substitute.For<IAgentDependencyPropagator>();
        var projectAccessor = Substitute.For<IProjectContextAccessor>();
        projectAccessor.Current.Returns(new ProjectContext(projectId));
        var tenantAccessor = Substitute.For<ITenantContextAccessor>();
        tenantAccessor.Current.Returns(new TenantContext(tenantId));
        var options = Options.Create(new GenericToolsOptions
        {
            DefaultTimeoutSeconds = 60,
            MaxTimeoutSeconds = maxTimeout,
        });

        var svc = new GenericToolService(repo, agentRepo, propagator, projectAccessor, tenantAccessor, options,
            Substitute.For<ILogger<GenericToolService>>());

        return (svc, repo);
    }

    private static GenericTool ValidGetTemplate(
        string id = "",
        string name = "search-users",
        string projectId = "alpha",
        string tenantId = "tenant-a") => new()
    {
        Id = id,
        ProjectId = projectId,
        TenantId = tenantId,
        Name = name,
        HttpMethod = HttpMethodType.GET,
        UrlTemplate = "https://api.example.com/users/{id}",
        PathParams = new Dictionary<string, ParamDefinition>
        {
            ["id"] = new("string", "user id", true),
        },
        OutputContentType = OutputContentType.Json,
        OutputSchema = "{\"type\":\"object\",\"properties\":{}}",
    };

    [Fact]
    public async Task CreateAsync_GeraIdQuandoAusente_ESalva()
    {
        var (svc, repo) = Build();
        repo.NameExistsAsync("search-users", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(false);
        repo.CreateAsync(Arg.Any<GenericTool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<GenericTool>());

        var result = await svc.CreateAsync(id: null, ValidGetTemplate());

        result.Id.Should().NotBeNullOrEmpty();
        result.Id.Length.Should().Be(32);
        result.ProjectId.Should().Be("alpha");
        result.TenantId.Should().Be("tenant-a");
        await repo.Received(1).CreateAsync(Arg.Any<GenericTool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_RespeitaIdInformado()
    {
        var (svc, repo) = Build();
        repo.CreateAsync(Arg.Any<GenericTool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<GenericTool>());

        var result = await svc.CreateAsync(id: "my-tool", ValidGetTemplate());

        result.Id.Should().Be("my-tool");
    }

    [Fact]
    public async Task CreateAsync_NomeColidente_LancaConflict()
    {
        var (svc, repo) = Build();
        repo.NameExistsAsync("search-users", null, Arg.Any<CancellationToken>())
            .Returns(true);

        Func<Task> act = () => svc.CreateAsync(null, ValidGetTemplate());

        await act.Should().ThrowAsync<GenericToolNameConflictException>();
    }

    [Fact]
    public async Task CreateAsync_TimeoutOverrideMaiorQueMax_Lanca400()
    {
        var (svc, _) = Build(maxTimeout: 60);
        var template = ValidGetTemplate();
        template.TimeoutSecondsOverride = 200;

        Func<Task> act = () => svc.CreateAsync(null, template);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*excede o limite global*");
    }

    [Fact]
    public async Task CreateAsync_GetComInputContentTypeJson_NormalizaParaNone()
    {
        var (svc, repo) = Build();
        GenericTool? captured = null;
        repo.CreateAsync(Arg.Do<GenericTool>(t => captured = t), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<GenericTool>());

        var template = ValidGetTemplate();
        template.InputContentType = InputContentType.Json;
        template.InputSchema = "{\"type\":\"object\",\"properties\":{}}";

        await svc.CreateAsync(null, template);

        captured.Should().NotBeNull();
        captured!.InputContentType.Should().Be(InputContentType.None);
        captured.InputSchema.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_PlaceholderSemPathParam_Lanca400()
    {
        var (svc, _) = Build();
        var template = new GenericTool
        {
            Id = string.Empty,
            ProjectId = "x",
            TenantId = "y",
            Name = "broken",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api/aihub/{id}",
            OutputContentType = OutputContentType.Json,
            OutputSchema = "{\"type\":\"object\",\"properties\":{}}",
        };

        Func<Task> act = () => svc.CreateAsync(null, template);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task UpdateAsync_ToolNaoExiste_Lanca404()
    {
        var (svc, repo) = Build();
        repo.GetByIdAsync("missing", Arg.Any<CancellationToken>())
            .Returns((GenericTool?)null);

        Func<Task> act = () => svc.UpdateAsync("missing", ValidGetTemplate(), DateTime.UtcNow);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_NomeColidiCom_OutroTool_LancaConflict()
    {
        var (svc, repo) = Build();
        var existing = ValidGetTemplate(id: "tool-a", name: "old-name");
        repo.GetByIdAsync("tool-a", Arg.Any<CancellationToken>()).Returns(existing);
        repo.NameExistsAsync("new-name", "tool-a", Arg.Any<CancellationToken>()).Returns(true);

        var patch = ValidGetTemplate(name: "new-name");

        Func<Task> act = () => svc.UpdateAsync("tool-a", patch, existing.UpdatedAt);

        await act.Should().ThrowAsync<GenericToolNameConflictException>();
    }

    [Fact]
    public async Task UpdateAsync_MesmoNome_NaoChamaNameExists()
    {
        var (svc, repo) = Build();
        var existing = ValidGetTemplate(id: "tool-a");
        repo.GetByIdAsync("tool-a", Arg.Any<CancellationToken>()).Returns(existing);
        repo.UpdateAsync(Arg.Any<GenericTool>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<GenericTool>());

        var patch = ValidGetTemplate();

        await svc.UpdateAsync("tool-a", patch, existing.UpdatedAt);

        await repo.DidNotReceive().NameExistsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_PatchTentaTrocarOwnership_ServiceForcaDoExisting()
    {
        var (svc, repo) = Build(projectId: "real-owner", tenantId: "tenant-real");
        var existing = ValidGetTemplate(id: "tool-a", projectId: "real-owner", tenantId: "tenant-real");
        var existingCreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var withCreatedAt = new GenericTool
        {
            Id = existing.Id,
            ProjectId = existing.ProjectId,
            TenantId = existing.TenantId,
            Name = existing.Name,
            HttpMethod = existing.HttpMethod,
            UrlTemplate = existing.UrlTemplate,
            PathParams = existing.PathParams,
            OutputContentType = existing.OutputContentType,
            OutputSchema = existing.OutputSchema,
            CreatedAt = existingCreatedAt,
            UpdatedAt = existing.UpdatedAt,
        };
        repo.GetByIdAsync("tool-a", Arg.Any<CancellationToken>()).Returns(withCreatedAt);

        GenericTool? captured = null;
        repo.UpdateAsync(Arg.Do<GenericTool>(t => captured = t), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<GenericTool>());

        var hostilePatch = ValidGetTemplate(projectId: "attacker", tenantId: "tenant-evil");

        await svc.UpdateAsync("tool-a", hostilePatch, withCreatedAt.UpdatedAt);

        captured.Should().NotBeNull();
        captured!.ProjectId.Should().Be("real-owner");
        captured.TenantId.Should().Be("tenant-real");
        captured.CreatedAt.Should().Be(existingCreatedAt);
    }

    [Fact]
    public async Task UpdateAsync_OutputContentTypeText_ZeraOutputSchema()
    {
        var (svc, repo) = Build();
        var existing = ValidGetTemplate(id: "tool-a");
        repo.GetByIdAsync("tool-a", Arg.Any<CancellationToken>()).Returns(existing);

        GenericTool? captured = null;
        repo.UpdateAsync(Arg.Do<GenericTool>(t => captured = t), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<GenericTool>());

        var patch = ValidGetTemplate();
        patch.OutputContentType = OutputContentType.Text;
        patch.OutputSchema = "{\"type\":\"object\"}";

        await svc.UpdateAsync("tool-a", patch, existing.UpdatedAt);

        captured.Should().NotBeNull();
        captured!.OutputContentType.Should().Be(OutputContentType.Text);
        captured.OutputSchema.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ToolNaoExiste_Lanca404()
    {
        var (svc, repo) = Build();
        repo.GetByIdAsync("missing", Arg.Any<CancellationToken>())
            .Returns((GenericTool?)null);

        Func<Task> act = () => svc.DeleteAsync("missing");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_ToolExistente_RemoveERegistraLog()
    {
        var (svc, repo) = Build();
        var existing = ValidGetTemplate(id: "tool-a");
        repo.GetByIdAsync("tool-a", Arg.Any<CancellationToken>()).Returns(existing);
        repo.DeleteAsync("tool-a", Arg.Any<CancellationToken>()).Returns(true);

        await svc.DeleteAsync("tool-a");

        await repo.Received(1).DeleteAsync("tool-a", Arg.Any<CancellationToken>());
    }
}
