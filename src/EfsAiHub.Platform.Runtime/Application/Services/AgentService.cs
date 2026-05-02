using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;

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
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly IAgentVersionRepository? _versionRepo;
    private readonly IAdminAuditLogger? _auditLogger;
    private readonly ILogger<AgentService> _logger;

    public AgentService(
        IAgentDefinitionRepository repository,
        IAgentPromptRepository promptRepo,
        IProjectContextAccessor projectAccessor,
        ILogger<AgentService> logger,
        IAgentVersionRepository? versionRepo = null,
        IAdminAuditLogger? auditLogger = null)
    {
        _repository = repository;
        _promptRepo = promptRepo;
        _projectAccessor = projectAccessor;
        _versionRepo = versionRepo;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public async Task<AgentDefinition> CreateAsync(
        AgentDefinition definition,
        CancellationToken ct = default,
        bool breakingChange = false,
        string? changeReason = null,
        string? createdBy = null)
    {
        definition.ProjectId = _projectAccessor.Current.ProjectId;

        var (isValid, errors) = await ValidateAsync(definition, ct);
        if (!isValid)
            throw new ArgumentException($"Definição de agente inválida: {string.Join(", ", errors)}");

        _logger.LogInformation("Criando definição de agente '{AgentId}'", definition.Id);
        var saved = await _repository.UpsertAsync(definition, ct,
            breakingChange: breakingChange,
            changeReason: changeReason,
            createdBy: createdBy);

        await SeedInitialPromptAsync(saved, ct);

        return saved;
    }

    public Task<AgentDefinition?> GetAsync(string id, CancellationToken ct = default)
        => _repository.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken ct = default)
        => _repository.GetAllAsync(ct);

    public async Task<AgentDefinition> UpdateAsync(
        AgentDefinition definition,
        CancellationToken ct = default,
        bool breakingChange = false,
        string? changeReason = null,
        string? createdBy = null)
    {
        var existing = await _repository.GetByIdAsync(definition.Id, ct)
            ?? throw new KeyNotFoundException($"Agente '{definition.Id}' não encontrado.");

        // Preserva Visibility/ProjectId/TenantId do existing.
        // Request DTO não carrega esses campos por design; sem isso o PUT silenciosamente
        // resetaria Visibility="project". PATCH /agents/{id}/visibility é o único caminho.
        definition.ProjectId = existing.ProjectId;
        definition.TenantId = existing.TenantId;
        definition.Visibility = existing.Visibility;
        // Preserve AllowedProjectIds quando o caller não envia (CreateAgentRequest
        // pode trazer null tanto pra "remover whitelist" quanto pra "não mexer". Pra evitar
        // ambiguidade, PATCH /visibility é o único caminho de mudar AllowedProjectIds).
        if (definition.AllowedProjectIds is null)
            definition.AllowedProjectIds = existing.AllowedProjectIds;

        var (isValid, errors) = await ValidateAsync(definition, ct);
        if (!isValid)
            throw new ArgumentException($"Definição de agente inválida: {string.Join(", ", errors)}");

        definition.UpdatedAt = DateTime.UtcNow;
        _logger.LogInformation("Atualizando definição de agente '{AgentId}'", definition.Id);
        var saved = await _repository.UpsertAsync(definition, ct,
            breakingChange: breakingChange,
            changeReason: changeReason,
            createdBy: createdBy);

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
    /// Garante que todo agente tenha pelo menos uma versão de prompt ativa ("v1").
    /// Usa Instructions como conteúdo inicial — se vazio/null, grava "" (sem system message).
    /// Idempotente: se já existe alguma versão, não faz nada.
    /// Por que: GET /api/agents/{id}/prompts/active retorna 404 quando não há versão; com
    /// este seed garantido, o frontend e qualquer caller subsequente enxerga o estado
    /// "agente recém-criado sem prompt customizado" como uma versão vazia (200 OK), não
    /// como recurso ausente.
    /// </summary>
    public async Task SeedInitialPromptAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        var versions = await _promptRepo.ListVersionsAsync(definition.Id, ct);
        if (versions.Count > 0) return;

        var initialContent = definition.Instructions ?? string.Empty;
        await _promptRepo.SaveVersionAsync(definition.Id, "v1", initialContent, ct);
        await _promptRepo.SetMasterAsync(definition.Id, "v1", ct);

        _logger.LogInformation(
            "[PromptSeed] Agente '{AgentId}' — versão 'v1' criada (size={Size}).",
            definition.Id, initialContent.Length);
    }

    public async Task<AgentDefinition> UpdateVisibilityAsync(string id, string newVisibility, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Agente '{id}' não encontrado.");

        // Owner gate: só o projeto dono pode alterar visibility.
        var currentProjectId = _projectAccessor.Current.ProjectId;
        if (!string.Equals(existing.ProjectId, currentProjectId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Agente '{id}' não pertence ao projeto atual; apenas o projeto dono pode alterar visibility.");

        if (!AgentDefinition.AllowedVisibilities.Contains(newVisibility))
            throw new ArgumentException(
                $"Visibility '{newVisibility}' inválida. Permitidos: {string.Join(", ", AgentDefinition.AllowedVisibilities)}.");

        // Idempotência: sem mudança, retorna existing sem audit/cache churn.
        if (string.Equals(existing.Visibility, newVisibility, StringComparison.OrdinalIgnoreCase))
            return existing;

        existing.Visibility = newVisibility;
        existing.UpdatedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "[AgentService] Visibility do agent '{AgentId}' alterada para '{Visibility}' por projeto '{ProjectId}'.",
            id, newVisibility, currentProjectId);

        return await _repository.UpsertAsync(existing, ct);
    }

    public async Task<AgentVersion> PublishVersionAsync(
        string agentId,
        bool breakingChange,
        string? changeReason = null,
        string? createdBy = null,
        CancellationToken ct = default)
    {
        if (_versionRepo is null)
            throw new InvalidOperationException(
                "PublishVersionAsync requer IAgentVersionRepository registrado.");

        var existing = await _repository.GetByIdAsync(agentId, ct)
            ?? throw new KeyNotFoundException($"Agente '{agentId}' não encontrado.");

        // Owner gate: só o projeto dono publica versions.
        var currentProjectId = _projectAccessor.Current.ProjectId;
        if (!string.Equals(existing.ProjectId, currentProjectId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Agente '{agentId}' não pertence ao projeto atual; apenas o projeto dono pode publicar versions.");

        // Active prompt do agent — capturado no snapshot pra rollback determinístico.
        string? promptContent = null;
        string? promptVersionId = null;
        try
        {
            var prompt = await _promptRepo.GetActivePromptWithVersionAsync(agentId, ct);
            promptContent = prompt?.Content;
            promptVersionId = prompt?.VersionId;
        }
        catch
        {
            // best-effort — agent recém-criado pode não ter prompt ativo ainda.
        }

        var revision = await _versionRepo.GetNextRevisionAsync(agentId, ct);
        var snapshot = AgentVersion.FromDefinition(
            existing,
            revision,
            promptContent: promptContent,
            promptVersionId: promptVersionId,
            createdBy: createdBy,
            changeReason: changeReason,
            breakingChange: breakingChange);

        // EnsureInvariants é chamado dentro de AppendAsync — DomainException dispara
        // quando breakingChange=true e changeReason está vazio.
        var persisted = await _versionRepo.AppendAsync(snapshot, ct);

        // Audit dispara apenas em publish efetivo. AppendAsync é idempotente por
        // ContentHash: re-publish sem mudança retorna a version existing (mesmo
        // AgentVersionId que o snapshot que tentamos persistir difere). Comparamos
        // ids pra detectar no-op e suprimir audit redundante.
        var isNoOp = !string.Equals(persisted.AgentVersionId, snapshot.AgentVersionId, StringComparison.OrdinalIgnoreCase);
        if (!isNoOp && _auditLogger is not null)
        {
            try
            {
                var payload = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    revision = persisted.Revision,
                    breakingChange,
                    changeReason,
                    contentHash = persisted.ContentHash,
                }));
                await _auditLogger.RecordAsync(new AdminAuditEntry
                {
                    ActorUserId = createdBy ?? "system:agent-service",
                    ActorUserType = createdBy is null ? "system" : "user",
                    Action = AdminAuditActions.AgentVersionPublished,
                    ResourceType = AdminAuditResources.Agent,
                    ResourceId = agentId,
                    ProjectId = existing.ProjectId,
                    TenantId = existing.TenantId,
                    PayloadAfter = payload,
                    Timestamp = DateTime.UtcNow,
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[AgentService] Falha ao registrar audit agent.version_published (não-bloqueante).");
            }
        }

        _logger.LogInformation(
            "[AgentService] Version '{VersionId}' publicada pra '{AgentId}' (revision={Revision}, breaking={Breaking}, noOp={NoOp}).",
            persisted.AgentVersionId, agentId, persisted.Revision, breakingChange, isNoOp);

        return persisted;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var deleted = await _repository.DeleteAsync(id, ct);
        if (!deleted)
            throw new KeyNotFoundException($"Agente '{id}' não encontrado.");

        _logger.LogInformation("Definição de agente '{AgentId}' removida.", id);
    }

    public Task<(bool IsValid, IReadOnlyList<string> Errors)> ValidateAsync(AgentDefinition definition, CancellationToken ct = default)
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
                // Modo id-based (preferido): agent referencia registro em aihub.mcp_servers.
                // Modo inline (legacy/fallback BC): agent guarda ServerLabel+ServerUrl direto.
                // Zero chamada de rede — a resolução ocorre em runtime no provider LLM.
                var hasIdRef = !string.IsNullOrWhiteSpace(tool.McpServerId);
                var hasInlineConfig = !string.IsNullOrWhiteSpace(tool.ServerLabel)
                                      && !string.IsNullOrWhiteSpace(tool.ServerUrl);

                if (!hasIdRef && !hasInlineConfig)
                {
                    errors.Add("MCP tool requer 'mcpServerId' (preferido) ou 'serverLabel' + 'serverUrl' (legacy).");
                }
                else if (!hasIdRef && hasInlineConfig)
                {
                    // Valida shape inline só quando não há referência por Id.
                    if (!Uri.TryCreate(tool.ServerUrl, UriKind.Absolute, out var mcpUri)
                        || mcpUri!.Scheme is not ("http" or "https"))
                        errors.Add("MCP tool 'serverUrl' deve ser uma URL absoluta válida com esquema http ou https.");
                    if (tool.AllowedTools.Count == 0)
                        errors.Add("MCP tool inline requer ao menos um item em 'allowedTools'.");
                }

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

        return Task.FromResult<(bool, IReadOnlyList<string>)>((errors.Count == 0, errors));
    }
}
