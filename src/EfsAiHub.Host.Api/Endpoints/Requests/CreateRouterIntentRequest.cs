using EfsAiHub.Core.Agents.RouterIntents;

namespace EfsAiHub.Host.Api.Models.Requests;

public sealed class CreateRouterIntentRequest
{
    /// <summary>Id opcional. Se ausente, service gera GUID.</summary>
    public string? Id { get; init; }

    /// <summary>Nome canônico (snake_case). Quando ausente, service usa o DisplayName slugificado.</summary>
    public string? Name { get; init; }

    /// <summary>Texto humano editável (PT-BR). Vai pro DisplayName.</summary>
    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    /// <summary>Categoria = ProjectId. Quando ausente, service usa o projeto do contexto.</summary>
    public string? ProjectId { get; init; }

    public IReadOnlyList<string>? Examples { get; init; }

    /// <summary>
    /// Hidrata um <see cref="RouterIntent"/> em estado "draft" — TenantId é
    /// preenchido pelo service a partir do contexto, e CreatedAt/UpdatedAt
    /// são atribuídos no <c>CreateAsync</c>.
    /// </summary>
    public RouterIntent ToDomainDraft() => new()
    {
        Id = Id ?? string.Empty,
        TenantId = string.Empty,
        ProjectId = ProjectId ?? string.Empty,
        Name = Name ?? string.Empty,
        DisplayName = DisplayName,
        Description = Description,
        Examples = Examples ?? Array.Empty<string>(),
    };
}

public sealed class UpdateRouterIntentRequest
{
    public string? Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }

    /// <summary>Quando ausente, service preserva o ProjectId atual da intent.</summary>
    public string? ProjectId { get; init; }

    public IReadOnlyList<string>? Examples { get; init; }

    public RouterIntent ToDomainPatch() => new()
    {
        Id = string.Empty,
        TenantId = string.Empty,
        ProjectId = ProjectId ?? string.Empty,
        Name = Name ?? string.Empty,
        DisplayName = DisplayName,
        Description = Description,
        Examples = Examples ?? Array.Empty<string>(),
    };
}

public sealed class AnalyzeRouterIntentRequest
{
    public required string Description { get; init; }
    public IReadOnlyList<string>? Examples { get; init; }
    public string? NameHint { get; init; }
    public string? DisplayNameHint { get; init; }
    public string? ProjectIdHint { get; init; }

    /// <summary>Quando edit: id da intent sendo editada (exclui ela do conjunto comparado).</summary>
    public string? ExcludeId { get; init; }
}
