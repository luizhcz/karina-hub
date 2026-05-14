using System.Diagnostics;
using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Infra.Observability;
using EfsAiHub.Platform.Runtime.Configuration;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Platform.Runtime.Tools.Generic;

public sealed class GenericToolExecutor : IGenericToolExecutor
{
    private const string ClientName = "generic-tool-executor";
    private const string GenericFailureMessage = "Não foi possível executar o request do tool";
    private const string UnauthorizedFailureMessage =
        "Você não tem permissão para usar esta ferramenta. Verifique com o administrador.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<GenericToolsOptions> _options;
    private readonly IRequestAuthContextAccessor _authContext;
    private readonly ILogger<GenericToolExecutor> _logger;

    public GenericToolExecutor(
        IHttpClientFactory httpClientFactory,
        IOptions<GenericToolsOptions> options,
        IRequestAuthContextAccessor authContext,
        ILogger<GenericToolExecutor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _authContext = authContext;
        _logger = logger;
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        GenericTool tool,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var success = false;
        var statusClass = "error";

        try
        {
            var timeout = ResolveTimeout(tool);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeout));

            using var request = GenericRequestBuilder.Build(tool, args);

            // Forward de credenciais do caller pra que o downstream autorize a
            // ferramenta com base no token do usuário (não no token de service
            // do hub). 401 do downstream = caller sem permissão pra essa tool.
            // CustomHeaders do próprio tool têm precedência (já adicionados em
            // GenericRequestBuilder) — só preenchemos quando ausentes.
            if (!string.IsNullOrWhiteSpace(_authContext.AppOrigin)
                && !request.Headers.Contains("app_origin"))
            {
                request.Headers.TryAddWithoutValidation("app_origin", _authContext.AppOrigin);
            }
            if (!string.IsNullOrWhiteSpace(_authContext.AccessToken)
                && !request.Headers.Contains("access_token"))
            {
                request.Headers.TryAddWithoutValidation("access_token", _authContext.AccessToken);
            }

            var client = _httpClientFactory.CreateClient(ClientName);

            using var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);
            statusClass = ClassifyStatus((int)response.StatusCode);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "[GenericToolExecutor] Tool '{ToolId}' barrado pelo downstream (status {StatusCode}). Caller sem permissão.",
                    tool.Id, (int)response.StatusCode);
                return ToolExecutionResult.Fail(UnauthorizedFailureMessage);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[GenericToolExecutor] Tool '{ToolId}' retornou status {StatusCode} ({StatusClass}).",
                    tool.Id, (int)response.StatusCode, statusClass);
                return ToolExecutionResult.Fail(GenericFailureMessage);
            }

            var data = await GenericResponseParser
                .ParseAsync(response.Content, tool.OutputContentType, cts.Token)
                .ConfigureAwait(false);

            success = true;
            return ToolExecutionResult.Ok(data);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "[GenericToolExecutor] Tool '{ToolId}' excedeu timeout.", tool.Id);
            return ToolExecutionResult.Fail(GenericFailureMessage);
        }
        catch (OperationCanceledException)
        {
            // Caller cancelou — propagar é OK porque agente foi cancelado também.
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "[GenericToolExecutor] Tool '{ToolId}' falhou em rede.", tool.Id);
            return ToolExecutionResult.Fail(GenericFailureMessage);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex,
                "[GenericToolExecutor] Tool '{ToolId}' retornou JSON inválido.", tool.Id);
            return ToolExecutionResult.Fail(GenericFailureMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[GenericToolExecutor] Tool '{ToolId}' falhou com erro inesperado.", tool.Id);
            return ToolExecutionResult.Fail(GenericFailureMessage);
        }
        finally
        {
            sw.Stop();
            MetricsRegistry.GenericToolInvocations.Add(1,
                new KeyValuePair<string, object?>("tool_id", tool.Id),
                new KeyValuePair<string, object?>("project", tool.ProjectId),
                new KeyValuePair<string, object?>("success", success),
                new KeyValuePair<string, object?>("status_class", statusClass));
            MetricsRegistry.GenericToolDurationMs.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("tool_id", tool.Id),
                new KeyValuePair<string, object?>("success", success));
        }
    }

    private int ResolveTimeout(GenericTool tool)
    {
        var max = _options.Value.MaxTimeoutSeconds;
        var seconds = tool.TimeoutSecondsOverride ?? _options.Value.DefaultTimeoutSeconds;
        return Math.Min(seconds, max);
    }

    private static string ClassifyStatus(int statusCode) => statusCode switch
    {
        >= 200 and < 300 => "2xx",
        >= 300 and < 400 => "3xx",
        >= 400 and < 500 => "4xx",
        >= 500 => "5xx",
        _ => "error",
    };
}
