using Microsoft.Extensions.AI;

namespace EfsAiHub.Core.Agents.Interfaces;

/// <summary>
/// Registry versionado de tools (funções chamáveis pelo LLM) por fingerprint SHA-256.
/// Cada <see cref="Register"/> computa um hash canônico de <c>{Name, Description, JsonSchema}</c>
/// e indexa em <c>(Name, Hash) → AIFunction</c>. O hash mais recente por nome é exposto via
/// <see cref="GetLatestFingerprint"/> e usado pelos consumidores legados.
///
/// Uso — 3 passos:
/// <list type="number">
///   <item>Crie um método C# com <c>[Description]</c> no método e em cada parâmetro</item>
///   <item>Registre em Program.cs: <c>registry.Register("nome_tool", AIFunctionFactory.Create(obj.Metodo))</c></item>
///   <item>Habilite no agente: <c>tools: [{ "type": "function", "name": "nome_tool" }]</c></item>
/// </list>
///
/// Exemplo rápido:
/// <code>
///   public class MyFunctions
///   {
///       [Description("Busca dados do cliente pelo ID")]
///       public async Task&lt;string&gt; GetCustomer(
///           [Description("ID do cliente")] string customerId)
///           => JsonSerializer.Serialize(await _repo.GetAsync(customerId));
///   }
///
///   // No Program.cs:
///   registry.Register("get_customer", AIFunctionFactory.Create(myFunctions.GetCustomer));
/// </code>
///
/// Veja CONTRIBUTING.md para o guia completo. Para tools com escopo de projeto use
/// <see cref="Register(string, AIFunction, string)"/>.
///
/// Internamente: fingerprint detecta mudanças de schema entre versões de agente;
/// <c>AgentVersion</c> e <c>AgentToolDefinition</c> gravam o hash no publish para
/// auditoria de compatibilidade.
/// </summary>
public interface IFunctionToolRegistry
{
    /// <summary>Registra uma nova versão; fingerprint derivado do JSONSchema.</summary>
    void Register(string name, AIFunction function);

    /// <summary>Versão mais recente registrada para <paramref name="name"/>. Lança se ausente.</summary>
    AIFunction Get(string name);

    /// <summary>Versão mais recente registrada para <paramref name="name"/>.</summary>
    bool TryGet(string name, out AIFunction? function);

    /// <summary>Versão exata indicada por <paramref name="fingerprintHash"/>, ou null.</summary>
    AIFunction? GetByFingerprint(string name, string fingerprintHash);

    /// <summary>Hash da versão mais recente registrada para <paramref name="name"/>, ou null.</summary>
    string? GetLatestFingerprint(string name);

    /// <summary>Fingerprints conhecidos para <paramref name="name"/> (ordem de inserção).</summary>
    IReadOnlyList<string> ListFingerprints(string name);

    /// <summary>Mapa <c>name → AIFunction (latest)</c> — API legado.</summary>
    IReadOnlyDictionary<string, AIFunction> GetAll();

    // ── Project-scoped tools ────────────────────────────────────────────────

    /// <summary>Registra uma tool com escopo de projeto.</summary>
    void Register(string name, AIFunction function, string projectId);

    /// <summary>Versão mais recente para <paramref name="name"/> no escopo do projeto (fallback global).</summary>
    bool TryGet(string name, string projectId, out AIFunction? function);

    /// <summary>Todas as tools visíveis para um projeto (project + global).</summary>
    IReadOnlyDictionary<string, AIFunction> GetAll(string projectId);
}
