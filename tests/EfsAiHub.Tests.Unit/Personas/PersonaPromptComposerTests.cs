using System.Globalization;
using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Platform.Runtime.Execution;
using EfsAiHub.Platform.Runtime.Personalization;
using FluentAssertions;
using Xunit;

namespace EfsAiHub.Tests.Unit.Personas;

[Trait("Category", "Unit")]
public class PersonaPromptComposerTests : IDisposable
{
    // F8 — trava CurrentUICulture em pt-BR pra testes que esperam "sim"/"não".
    // Produção usa RequestLocalizationMiddleware ([ADR 007]).
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentUICulture;
    public PersonaPromptComposerTests()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");
    }
    public void Dispose() => CultureInfo.CurrentUICulture = _originalCulture;

    private sealed class StubCache : IPersonaPromptTemplateCache
    {
        public Dictionary<string, PersonaPromptTemplate> Store { get; } = new();

        public ValueTask<PersonaPromptTemplate?> GetByScopeAsync(string scope, CancellationToken ct = default)
            => ValueTask.FromResult(Store.TryGetValue(scope, out var tpl) ? tpl : null);

        public Task InvalidateAsync(string? scope = null)
        {
            if (scope is null) Store.Clear();
            else Store.Remove(scope);
            return Task.CompletedTask;
        }
    }

    private static PersonaPromptTemplate Tpl(string scope, string template)
        => new() { Scope = scope, Name = $"Test {scope}", Template = template };

    private static ClientPersona MakeClient(
        string? name = "João",
        string? suitability = "moderado",
        string? segment = "private",
        string? country = "BR",
        bool isOffshore = false)
        => new("u1", name, suitability, null, segment, country, isOffshore);

    private static AdminPersona MakeAdmin(
        string? username = "assessor-1",
        string? partnerType = "ADVISORS",
        string[]? segments = null,
        string[]? institutions = null,
        bool isInternal = false,
        bool isWm = false,
        bool isMaster = false,
        bool isBroker = false)
        => new(
            "u1", username, partnerType,
            segments ?? new[] { "B2B", "WM" },
            institutions ?? new[] { "BTG" },
            isInternal, isWm, isMaster, isBroker);

    // ── Fluxo comum ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Compose_NullPersona_ReturnsEmpty()
    {
        var composer = new PersonaPromptComposer(new StubCache());

        var result = await composer.ComposeAsync(null, agentId: null);

        result.HasAnyContent.Should().BeFalse();
    }

    [Fact]
    public async Task Compose_AnonymousClientPersona_ReturnsEmpty()
    {
        var composer = new PersonaPromptComposer(new StubCache());

        var result = await composer.ComposeAsync(
            ClientPersona.Anonymous("u1"), agentId: "any");

        result.HasAnyContent.Should().BeFalse();
    }

    [Fact]
    public async Task Compose_AnonymousAdminPersona_ReturnsEmpty()
    {
        var composer = new PersonaPromptComposer(new StubCache());

        var result = await composer.ComposeAsync(
            AdminPersona.Anonymous("u1"), agentId: "any");

        result.HasAnyContent.Should().BeFalse();
    }

    [Fact]
    public async Task Compose_NoTemplateAtAll_ReturnsEmpty()
    {
        var composer = new PersonaPromptComposer(new StubCache());

        var result = await composer.ComposeAsync(MakeClient(), agentId: "agent-x");

        result.HasAnyContent.Should().BeFalse();
    }

    // ── Cliente ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Compose_Client_UsesGlobalClienteTemplate()
    {
        var cache = new StubCache();
        cache.Store["global:cliente"] = Tpl("global:cliente",
            "Suitability: {{suitability_level}} / Segmento: {{business_segment}}");
        var composer = new PersonaPromptComposer(cache);

        var result = await composer.ComposeAsync(MakeClient(), agentId: "agent-x");

        result.SystemSection.Should().Be("Suitability: moderado / Segmento: private");
    }

    [Fact]
    public async Task Compose_Client_AgentScopeWithUserTypeWinsOverGlobal()
    {
        var cache = new StubCache();
        cache.Store["global:cliente"] = Tpl("global:cliente", "GLOBAL: {{business_segment}}");
        cache.Store["agent:atendimento:cliente"] =
            Tpl("agent:atendimento:cliente", "AGENT: {{business_segment}}");
        var composer = new PersonaPromptComposer(cache);

        var result = await composer.ComposeAsync(MakeClient(), agentId: "atendimento");

        result.SystemSection.Should().StartWith("AGENT:");
        result.SystemSection.Should().NotContain("GLOBAL");
    }

    [Fact]
    public async Task Compose_Client_ProjectAgentScopeWinsOverAll()
    {
        // F4 cadeia de 5 níveis — project:{pid}:agent:{aid}:{ut} é o mais específico
        // e deve ganhar sobre project:{pid}:{ut}, agent:{aid}:{ut} e global:{ut}.
        var cache = new StubCache();
        cache.Store["global:cliente"] = Tpl("global:cliente", "GLOBAL");
        cache.Store["agent:at:cliente"] = Tpl("agent:at:cliente", "AGENT");
        cache.Store["project:pA:cliente"] = Tpl("project:pA:cliente", "PROJECT");
        cache.Store["project:pA:agent:at:cliente"] =
            Tpl("project:pA:agent:at:cliente", "PROJECT_AGENT");
        var composer = new PersonaPromptComposer(cache);

        var result = await composer.ComposeAsync(MakeClient(), agentId: "at", projectId: "pA");

        result.SystemSection.Should().Be("PROJECT_AGENT");
    }

    [Fact]
    public async Task Compose_Client_ProjectScopeWinsOverAgentWhenNoProjectAgent()
    {
        // Sem project:{pid}:agent, cai pra project:{pid}:{ut} antes de
        // agent:{aid}:{ut} ou global:{ut}.
        var cache = new StubCache();
        cache.Store["global:cliente"] = Tpl("global:cliente", "GLOBAL");
        cache.Store["agent:at:cliente"] = Tpl("agent:at:cliente", "AGENT");
        cache.Store["project:pA:cliente"] = Tpl("project:pA:cliente", "PROJECT");
        var composer = new PersonaPromptComposer(cache);

        var result = await composer.ComposeAsync(MakeClient(), agentId: "at", projectId: "pA");

        result.SystemSection.Should().Be("PROJECT");
    }

    [Fact]
    public async Task Compose_Client_ProjectScopeIgnoredWhenProjectIdNull()
    {
        // projectId null → composer pula níveis 1 e 2 da cadeia (project-aware)
        // e vai direto pra agent/global. Garante retrocompat pré-F4.
        var cache = new StubCache();
        cache.Store["project:pA:cliente"] = Tpl("project:pA:cliente", "PROJECT");
        cache.Store["global:cliente"] = Tpl("global:cliente", "GLOBAL");
        var composer = new PersonaPromptComposer(cache);

        var result = await composer.ComposeAsync(MakeClient(), agentId: null, projectId: null);

        result.SystemSection.Should().Be("GLOBAL");
    }

    [Fact]
    public async Task Compose_Client_AdminScopedTemplateIsIgnoredForClientUser()
    {
        // Scope paralelo (admin) não contamina cliente.
        var cache = new StubCache();
        cache.Store["global:admin"] = Tpl("global:admin", "ADMIN-ONLY");
        var composer = new PersonaPromptComposer(cache);

        var result = await composer.ComposeAsync(MakeClient(), agentId: null);

        result.HasAnyContent.Should().BeFalse();
    }

    [Fact]
    public async Task Compose_Client_ReinforcementUsesSuitabilityAndSegment()
    {
        var cache = new StubCache();
        cache.Store["global:cliente"] = Tpl("global:cliente", "ignored");
        var composer = new PersonaPromptComposer(cache);

        var result = await composer.ComposeAsync(MakeClient(), agentId: null);

        result.UserReinforcement.Should().Contain("persona.suitability=moderado");
        result.UserReinforcement.Should().Contain("persona.segment=private");
        result.UserReinforcement!.Length.Should().BeLessThan(80);
    }

    [Fact]
    public async Task Compose_Client_WithoutSuitabilityOrSegment_NoReinforcement()
    {
        var cache = new StubCache();
        cache.Store["global:cliente"] = Tpl("global:cliente", "Olá {{client_name}}");
        var composer = new PersonaPromptComposer(cache);
        var persona = new ClientPersona("u1", "João", null, null, null, null, false);

        var result = await composer.ComposeAsync(persona, agentId: null);

        result.SystemSection.Should().Be("Olá João");
        result.UserReinforcement.Should().BeNull();
    }

    [Fact]
    public async Task Compose_Client_OffshoreBooleanRendersAsSimOrNao()
    {
        var cache = new StubCache();
        cache.Store["global:cliente"] = Tpl("global:cliente", "offshore={{is_offshore}}");
        var composer = new PersonaPromptComposer(cache);

        var onshore = await composer.ComposeAsync(MakeClient(isOffshore: false), agentId: null);
        var offshore = await composer.ComposeAsync(MakeClient(isOffshore: true), agentId: null);

        onshore.SystemSection.Should().Be("offshore=não");
        offshore.SystemSection.Should().Be("offshore=sim");
    }

    // ── Admin ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Compose_Admin_UsesGlobalAdminTemplate()
    {
        var cache = new StubCache();
        cache.Store["global:admin"] = Tpl("global:admin",
            "Partner: {{partner_type}} / Internal: {{is_internal}} / Inst: {{institutions}}");
        var composer = new PersonaPromptComposer(cache);

        var result = await composer.ComposeAsync(
            MakeAdmin(isInternal: true, institutions: new[] { "BTG", "EQI" }),
            agentId: null);

        result.SystemSection.Should().Be("Partner: ADVISORS / Internal: sim / Inst: BTG, EQI");
    }

    [Fact]
    public async Task Compose_Admin_AgentScopeWithUserTypeWinsOverGlobal()
    {
        var cache = new StubCache();
        cache.Store["global:admin"] = Tpl("global:admin", "GLOBAL: {{partner_type}}");
        cache.Store["agent:backoffice:admin"] =
            Tpl("agent:backoffice:admin", "AGENT: {{partner_type}}");
        var composer = new PersonaPromptComposer(cache);

        var result = await composer.ComposeAsync(MakeAdmin(), agentId: "backoffice");

        result.SystemSection.Should().StartWith("AGENT:");
    }

    [Fact]
    public async Task Compose_Admin_ReinforcementUsesPartnerType()
    {
        var cache = new StubCache();
        cache.Store["global:admin"] = Tpl("global:admin", "ignored");
        var composer = new PersonaPromptComposer(cache);

        var result = await composer.ComposeAsync(MakeAdmin(), agentId: null);

        result.UserReinforcement.Should().Contain("persona.partner=ADVISORS");
        result.UserReinforcement!.Length.Should().BeLessThan(80);
    }

    [Fact]
    public async Task Compose_Admin_WmFlagAppendsReinforcement()
    {
        var cache = new StubCache();
        cache.Store["global:admin"] = Tpl("global:admin", "ignored");
        var composer = new PersonaPromptComposer(cache);

        var result = await composer.ComposeAsync(MakeAdmin(isWm: true), agentId: null);

        result.UserReinforcement.Should().Contain("persona.partner=ADVISORS");
        result.UserReinforcement.Should().Contain("persona.wm=sim");
    }

    [Fact]
    public async Task Compose_Admin_SegmentsListRendersAsCsv()
    {
        var cache = new StubCache();
        cache.Store["global:admin"] = Tpl("global:admin", "segs={{segments}}");
        var composer = new PersonaPromptComposer(cache);

        var result = await composer.ComposeAsync(
            MakeAdmin(segments: new[] { "B2B", "WM", "IB" }), agentId: null);

        result.SystemSection.Should().Be("segs=B2B, WM, IB");
    }

    [Fact]
    public async Task Compose_Admin_EmptySegmentsListRendersAsEmptyString()
    {
        var cache = new StubCache();
        cache.Store["global:admin"] = Tpl("global:admin", "segs=[{{segments}}]");
        var composer = new PersonaPromptComposer(cache);

        var result = await composer.ComposeAsync(
            MakeAdmin(segments: Array.Empty<string>()), agentId: null);

        result.SystemSection.Should().Be("segs=[]");
    }

    // ── F6: A/B testing via experiments ──────────────────────────────────────

    private sealed class StubExperimentRepo : IPersonaPromptExperimentRepository
    {
        public Dictionary<(string projectId, string scope), PersonaPromptExperiment> Active { get; } = new();

        public Task<PersonaPromptExperiment?> GetActiveAsync(
            string projectId, string scope, CancellationToken ct = default)
            => Task.FromResult(Active.TryGetValue((projectId, scope), out var x) ? x : null);

        public Task<PersonaPromptExperiment?> GetByIdAsync(int id, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<PersonaPromptExperiment>> GetByProjectAsync(
            string projectId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<PersonaPromptExperiment> CreateAsync(
            PersonaPromptExperiment experiment, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> EndAsync(int id, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> DeleteAsync(int id, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<ExperimentVariantResult>> GetResultsAsync(
            int experimentId, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class StubTemplateRepo : IPersonaPromptTemplateRepository
    {
        public Dictionary<Guid, PersonaPromptTemplateVersion> Versions { get; } = new();

        public Task<PersonaPromptTemplateVersion?> GetVersionByIdAsync(
            Guid versionId, CancellationToken ct = default)
            => Task.FromResult(Versions.TryGetValue(versionId, out var v) ? v : null);

        // Resto não é usado pelo composer.
        public Task<PersonaPromptTemplate?> GetByScopeAsync(string scope, CancellationToken ct = default) => Task.FromResult<PersonaPromptTemplate?>(null);
        public Task<PersonaPromptTemplate?> GetByIdAsync(int id, CancellationToken ct = default) => Task.FromResult<PersonaPromptTemplate?>(null);
        public Task<IReadOnlyList<PersonaPromptTemplate>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PersonaPromptTemplate>>(Array.Empty<PersonaPromptTemplate>());
        public Task<PersonaPromptTemplate> UpsertAsync(PersonaPromptTemplate template, string? createdBy = null, string? changeReason = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<PersonaPromptTemplateVersion>> GetVersionsAsync(int templateId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PersonaPromptTemplateVersion>>(Array.Empty<PersonaPromptTemplateVersion>());
        public Task<PersonaPromptTemplate?> RollbackAsync(int templateId, Guid targetVersionId, string? createdBy = null, CancellationToken ct = default) => Task.FromResult<PersonaPromptTemplate?>(null);
    }

    [Fact]
    public async Task Compose_Experiment_UsesVariantVersionContent()
    {
        // Template ActiveVersion tem conteúdo "ACTIVE". Experiment ativo aponta
        // pra variant A com conteúdo "VARIANT_A" e B com "VARIANT_B". Com split=0
        // (100% pra A), composer deve renderizar VARIANT_A — NÃO ACTIVE.
        var scope = "project:p1:cliente";
        var cache = new StubCache();
        cache.Store[scope] = Tpl(scope, "ACTIVE content");
        var variantA = Guid.NewGuid();
        var variantB = Guid.NewGuid();
        var templateRepo = new StubTemplateRepo();
        templateRepo.Versions[variantA] = new PersonaPromptTemplateVersion
        {
            Id = 1, TemplateId = 1, VersionId = variantA,
            Template = "VARIANT_A content",
            CreatedAt = DateTime.UtcNow,
        };
        templateRepo.Versions[variantB] = new PersonaPromptTemplateVersion
        {
            Id = 2, TemplateId = 1, VersionId = variantB,
            Template = "VARIANT_B content",
            CreatedAt = DateTime.UtcNow,
        };
        var experiments = new StubExperimentRepo();
        experiments.Active[("p1", scope)] = new PersonaPromptExperiment
        {
            Id = 42, ProjectId = "p1", Scope = scope, Name = "x",
            VariantAVersionId = variantA,
            VariantBVersionId = variantB,
            TrafficSplitB = 0, // 100% pra A
            Metric = "cost_usd",
            StartedAt = DateTime.UtcNow,
        };

        var composer = new PersonaPromptComposer(cache, experiments, templateRepo);

        var result = await composer.ComposeAsync(MakeClient(), agentId: null, projectId: "p1");

        result.SystemSection.Should().Be("VARIANT_A content");
        result.SystemSection.Should().NotContain("ACTIVE");
    }

    [Fact]
    public async Task Compose_Experiment_Split100ForcesVariantB()
    {
        var scope = "project:p1:cliente";
        var cache = new StubCache();
        cache.Store[scope] = Tpl(scope, "ACTIVE");
        var variantA = Guid.NewGuid();
        var variantB = Guid.NewGuid();
        var templateRepo = new StubTemplateRepo();
        templateRepo.Versions[variantA] = new PersonaPromptTemplateVersion
        { Id = 1, TemplateId = 1, VersionId = variantA, Template = "A", CreatedAt = DateTime.UtcNow };
        templateRepo.Versions[variantB] = new PersonaPromptTemplateVersion
        { Id = 2, TemplateId = 1, VersionId = variantB, Template = "B", CreatedAt = DateTime.UtcNow };
        var experiments = new StubExperimentRepo();
        experiments.Active[("p1", scope)] = new PersonaPromptExperiment
        {
            Id = 42, ProjectId = "p1", Scope = scope, Name = "x",
            VariantAVersionId = variantA, VariantBVersionId = variantB,
            TrafficSplitB = 100, // 100% pra B
            Metric = "cost_usd", StartedAt = DateTime.UtcNow,
        };

        var composer = new PersonaPromptComposer(cache, experiments, templateRepo);

        var result = await composer.ComposeAsync(MakeClient(), agentId: null, projectId: "p1");

        result.SystemSection.Should().Be("B");
    }

    [Fact]
    public async Task Compose_Experiment_OrphanedVariant_DegradeParaActiveVersion()
    {
        // VersionId do experiment não existe no templateRepo → composer degrada
        // pro template corrente (ACTIVE) em vez de explodir. Comportamento
        // documentado no ADR 005; garantido aqui pra não regredir.
        var scope = "project:p1:cliente";
        var cache = new StubCache();
        cache.Store[scope] = Tpl(scope, "ACTIVE_FALLBACK");
        var orphanA = Guid.NewGuid();
        var orphanB = Guid.NewGuid();
        var templateRepo = new StubTemplateRepo(); // vazio — nenhum version registrado
        var experiments = new StubExperimentRepo();
        experiments.Active[("p1", scope)] = new PersonaPromptExperiment
        {
            Id = 77, ProjectId = "p1", Scope = scope, Name = "orphan-test",
            VariantAVersionId = orphanA, VariantBVersionId = orphanB,
            TrafficSplitB = 50, Metric = "cost_usd", StartedAt = DateTime.UtcNow,
        };

        var composer = new PersonaPromptComposer(cache, experiments, templateRepo);
        var result = await composer.ComposeAsync(MakeClient(), agentId: null, projectId: "p1");

        result.SystemSection.Should().Be("ACTIVE_FALLBACK");
    }

    [Fact]
    public async Task Compose_Experiment_WithoutProjectId_Skipped()
    {
        // Sem projectId no contexto, experiment não é consultado — template
        // ativo é usado normalmente. Garante compat pré-F4 / system contexts.
        var cache = new StubCache();
        cache.Store["global:cliente"] = Tpl("global:cliente", "GLOBAL_ACTIVE");
        var experiments = new StubExperimentRepo();
        // Poluir o repo — não deveria ser consultado.
        experiments.Active[("", "global:cliente")] = new PersonaPromptExperiment
        {
            Id = 1, ProjectId = "", Scope = "global:cliente", Name = "poison",
            VariantAVersionId = Guid.NewGuid(), VariantBVersionId = Guid.NewGuid(),
            TrafficSplitB = 50, Metric = "x", StartedAt = DateTime.UtcNow,
        };

        var composer = new PersonaPromptComposer(cache, experiments, new StubTemplateRepo());

        var result = await composer.ComposeAsync(MakeClient(), agentId: null, projectId: null);

        result.SystemSection.Should().Be("GLOBAL_ACTIVE");
    }

    [Fact]
    public void AssignVariant_Split50_DistribuicaoAprox50_50()
    {
        // Bucketing determinístico: 200 userIds distintos com split 50% devem
        // distribuir ~50/50 (±10% = range [90, 110] pra A e B).
        int countA = 0, countB = 0;
        for (int i = 0; i < 200; i++)
        {
            var variant = ExperimentAssignment.AssignVariant(
                userId: $"user-{i}", experimentId: 1, trafficSplitB: 50);
            if (variant == 'A') countA++;
            else if (variant == 'B') countB++;
        }

        countA.Should().BeInRange(80, 120, because: "split 50 com 200 amostras");
        countB.Should().BeInRange(80, 120, because: "split 50 com 200 amostras");
        (countA + countB).Should().Be(200);
    }

    [Fact]
    public void AssignVariant_MesmoUserId_SempreMesmaVariant()
    {
        // Sticky assignment: retries/multi-turn não alternam variant.
        const string userId = "stable-user";
        const int expId = 42;
        var v1 = ExperimentAssignment.AssignVariant(userId, expId, trafficSplitB: 50);
        for (int i = 0; i < 10; i++)
        {
            ExperimentAssignment.AssignVariant(userId, expId, trafficSplitB: 50)
                .Should().Be(v1);
        }
    }

    [Fact]
    public void AssignVariant_Split0_SempreA()
    {
        for (int i = 0; i < 50; i++)
        {
            ExperimentAssignment.AssignVariant($"u-{i}", 1, trafficSplitB: 0)
                .Should().Be('A');
        }
    }

    [Fact]
    public void AssignVariant_Split100_SempreB()
    {
        for (int i = 0; i < 50; i++)
        {
            ExperimentAssignment.AssignVariant($"u-{i}", 1, trafficSplitB: 100)
                .Should().Be('B');
        }
    }
}
