using EfsAiHub.Core.Abstractions.BackgroundServices;
using EfsAiHub.Host.Api.Configuration;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Decide se um conjunto de permissions concede acesso administrativo,
/// comparando contra a lista <c>Admin:AdminPermissions</c> de appsettings.
/// Semântica OR: qualquer match (case-insensitive) → admin.
///
/// As listas de entrada já vêm normalizadas (lowercase) pelo
/// <c>UserIdentityResolver.ParsePermissions</c>; a config é memoizada em um
/// <c>HashSet</c> imutável, recriado apenas quando o <see cref="IOptionsMonitor{T}"/>
/// reporta mudança. Hot-path = single dictionary lookup.
/// </summary>
public interface IAdminPermissionEvaluator
{
    bool IsAdmin(IReadOnlyList<string>? userPermissions);
}

public sealed class AdminPermissionEvaluator : IAdminPermissionEvaluator, IDisposable
{
    private readonly IDisposable? _changeSubscription;
    private volatile HashSet<string> _adminSet;

    public AdminPermissionEvaluator(IOptionsMonitor<AdminOptions> options)
    {
        _adminSet = Build(options.CurrentValue.AdminPermissions);
        _changeSubscription = options.OnChange(opts => _adminSet = Build(opts.AdminPermissions));
    }

    public bool IsAdmin(IReadOnlyList<string>? userPermissions)
    {
        if (userPermissions is null || userPermissions.Count == 0) return false;
        var adminSet = _adminSet;
        if (adminSet.Count == 0) return false;

        for (var i = 0; i < userPermissions.Count; i++)
        {
            if (adminSet.Contains(userPermissions[i])) return true;
        }
        return false;
    }

    public void Dispose() => _changeSubscription?.Dispose();

    private static HashSet<string> Build(IReadOnlyCollection<string>? source)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (source is null) return set;
        foreach (var p in source)
        {
            if (!string.IsNullOrWhiteSpace(p))
                set.Add(p.Trim());
        }
        return set;
    }
}

/// <summary>
/// Loga warning no startup quando <c>Admin:AdminPermissions</c> está vazia
/// fora de Development — sinaliza que ninguém vai virar admin (UI admin trancada).
/// </summary>
public sealed class AdminPermissionsStartupValidator : IHostedService
{
    private const string HeartbeatName = "AdminPermissionsStartupValidator";

    private readonly IOptionsMonitor<AdminOptions> _options;
    private readonly IHostEnvironment _env;
    private readonly IBackgroundServiceHeartbeatSink _heartbeat;
    private readonly ILogger<AdminPermissionsStartupValidator> _logger;

    public AdminPermissionsStartupValidator(
        IOptionsMonitor<AdminOptions> options,
        IHostEnvironment env,
        IBackgroundServiceHeartbeatSink heartbeat,
        ILogger<AdminPermissionsStartupValidator> logger)
    {
        _options = options;
        _env = env;
        _heartbeat = heartbeat;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _heartbeat.Started(HeartbeatName, DateTimeOffset.UtcNow);
        try
        {
            var perms = _options.CurrentValue.AdminPermissions;
            if ((perms is null || perms.Count == 0) && !_env.IsDevelopment())
            {
                _logger.LogWarning(
                    "Admin:AdminPermissions está vazia em ambiente {Environment} — nenhum usuário será reconhecido como admin.",
                    _env.EnvironmentName);
            }
            _heartbeat.RecordSuccess(HeartbeatName, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _heartbeat.RecordError(HeartbeatName, DateTimeOffset.UtcNow, ex);
            throw;
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
