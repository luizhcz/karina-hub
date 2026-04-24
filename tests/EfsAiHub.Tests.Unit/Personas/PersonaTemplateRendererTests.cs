using System.Globalization;
using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Platform.Runtime.Personalization;
using FluentAssertions;
using Xunit;

namespace EfsAiHub.Tests.Unit.Personas;

[Trait("Category", "Unit")]
public class PersonaTemplateRendererTests : IDisposable
{
    // F8 — trava CurrentUICulture em pt-BR pra testes que esperam "sim"/"não".
    // Cada runner pode ter default diferente (macOS = en-US, Linux = C),
    // mas produção roda com RequestLocalizationMiddleware definindo a culture
    // por request — default pt-BR ([ADR 007]).
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentUICulture;
    public PersonaTemplateRendererTests()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");
    }
    public void Dispose() => CultureInfo.CurrentUICulture = _originalCulture;

    private static ClientPersona MakeClient(
        string? clientName = null,
        string? suitability = null,
        string? suitabilityDesc = null,
        string? segment = null,
        string? country = null,
        bool isOffshore = false)
        => new("u1", clientName, suitability, suitabilityDesc, segment, country, isOffshore);

    private static AdminPersona MakeAdmin(
        string? username = null,
        string? partnerType = null,
        IReadOnlyList<string>? segments = null,
        IReadOnlyList<string>? institutions = null,
        bool isInternal = false,
        bool isWm = false,
        bool isMaster = false,
        bool isBroker = false)
        => new(
            "u1", username, partnerType,
            segments ?? Array.Empty<string>(),
            institutions ?? Array.Empty<string>(),
            isInternal, isWm, isMaster, isBroker);

    // ── Cliente ──────────────────────────────────────────────────────────────

    [Fact]
    public void Render_Client_AllPlaceholders_Substituted()
    {
        var persona = MakeClient("João", "conservador", "Perfil A", "private", "BR", true);
        var template = "{{client_name}} / {{suitability_level}} / {{suitability_description}} / "
                     + "{{business_segment}} / {{country}} / offshore={{is_offshore}} / tipo={{user_type}}";

        var result = PersonaTemplateRenderer.Render(template, persona);

        result.Should().Be("João / conservador / Perfil A / private / BR / offshore=sim / tipo=cliente");
    }

    [Fact]
    public void Render_Client_NullStringFields_ReplacedByEmpty()
    {
        var persona = MakeClient(clientName: null, segment: "private");
        var template = "N={{client_name}};S={{business_segment}};Suit={{suitability_level}}";

        var result = PersonaTemplateRenderer.Render(template, persona);

        result.Should().Be("N=;S=private;Suit=");
    }

    [Fact]
    public void Render_Client_OffshoreFalse_RendersNao()
    {
        var persona = MakeClient(clientName: "X", isOffshore: false);

        var result = PersonaTemplateRenderer.Render("off={{is_offshore}}", persona);

        result.Should().Be("off=não");
    }

    // ── Admin ────────────────────────────────────────────────────────────────

    [Fact]
    public void Render_Admin_AllPlaceholders_Substituted()
    {
        var persona = MakeAdmin(
            username: "ana",
            partnerType: "GESTOR",
            segments: new[] { "B2B", "WM" },
            institutions: new[] { "BTG", "EQI" },
            isInternal: true,
            isWm: true,
            isMaster: false,
            isBroker: true);
        var template =
            "{{username}}|{{partner_type}}|{{segments}}|{{institutions}}|"
          + "int={{is_internal}}|wm={{is_wm}}|master={{is_master}}|broker={{is_broker}}|t={{user_type}}";

        var result = PersonaTemplateRenderer.Render(template, persona);

        result.Should().Be("ana|GESTOR|B2B, WM|BTG, EQI|int=sim|wm=sim|master=não|broker=sim|t=admin");
    }

    [Fact]
    public void Render_Admin_EmptyListsRenderEmptyString()
    {
        var persona = MakeAdmin(
            username: "x",
            segments: Array.Empty<string>(),
            institutions: Array.Empty<string>());

        var result = PersonaTemplateRenderer.Render(
            "segs=[{{segments}}];inst=[{{institutions}}]", persona);

        result.Should().Be("segs=[];inst=[]");
    }

    // ── Regras comuns do renderer ────────────────────────────────────────────

    [Fact]
    public void Render_UnknownPlaceholder_LeftIntactToExposeTypos()
    {
        var persona = MakeClient(segment: "private");
        var template = "Seg: {{business_segment}}, typo: {{unknown_xxx}}";

        var result = PersonaTemplateRenderer.Render(template, persona);

        result.Should().Be("Seg: private, typo: {{unknown_xxx}}");
    }

    [Fact]
    public void Render_CaseSensitive()
    {
        var persona = MakeClient(segment: "private");

        var result = PersonaTemplateRenderer.Render(
            "{{Business_Segment}} vs {{business_segment}}", persona);

        result.Should().Be("{{Business_Segment}} vs private");
    }

    [Fact]
    public void Render_EmptyTemplate_ReturnsNull()
    {
        PersonaTemplateRenderer.Render("", MakeClient(segment: "x")).Should().BeNull();
        PersonaTemplateRenderer.Render(null, MakeClient(segment: "x")).Should().BeNull();
        PersonaTemplateRenderer.Render("   ", MakeClient(segment: "x")).Should().BeNull();
    }

    [Fact]
    public void Render_NullOrAnonymousPersona_ReturnsNull()
    {
        PersonaTemplateRenderer.Render("Seg: {{business_segment}}", null).Should().BeNull();
        PersonaTemplateRenderer.Render("Seg: {{business_segment}}",
            ClientPersona.Anonymous("u1")).Should().BeNull();
        PersonaTemplateRenderer.Render("Partner: {{partner_type}}",
            AdminPersona.Anonymous("u1")).Should().BeNull();
    }

    [Fact]
    public void Render_PlaceholderRepeated_AllOccurrencesReplaced()
    {
        var persona = MakeClient(segment: "private");

        var result = PersonaTemplateRenderer.Render(
            "{{business_segment}} / {{business_segment}} / {{business_segment}}", persona);

        result.Should().Be("private / private / private");
    }

    [Fact]
    public void Render_WhitespaceInsideBraces_IsSubstituted()
    {
        // {{ segment }} com espaços não é erro — regex tolera indentação acidental.
        // Importante pra admin não ver typo literal porque digitou com espaço.
        var persona = MakeClient(segment: "private");

        var result = PersonaTemplateRenderer.Render(
            "A={{business_segment}} B={{ business_segment }} C={{  business_segment  }}", persona);

        result.Should().Be("A=private B=private C=private");
    }

    [Fact]
    public void Render_WhitespaceIncludingNewlinesAndTabs_IsSubstituted()
    {
        // F3: \s* cobre espaço, tab e quebra de linha. Templates multiline
        // formatados (ex: export de editor) não viram typo literal acidental.
        var persona = MakeClient(segment: "private");

        var result = PersonaTemplateRenderer.Render(
            "A={{\nbusiness_segment\n}} B={{\tbusiness_segment\t}} C={{\n  business_segment  \n}}",
            persona);

        result.Should().Be("A=private B=private C=private");
    }

    [Fact]
    public void Render_Client_AdminPlaceholders_LeftIntact()
    {
        // Placeholder de admin num template aplicado a cliente não resolve —
        // fica literal pro admin da UI ver que errou o userType.
        var persona = MakeClient(segment: "private");

        var result = PersonaTemplateRenderer.Render(
            "{{business_segment}} + {{partner_type}}", persona);

        result.Should().Be("private + {{partner_type}}");
    }

    // ── F8: i18n do boolean format ───────────────────────────────────────────

    [Fact]
    public void Render_Client_IsOffshore_PtBr_RenderizaSim()
    {
        // Sem troca de culture = default pt-BR = "sim"/"não".
        var persona = MakeClient(isOffshore: true);
        var result = PersonaTemplateRenderer.Render("offshore={{is_offshore}}", persona);
        result.Should().Be("offshore=sim");
    }

    [Fact]
    public void Render_Client_IsOffshore_EnUs_RenderizaYes()
    {
        // F8 — CultureInfo.CurrentUICulture setado como en-US pelo
        // RequestLocalizationMiddleware em produção; aqui simulamos inline.
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            var persona = MakeClient(isOffshore: true);
            var result = PersonaTemplateRenderer.Render("offshore={{is_offshore}}", persona);
            result.Should().Be("offshore=yes");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void Render_Admin_TodosBooleanos_EnUs_UsamYesNo()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            var persona = MakeAdmin(
                isInternal: true, isWm: false, isMaster: true, isBroker: false);
            var result = PersonaTemplateRenderer.Render(
                "int={{is_internal}} wm={{is_wm}} master={{is_master}} broker={{is_broker}}",
                persona);
            result.Should().Be("int=yes wm=no master=yes broker=no");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
