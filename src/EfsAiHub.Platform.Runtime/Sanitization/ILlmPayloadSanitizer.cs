namespace EfsAiHub.Platform.Runtime.Sanitization;

/// <summary>
/// Remove segredos (API keys, JWTs, tokens) de payloads JSON antes da
/// persistência em <c>aihub.llm_invocation_log</c>. Operação opera no JSON
/// já serializado como string — mais simples que reescrever a árvore e
/// pega secrets escondidos em strings de descrição, exemplos, headers.
///
/// Estratégia: blacklist regex. Whitelist é inviável porque tools
/// customizadas (generic_http) podem trazer descrições arbitrárias.
/// </summary>
public interface ILlmPayloadSanitizer
{
    /// <summary>
    /// Aplica todos os padrões registrados. Substitui matches por
    /// <c>***REDACTED:{kind}***</c> onde <c>kind</c> identifica o tipo
    /// (openai-key, jwt, slack-token, etc.). Devolve nova string —
    /// input imutável.
    /// </summary>
    string Sanitize(string payload);
}
