using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;
using Microsoft.AspNetCore.Http;

namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Helper compartilhado pelos controllers admin — resolve a identidade do actor
/// pelo mesmo fluxo de headers do UserIdentityResolver e monta o AdminAuditEntry
/// já com TenantId/ProjectId do contexto da request. Mantém o call site dos
/// controllers curto: `await _audit.RecordAsync(_auditContext.Build(...));`.
/// </summary>
public sealed class AdminAuditContext
{
    private readonly UserIdentityResolver _identity;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly IHttpContextAccessor _http;

    public AdminAuditContext(
        UserIdentityResolver identity,
        ITenantContextAccessor tenantAccessor,
        IProjectContextAccessor projectAccessor,
        IHttpContextAccessor http)
    {
        _identity = identity;
        _tenantAccessor = tenantAccessor;
        _projectAccessor = projectAccessor;
        _http = http;
    }

    /// <summary>
    /// Monta um AdminAuditEntry com actor/tenant/project do contexto atual.
    /// Actor default "system:anonymous" quando headers ausentes — cobre jobs/scripts
    /// sem identidade. Controllers que exigem admin gate já validaram identidade antes.
    /// </summary>
    public AdminAuditEntry Build(
        string action,
        string resourceType,
        string resourceId,
        JsonDocument? payloadBefore = null,
        JsonDocument? payloadAfter = null)
    {
        var headers = _http.HttpContext?.Request.Headers;
        UserIdentityResolver.UserIdentity? identity = null;
        if (headers is not null)
            identity = _identity.TryResolve(headers, out _);

        return new AdminAuditEntry
        {
            TenantId = _tenantAccessor.Current.TenantId,
            ProjectId = _projectAccessor.Current.ProjectId,
            ActorUserId = identity?.UserId ?? "system:anonymous",
            ActorUserType = identity?.UserType,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            PayloadBefore = payloadBefore,
            PayloadAfter = payloadAfter,
            Timestamp = DateTime.UtcNow,
        };
    }

    /// <summary>Serializa um objeto para JsonDocument (para usar em PayloadBefore/After).</summary>
    public static JsonDocument? Snapshot(object? value)
    {
        if (value is null) return null;
        try
        {
            var json = JsonSerializer.Serialize(value);
            return JsonDocument.Parse(json);
        }
        catch
        {
            // Payloads complexos com JsonDocument/JsonElement embutidos podem falhar;
            // auditoria não deve bloquear o fluxo — devolve null e segue.
            return null;
        }
    }
}
