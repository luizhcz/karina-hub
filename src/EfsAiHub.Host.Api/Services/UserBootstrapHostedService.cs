using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Host.Api.Configuration;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Garante a existência dos admins de bootstrap no startup. Pra cada
/// ExternalUserId em <see cref="AdminOptions.BootstrapAdminExternalUserIds"/>,
/// faz upsert na tabela aihub.users e força IsAdmin=TRUE.
///
/// Idempotente — rodar várias vezes não duplica registros nem corrompe
/// estado. Sobrescreve IsAdmin a cada startup, o que protege o ambiente
/// contra cenário "último admin se demoveu por engano via UI": basta
/// reiniciar pra recuperar o acesso, sem mexer em SQL.
///
/// Falha de DB no startup é logada e propaga — sem admins de bootstrap a
/// UI admin fica inacessível e o operador precisa investigar.
/// </summary>
public sealed class UserBootstrapHostedService : IHostedService
{
    private readonly IUserDirectory _directory;
    private readonly AdminOptions _options;
    private readonly ILogger<UserBootstrapHostedService> _logger;

    public UserBootstrapHostedService(
        IUserDirectory directory,
        IOptions<AdminOptions> options,
        ILogger<UserBootstrapHostedService> logger)
    {
        _directory = directory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (_options.BootstrapAdminExternalUserIds.Count == 0)
        {
            _logger.LogInformation("UserBootstrap: nenhum admin de bootstrap configurado.");
            return;
        }

        var tenantId = _options.BootstrapTenantId;
        var userType = _options.BootstrapUserType;

        foreach (var externalId in _options.BootstrapAdminExternalUserIds)
        {
            if (string.IsNullOrWhiteSpace(externalId)) continue;
            try
            {
                var result = await _directory.UpsertAsync(externalId, userType, tenantId, displayName: null, ct);
                if (!result.User.IsAdmin)
                {
                    await _directory.SetAdminAsync(result.User.Id, isAdmin: true, ct);
                    _logger.LogInformation(
                        "UserBootstrap: promovido externalUserId={ExternalUserId} tenant={TenantId} (estava IsAdmin=false).",
                        externalId, tenantId);
                }
                else
                {
                    _logger.LogDebug(
                        "UserBootstrap: externalUserId={ExternalUserId} tenant={TenantId} já é admin.",
                        externalId, tenantId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "UserBootstrap falhou pra externalUserId={ExternalUserId} tenant={TenantId}",
                    externalId, tenantId);
                throw;
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
