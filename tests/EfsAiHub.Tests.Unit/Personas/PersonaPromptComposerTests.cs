using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Platform.Runtime.Execution;
using EfsAiHub.Platform.Runtime.Personalization;
using FluentAssertions;
using Xunit;

namespace EfsAiHub.Tests.Unit.Personas;

[Trait("Category", "Unit")]
public class PersonaPromptComposerTests
{
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
}
