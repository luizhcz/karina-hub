using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using EfsAiHub.Core.Agents.Middlewares;
using EfsAiHub.Core.Orchestration.Executors;

namespace EfsAiHub.Platform.Runtime.Middlewares;

/// <summary>
/// Middleware que protege a conta do cliente no output do agente.
/// Extrai a conta original do input (ChatTurnContext.UserId) e, se o LLM
/// substituir por outra conta na resposta, corrige de volta para a original.
///
/// Ativação: middleware type "AccountGuard" na definição do agente.
/// Settings opcionais:
///   - "accountPattern": regex custom para detectar contas (default: \b\d{5,10}\b)
/// </summary>
public partial class AccountGuardChatClient : AgentMiddlewareBase
{
    private readonly string? _accountPattern;

    public AccountGuardChatClient(
        IChatClient innerClient,
        string agentId,
        Dictionary<string, string>? settings,
        ILogger logger)
        : base(innerClient, agentId, settings ?? new Dictionary<string, string>(), logger)
    {
        _accountPattern = settings?.GetValueOrDefault("accountPattern");
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var originalAccount = ExtractOriginalAccount(messages);
        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        if (originalAccount is not null)
            GuardResponse(response, originalAccount);

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var originalAccount = ExtractOriginalAccount(messages);

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (originalAccount is not null && update.Text is not null)
            {
                var replaced = ReplaceAccounts(update.Text, originalAccount);
                if (replaced != update.Text)
                {
                    Logger.LogWarning(
                        "[AccountGuard] Agent '{AgentId}': conta alterada detectada no stream — corrigindo para '{Account}'.",
                        AgentId, originalAccount);

                    // Substitui itens TextContent na lista de Contents
                    for (var i = 0; i < update.Contents.Count; i++)
                    {
                        if (update.Contents[i] is TextContent tc)
                            update.Contents[i] = new TextContent(ReplaceAccounts(tc.Text, originalAccount));
                    }
                }
            }
            yield return update;
        }
    }

    /// <summary>
    /// Extrai a conta original de 3 fontes (em ordem de prioridade):
    /// 1. AsyncLocal CurrentExecutionInput (ChatTurnContext JSON → UserId)
    /// 2. System message contendo padrão "conta: XXXXX" ou "account: XXXXX"
    /// 3. Settings["fixedAccount"] se configurado manualmente
    /// </summary>
    private string? ExtractOriginalAccount(IEnumerable<ChatMessage> messages)
    {
        // 1. Tentar extrair do ChatTurnContext via AsyncLocal
        var executionInput = DelegateExecutor.Current.Value?.Input;
        if (!string.IsNullOrEmpty(executionInput))
        {
            try
            {
                using var doc = JsonDocument.Parse(executionInput);
                if (doc.RootElement.TryGetProperty("userId", out var userIdEl))
                {
                    var userId = userIdEl.GetString();
                    if (!string.IsNullOrWhiteSpace(userId))
                        return userId;
                }
            }
            catch { /* não é JSON — fallback */ }
        }

        // 2. Extrair de system messages via regex "conta: XXXXX" ou "account: XXXXX"
        foreach (var msg in messages)
        {
            if (msg.Role != ChatRole.System) continue;
            var text = msg.Text;
            if (string.IsNullOrEmpty(text)) continue;

            var match = AccountInSystemMessageRegex().Match(text);
            if (match.Success)
                return match.Groups[1].Value;
        }

        return null;
    }

    /// <summary>
    /// Verifica e corrige contas alteradas na resposta não-streaming.
    /// </summary>
    private void GuardResponse(ChatResponse response, string originalAccount)
    {
        foreach (var msg in response.Messages)
        {
            if (msg.Role != ChatRole.Assistant) continue;

            for (var i = 0; i < msg.Contents.Count; i++)
            {
                if (msg.Contents[i] is not TextContent textContent) continue;

                var replaced = ReplaceAccounts(textContent.Text, originalAccount);
                if (replaced != textContent.Text)
                {
                    // LogDebug: conteúdo do output pode conter PII — não logar em Warning em produção.
                    Logger.LogDebug(
                        "[AccountGuard] Agent '{AgentId}': conta alterada detectada — corrigindo para '{Fixed}' no output ({Chars} chars).",
                        AgentId, originalAccount, textContent.Text.Length);

                    msg.Contents[i] = new TextContent(replaced);
                }
            }
        }
    }

    /// <summary>
    /// Substitui contas numéricas no texto que diferem da conta original.
    /// Usa o accountPattern configurado ou o padrão (sequência de 5-10 dígitos).
    /// Só substitui se o número encontrado NÃO é a conta original (evita false positives com quantidades/preços).
    /// </summary>
    private string ReplaceAccounts(string text, string originalAccount)
    {
        var pattern = GetAccountRegex();

        return pattern.Replace(text, match =>
        {
            var found = match.Value;
            // Se já é a conta original, não toca
            if (found == originalAccount)
                return found;

            // Heurística: só considerar como "conta" se o número tem formato similar à conta original
            // (mesmo comprimento ± 1 dígito) para evitar substituir quantidades, preços etc.
            if (Math.Abs(found.Length - originalAccount.Length) > 1)
                return found;

            return originalAccount;
        });
    }

    private Regex GetAccountRegex()
    {
        if (!string.IsNullOrEmpty(_accountPattern))
            return new Regex(_accountPattern, RegexOptions.Compiled);
        return DefaultAccountRegex();
    }

    [GeneratedRegex(@"(?<=conta[:\s]+|account[:\s]+|operacional[:\s]+)\d{5,10}", RegexOptions.IgnoreCase)]
    private static partial Regex DefaultAccountRegex();

    [GeneratedRegex(@"(?:conta|account|operacional)[:\s]+(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex AccountInSystemMessageRegex();
}
