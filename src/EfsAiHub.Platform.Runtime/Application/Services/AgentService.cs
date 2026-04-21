using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity;

namespace EfsAiHub.Platform.Runtime.Services;

public class AgentService : IAgentService
{
    private static readonly HashSet<string> ValidProviderTypes =
        new(StringComparer.OrdinalIgnoreCase) { "AzureFoundry", "AzureOpenAI", "OpenAI" };

    private static readonly HashSet<string> ValidClientTypes =
        new(StringComparer.OrdinalIgnoreCase) { "ChatCompletion", "Responses", "Assistants" };

    private static readonly HashSet<string> ValidToolTypes =
        new(StringComparer.OrdinalIgnoreCase) { "code_interpreter", "file_search", "function", "mcp", "web_search" };

    private static readonly HashSet<string> ValidResponseFormats =
        new(StringComparer.OrdinalIgnoreCase) { "text", "json", "json_schema" };

    private static readonly HashSet<string> ValidRequireApprovalValues =
        new(StringComparer.OrdinalIgnoreCase) { "never", "always" };

    private static readonly HashSet<string> ValidMiddlewareTypes =
        new(StringComparer.OrdinalIgnoreCase) { "AccountGuard", "StructuredOutputState" };

    private readonly IAgentDefinitionRepository _repository;
    private readonly IAgentPromptRepository _promptRepo;
    private readonly IMcpHealthChecker _mcpHealthChecker;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly ILogger<AgentService> _logger;

    public AgentService(
        IAgentDefinitionRepository repository,
        IAgentPromptRepository promptRepo,
        IMcpHealthChecker mcpHealthChecker,
        IProjectContextAccessor projectAccessor,
        ILogger<AgentService> logger)
    {
        _repository = repository;
        _promptRepo = promptRepo;
        _mcpHealthChecker = mcpHealthChecker;
        _projectAccessor = projectAccessor;
        _logger = logger;
    }

    public async Task<AgentDefinition> CreateAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        definition.ProjectId = _projectAccessor.Current.ProjectId;

        var (isValid, errors) = await ValidateAsync(definition, ct);
        if (!isValid)
            throw new ArgumentException($"Definição de agente inválida: {string.Join(", ", errors)}");

        _logger.LogInformation("Criando definição de agente '{AgentId}'", definition.Id);
        var saved = await _repository.UpsertAsync(definition, ct);

        await SeedInitialPromptAsync(saved, ct);

        return saved;
    }

    public Task<AgentDefinition?> GetAsync(string id, CancellationToken ct = default)
        => _repository.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken ct = default)
        => _repository.GetAllAsync(ct);

    public async Task<AgentDefinition> UpdateAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(definition.Id, ct)
            ?? throw new KeyNotFoundException($"Agente '{definition.Id}' não encontrado.");

        var (isValid, errors) = await ValidateAsync(definition, ct);
        if (!isValid)
            throw new ArgumentException($"Definição de agente inválida: {string.Join(", ", errors)}");

        definition.UpdatedAt = DateTime.UtcNow;
        _logger.LogInformation("Atualizando definição de agente '{AgentId}'", definition.Id);
        var saved = await _repository.UpsertAsync(definition, ct);

        // Sincroniza instructions com a versão master do prompt
        if (!string.IsNullOrWhiteSpace(definition.Instructions)
            && definition.Instructions != existing.Instructions)
        {
            var activePrompt = await _promptRepo.GetActivePromptAsync(definition.Id, ct);
            if (activePrompt is null || activePrompt != definition.Instructions)
            {
                var versions = await _promptRepo.ListVersionsAsync(definition.Id, ct);
                var activeVersion = versions.FirstOrDefault(v => v.IsActive);
                var newVersionId = activeVersion is not null
                    ? $"{activeVersion.VersionId}-upd{DateTime.UtcNow:yyyyMMddHHmmss}"
                    : "v1";

                await _promptRepo.SaveVersionAsync(definition.Id, newVersionId, definition.Instructions, ct);
                await _promptRepo.SetMasterAsync(definition.Id, newVersionId, ct);

                _logger.LogInformation(
                    "[PromptSync] Agente '{AgentId}' — versão '{VersionId}' criada e ativada a partir de instructions.",
                    definition.Id, newVersionId);
            }
        }

        return saved;
    }

    /// <summary>
    /// Se o agente tem Instructions e ainda não possui nenhuma versão de prompt,
    /// cria automaticamente "v1" como master.
    /// </summary>
    public async Task SeedInitialPromptAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(definition.Instructions)) return;

        var versions = await _promptRepo.ListVersionsAsync(definition.Id, ct);
        if (versions.Count > 0) return;

        await _promptRepo.SaveVersionAsync(definition.Id, "v1", definition.Instructions, ct);
        await _promptRepo.SetMasterAsync(definition.Id, "v1", ct);

        _logger.LogInformation(
            "[PromptSeed] Agente '{AgentId}' — versão 'v1' criada automaticamente a partir de instructions.",
            definition.Id);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var deleted = await _repository.DeleteAsync(id, ct);
        if (!deleted)
            throw new KeyNotFoundException($"Agente '{id}' não encontrado.");

        _logger.LogInformation("Definição de agente '{AgentId}' removida.", id);
    }

    public async Task<(bool IsValid, IReadOnlyList<string> Errors)> ValidateAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        var errors = new List<string>();

        // ── Id e Name ────────────────────────────────────────────────────────
        ValidationContext.RequireIdentifier(errors, definition.Id, "id");
        ValidationContext.RequireString(errors, definition.Name, "name", maxLength: 200);

        // ── Model ────────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(definition.Model?.DeploymentName))
            errors.Add("Campo 'model.deploymentName' é obrigatório.");

        if (definition.Model?.Temperature is { } temp && (temp < 0f || temp > 2f))
            errors.Add($"Campo 'model.temperature' deve estar entre 0.0 e 2.0 (recebido: {temp}).");

        if (definition.Model?.MaxTokens is { } maxTokens && maxTokens <= 0)
            errors.Add($"Campo 'model.maxTokens' deve ser maior que zero (recebido: {maxTokens}).");

        // ── Provider ─────────────────────────────────────────────────────────
        if (!ValidProviderTypes.Contains(definition.Provider.Type))
            errors.Add($"Campo 'provider.type' inválido: '{definition.Provider.Type}'. Valores aceitos: {string.Join(", ", ValidProviderTypes)}.");

        if (!ValidClientTypes.Contains(definition.Provider.ClientType))
            errors.Add($"Campo 'provider.clientType' inválido: '{definition.Provider.ClientType}'. Valores aceitos: {string.Join(", ", ValidClientTypes)}.");

        if (!string.IsNullOrWhiteSpace(definition.Provider.Endpoint))
        {
            if (!Uri.TryCreate(definition.Provider.Endpoint, UriKind.Absolute, out var endpointUri)
                || endpointUri.Scheme is not ("http" or "https"))
                errors.Add($"Campo 'provider.endpoint' deve ser uma URL absoluta válida com esquema http ou https.");
        }

        // ── Tools ────────────────────────────────────────────────────────────
        var functionToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in definition.Tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Type))
            {
                errors.Add("Cada tool deve ter o campo 'type'.");
                continue;
            }

            if (!ValidToolTypes.Contains(tool.Type))
            {
                errors.Add($"Tool type inválido: '{tool.Type}'. Valores aceitos: {string.Join(", ", ValidToolTypes)}.");
                continue;
            }

            if (tool.Type.Equals("mcp", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(tool.ServerLabel))
                    errors.Add("MCP tool requer 'serverLabel'.");
                if (string.IsNullOrWhiteSpace(tool.ServerUrl))
                {
                    errors.Add("MCP tool requer 'serverUrl'.");
                }
                else
                {
                    if (!Uri.TryCreate(tool.ServerUrl, UriKind.Absolute, out var mcpUri)
                        || mcpUri.Scheme is not ("http" or "https"))
                        errors.Add($"MCP tool 'serverUrl' deve ser uma URL absoluta válida com esquema http ou https.");
                    else
                    {
                        var healthError = await _mcpHealthChecker.CheckAsync(tool.ServerUrl, tool.ServerLabel ?? "unknown", ct);
                        if (healthError is not null)
                            errors.Add(healthError);
                    }
                }
                if (tool.AllowedTools.Count == 0)
                    errors.Add("MCP tool requer ao menos um item em 'allowedTools'.");

                if (!string.IsNullOrWhiteSpace(tool.RequireApproval)
                    && !ValidRequireApprovalValues.Contains(tool.RequireApproval))
                    errors.Add($"MCP tool 'requireApproval' inválido: '{tool.RequireApproval}'. Valores aceitos: never, always.");
            }

            if (tool.Type.Equals("function", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(tool.Name))
                    errors.Add("Function tool requer 'name'.");
                else if (!functionToolNames.Add(tool.Name))
                    errors.Add($"Function tool com nome duplicado: '{tool.Name}'. Nomes de function tools devem ser únicos.");
            }
        }

        // ── Output Estruturado ────────────────────────────────────────────────
        if (definition.StructuredOutput is { } so)
        {
            if (!ValidResponseFormats.Contains(so.ResponseFormat))
                errors.Add($"Campo 'structuredOutput.responseFormat' inválido: '{so.ResponseFormat}'. Valores aceitos: {string.Join(", ", ValidResponseFormats)}.");

            if (so.ResponseFormat.Equals("json_schema", StringComparison.OrdinalIgnoreCase))
            {
                if (so.Schema is null)
                    errors.Add("'structuredOutput.schema' é obrigatório quando responseFormat é 'json_schema'.");
                if (string.IsNullOrWhiteSpace(so.SchemaName))
                    errors.Add("'structuredOutput.schemaName' é obrigatório quando responseFormat é 'json_schema'.");
            }
        }

        // ── Middlewares ───────────────────────────────────────────────────────
        foreach (var mw in definition.Middlewares)
        {
            if (!ValidMiddlewareTypes.Contains(mw.Type))
                errors.Add($"Middleware type inválido: '{mw.Type}'. Valores aceitos: {string.Join(", ", ValidMiddlewareTypes)}.");
        }

        return (errors.Count == 0, errors);
    }
}
