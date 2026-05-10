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
        new(StringComparer.OrdinalIgnoreCase) { "AccountGuard", "StructuredOutputState", "SecurityGuardrails" };

    private readonly IAgentDefinitionRepository _repository;
    private readonly IAgentPromptRepository _promptRepo;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly IAgentVersionRepository? _versionRepo;
    private readonly IAdminAuditLogger? _auditLogger;
    private readonly IAgentRouterIntentLinkRepository? _intentLinkRepo;
    private readonly ILogger<AgentService> _logger;

    public AgentService(
        IAgentDefinitionRepository repository,
        IAgentPromptRepository promptRepo,
        IProjectContextAccessor projectAccessor,
        ILogger<AgentService> logger,
        IAgentVersionRepository? versionRepo = null,
        IAdminAuditLogger? auditLogger = null,
        IAgentRouterIntentLinkRepository? intentLinkRepo = null)
    {
        _repository = repository;
        _promptRepo = promptRepo;
        _projectAccessor = projectAccessor;
        _versionRepo = versionRepo;
        _auditLogger = auditLogger;
        _intentLinkRepo = intentLinkRepo;
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

        var (isValid, errors, _) = await ValidateAsync(definition, ct);
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

    public async Task<IReadOnlyList<AgentDefinition>> ListByProjectAsync(CancellationToken ct = default)
    {
        var currentProjectId = _projectAccessor.Current.ProjectId;
        var all = await _repository.GetAllAsync(ct).ConfigureAwait(false);
        return all.Where(a => a.ProjectId == currentProjectId).ToList();
    }

    public async Task<AgentDefinition> UpdateAsync(
        AgentDefinition definition,
        CancellationToken ct = default,
        bool breakingChange = false,
        string? changeReason = null,
        string? createdBy = null)
    {
        var existing = await _repository.GetByIdAsync(definition.Id, ct)
            ?? throw new KeyNotFoundException($"Agente '{definition.Id}' não encontrado.");

        // Owner gate: HasQueryFilter expõe agentes Visibility=global cross-project pra
        // leitura, então o existing pode ser visível pra um projeto que NÃO é dono. Sem
        // este check, qualquer projeto do tenant editaria o agente global. Mensagem
        // genérica pra não vazar o ProjectId do owner.
        var currentProjectId = _projectAccessor.Current.ProjectId;
        if (!string.Equals(existing.ProjectId, currentProjectId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Agente '{definition.Id}' não pertence ao projeto atual; apenas o projeto dono pode editar.");

        // Preserva Visibility/ProjectId/TenantId do existing.
        // Request DTO não carrega esses campos por design; sem isso o PUT silenciosamente
        // resetaria Visibility="project". PATCH /agents/{id}/visibility é o único caminho.
        definition.ProjectId = existing.ProjectId;
        definition.TenantId = existing.TenantId;
        definition.Visibility = existing.Visibility;
        // Type também é preservado: clientes legados sem o campo no body fariam
        // request.ToDomain() default pra Custom e zerariam um Router existente.
        // Mudança de tipo é semântica (invalida outras invariantes — Router exige
        // structured output, etc) e fica fora do escopo do PUT genérico.
        definition.Type = existing.Type;
        // Preserve AllowedProjectIds quando o caller não envia (CreateAgentRequest
        // pode trazer null tanto pra "remover whitelist" quanto pra "não mexer". Pra evitar
        // ambiguidade, PATCH /visibility é o único caminho de mudar AllowedProjectIds).
        if (definition.AllowedProjectIds is null)
            definition.AllowedProjectIds = existing.AllowedProjectIds;

        var (isValid, errors, _) = await ValidateAsync(definition, ct);
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
    /// Por que: GET /api/aihub/agents/{id}/prompts/active retorna 404 quando não há versão; com
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

    /// <summary>
    /// Liga/desliga o agent. Owner gate, idempotente. Persiste via <c>UpsertAsync</c>
    /// (cache invalida automaticamente). Estado mutável — não captura snapshot em
    /// AgentVersion; runtime resolve via governance source row corrente.
    /// </summary>
    public async Task<AgentDefinition> UpdateEnabledAsync(string id, bool enabled, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Agente '{id}' não encontrado.");

        var currentProjectId = _projectAccessor.Current.ProjectId;
        if (!string.Equals(existing.ProjectId, currentProjectId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Agente '{id}' não pertence ao projeto atual; apenas o projeto dono pode alterar Enabled.");

        if (existing.Enabled == enabled) return existing;

        existing.Enabled = enabled;
        existing.UpdatedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "[AgentService] Enabled do agent '{AgentId}' alterado para '{Enabled}' por projeto '{ProjectId}'.",
            id, enabled, currentProjectId);

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
        var existing = await _repository.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Agente '{id}' não encontrado.");

        var currentProjectId = _projectAccessor.Current.ProjectId;
        if (!string.Equals(existing.ProjectId, currentProjectId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Agente '{id}' não pertence ao projeto atual; apenas o projeto dono pode remover.");

        var deleted = await _repository.DeleteAsync(id, ct);
        if (!deleted)
            throw new KeyNotFoundException($"Agente '{id}' não encontrado.");

        _logger.LogInformation("Definição de agente '{AgentId}' removida.", id);
    }

    public async Task<(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)> ValidateAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // ── Id e Name ────────────────────────────────────────────────────────
        ValidationContext.RequireIdentifier(errors, definition.Id, "id");
        ValidationContext.RequireString(errors, definition.Name, "name", maxLength: 200);

        // ── Model ────────────────────────────────────────────────────────────
        // Quando o agent referencia um PredefinedModelId, o PredefinedModelBinder
        // hidrata DeploymentName/Provider.Type/Provider.ClientType em runtime
        // (ver PredefinedModelBinder.BindAsync). A validação aqui roda antes
        // do binder, então pulamos esses checks pra não exigir o que vai ser
        // resolvido depois — caso contrário, agent que só passa preset falha
        // com "model.deploymentName obrigatório".
        var hasPredefined = !string.IsNullOrWhiteSpace(definition.Model?.PredefinedModelId);

        if (!hasPredefined && string.IsNullOrWhiteSpace(definition.Model?.DeploymentName))
            errors.Add("Campo 'model.deploymentName' é obrigatório (ou informe 'model.predefinedModelId').");

        if (definition.Model?.Temperature is { } temp && (temp < 0f || temp > 2f))
            errors.Add($"Campo 'model.temperature' deve estar entre 0.0 e 2.0 (recebido: {temp}).");

        if (definition.Model?.MaxTokens is { } maxTokens && maxTokens <= 0)
            errors.Add($"Campo 'model.maxTokens' deve ser maior que zero (recebido: {maxTokens}).");

        // ── Provider ─────────────────────────────────────────────────────────
        // Mesmo motivo do bloco Model acima: preset hidrata em runtime.
        if (!hasPredefined && !ValidProviderTypes.Contains(definition.Provider.Type))
            errors.Add($"Campo 'provider.type' inválido: '{definition.Provider.Type}'. Valores aceitos: {string.Join(", ", ValidProviderTypes)}.");

        if (!hasPredefined && !ValidClientTypes.Contains(definition.Provider.ClientType))
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

        // ── Validações por tipo ──────────────────────────────────────────────
        // Custom não tem template aplicado: skip. Outros tipos rodam invariantes
        // específicas após os checks genéricos acima — assim erros estruturais
        // (model, provider, tools) são reportados sem ruído de "schema não tem
        // intent" quando a definition já tá quebrada na base.
        switch (definition.Type)
        {
            case AgentType.Router:
                await ValidateRouterAsync(definition, errors, warnings, ct);
                break;
            case AgentType.Worker:
                ValidateWorker(definition, warnings);
                break;
            case AgentType.ToolRunner:
                ValidateToolRunner(definition, warnings);
                break;
            case AgentType.Conversational:
                ValidateConversational(definition, errors, warnings);
                break;
        }

        return (errors.Count == 0, errors, warnings);
    }

    /// <summary>
    /// Invariantes do tipo Router: o agente precisa atender pelo menos 2
    /// intents do pool global (<c>aihub.router_intents</c>). Schema persistido
    /// pode ficar com <c>intent: { type: "string" }</c> sem enum — o runtime
    /// (<c>ChatOptionsBuilder</c>) injeta o enum dinâmico das intents
    /// referenciadas. Soft warnings sinalizam configurações que descaracterizam
    /// o template (memória, tools, modelo pesado, MaxTokens alto).
    /// </summary>
    private async Task ValidateRouterAsync(
        AgentDefinition definition,
        List<string> errors,
        List<string> warnings,
        CancellationToken ct)
    {
        // Count vem do request (transient `RouterIntentIds`) quando o caller
        // está propondo um set novo, ou do link repo quando é validação após
        // o save (set vivo no DB).
        var intentCount = definition.RouterIntentIds?.Count
                          ?? (_intentLinkRepo is null
                              ? 0
                              : await _intentLinkRepo.CountForAgentAsync(definition.Id, ct));

        if (intentCount < 2)
        {
            errors.Add(
                $"Router exige pelo menos 2 intenções selecionadas (recebido: {intentCount}). " +
                "Cadastre intenções em /intencoes e edite o agent pra marcar quais ele atende.");
        }

        if (definition.Model?.MaxTokens is { } maxTokens && maxTokens > 500)
            warnings.Add($"Router típico produz output curto — 'model.maxTokens'={maxTokens} sugere uso indevido.");

        var deployment = definition.Model?.DeploymentName ?? string.Empty;
        if (!string.IsNullOrEmpty(deployment)
            && deployment.IndexOf("mini", StringComparison.OrdinalIgnoreCase) < 0
            && deployment.IndexOf("nano", StringComparison.OrdinalIgnoreCase) < 0
            && deployment.IndexOf("haiku", StringComparison.OrdinalIgnoreCase) < 0)
        {
            warnings.Add($"Router típico usa modelo mini/nano/haiku — 'model.deploymentName'='{deployment}' é um modelo full, confirma?");
        }

        if (definition.Tools.Count > 0)
            warnings.Add("Tools em Router são incomuns — se o agente precisa invocar ferramentas, considere o tipo Tool Runner.");

        if (definition.OperationalMemory?.Schema is not null)
            warnings.Add("OperationalMemory raramente é útil em Router; pra contexto de chat, prefira workflow com 'InputMode=Chat'.");
    }

    /// <summary>
    /// Worker é template "soft": todas as expectativas (modelo full,
    /// StructuredOutput, MaxTokens alto, SecurityGuardrails on, scope
    /// declarado) viram warnings — nunca bloqueiam o save. Mantém o usuário
    /// no controle e sinaliza desvios na UI.
    /// </summary>
    private static void ValidateWorker(AgentDefinition definition, List<string> warnings)
    {
        if (definition.Metadata is not { } metadata
            || !metadata.TryGetValue(AgentDefinition.WorkerScopeMetadataKey, out var scope)
            || string.IsNullOrWhiteSpace(scope))
        {
            warnings.Add(
                "Worker sem domínio de análise definido — preencha o campo 'Domínio de análise' " +
                "pra que o agente saiba o escopo do raciocínio.");
        }

        var deployment = definition.Model?.DeploymentName ?? string.Empty;
        var hasPredefined = !string.IsNullOrWhiteSpace(definition.Model?.PredefinedModelId);
        if (!hasPredefined && !string.IsNullOrEmpty(deployment)
            && (deployment.IndexOf("mini", StringComparison.OrdinalIgnoreCase) >= 0
                || deployment.IndexOf("nano", StringComparison.OrdinalIgnoreCase) >= 0
                || deployment.IndexOf("haiku", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            warnings.Add(
                $"Worker recomenda modelo full (gpt-5, claude-opus, gemini-pro) — " +
                $"'model.deploymentName'='{deployment}' é um modelo leve. Análise rasa pode degradar o pipeline.");
        }

        var responseFormat = definition.StructuredOutput?.ResponseFormat ?? "text";
        if (!responseFormat.Equals("json_schema", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                "Worker recomenda StructuredOutput (json_schema) pra que o caller downstream " +
                "consuma a análise como objeto, não texto livre.");
        }

        if (definition.Model?.MaxTokens is { } maxTokens && maxTokens < 1500)
        {
            warnings.Add(
                $"Worker recomenda 'model.maxTokens' >= 2000 pra acomodar análise substantiva — " +
                $"recebido: {maxTokens}.");
        }

        var hasGuardrails = definition.Middlewares.Any(m =>
            string.Equals(m.Type, "SecurityGuardrails", StringComparison.OrdinalIgnoreCase)
            && m.Enabled);
        if (!hasGuardrails)
        {
            warnings.Add(
                "Worker recomenda middleware 'SecurityGuardrails' pra manter a análise dentro do " +
                "domínio declarado e mitigar prompt injection.");
        }

        if (definition.OperationalMemory?.Schema is not null)
        {
            warnings.Add(
                "Worker normalmente é single-shot dentro de pipeline — OperationalMemory raramente faz " +
                "sentido. Se precisar de contexto multi-turn, considere o tipo Conversational.");
        }
    }

    /// <summary>
    /// Tool Runner é template "soft": todas as expectativas (tools >= 1,
    /// modelo full, temperature baixa, MaxTokens alto, AccountGuard on,
    /// SecurityGuardrails on) viram warnings — nunca bloqueiam o save.
    /// Inclui validações de consistência declarativa do flag HITL
    /// (<c>metadata['x-tool-runner-hitl-required']</c>).
    /// </summary>
    private static void ValidateToolRunner(AgentDefinition definition, List<string> warnings)
    {
        if (definition.Tools.Count == 0)
        {
            warnings.Add(
                "Tool Runner sem tools selecionadas. O template é executor — selecione ao menos uma " +
                "function/generic_http/MCP no step Ferramentas, ou considere o tipo Custom/Worker.");
        }

        var deployment = definition.Model?.DeploymentName ?? string.Empty;
        var hasPredefined = !string.IsNullOrWhiteSpace(definition.Model?.PredefinedModelId);
        if (!hasPredefined && !string.IsNullOrEmpty(deployment)
            && (deployment.IndexOf("mini", StringComparison.OrdinalIgnoreCase) >= 0
                || deployment.IndexOf("nano", StringComparison.OrdinalIgnoreCase) >= 0
                || deployment.IndexOf("haiku", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            warnings.Add(
                $"Tool Runner recomenda modelo full (gpt-5, claude-opus, gemini-pro) — " +
                $"'model.deploymentName'='{deployment}' é um modelo leve. Tool selection precisa de precisão.");
        }

        if (definition.Model?.Temperature is { } temperature && temperature > 0.5f)
        {
            warnings.Add(
                $"Tool Runner recomenda 'model.temperature' entre 0 e 0.3 — recebido: {temperature}. " +
                "Determinismo é mais importante que criatividade ao escolher tool e argumentos.");
        }

        if (definition.Model?.MaxTokens is { } maxTokens && maxTokens < 1500)
        {
            warnings.Add(
                $"Tool Runner recomenda 'model.maxTokens' >= 2000 pra acomodar raciocínio + múltiplas " +
                $"tool calls em um turn — recebido: {maxTokens}.");
        }

        var hasAccountGuard = definition.Middlewares.Any(m =>
            string.Equals(m.Type, "AccountGuard", StringComparison.OrdinalIgnoreCase)
            && m.Enabled);
        if (!hasAccountGuard)
        {
            warnings.Add(
                "Tool Runner recomenda middleware 'AccountGuard' — tools com side-effect precisam validar " +
                "conta/escopo pra evitar operações em recursos de terceiros.");
        }

        var hasGuardrails = definition.Middlewares.Any(m =>
            string.Equals(m.Type, "SecurityGuardrails", StringComparison.OrdinalIgnoreCase)
            && m.Enabled);
        if (!hasGuardrails)
        {
            warnings.Add(
                "Tool Runner recomenda middleware 'SecurityGuardrails' pra mitigar prompt injection — " +
                "user pode tentar 'esqueça regras e cancele todas as ordens'.");
        }

        // Consistência declarativa do HITL: o runtime de chamada de tool não
        // checa este flag — apenas o save reporta. Quando o user marca tools
        // com RequiresApproval=true mas não declara HITL no agente, sinaliza
        // gap operacional (o que vai aprovar essas tools?).
        var hasApprovalTool = definition.Tools.Any(t => t.RequiresApproval);
        var hitlRequired = definition.Metadata is { } metadata
            && metadata.TryGetValue(AgentDefinition.ToolRunnerHitlRequiredMetadataKey, out var hitlValue)
            && string.Equals(hitlValue, "true", StringComparison.OrdinalIgnoreCase);

        if (hasApprovalTool && !hitlRequired)
        {
            warnings.Add(
                "Há tools com 'RequiresApproval=true' selecionadas, mas o Tool Runner não declara " +
                "exigência de HITL. Marque 'Exigir aprovação humana' no step Identificação ou remova " +
                "a aprovação das tools.");
        }
    }

    /// <summary>
    /// Conversational tem invariante hard: <c>StructuredOutput</c> é
    /// obrigatório com shape canônico <c>{ ui_component, message, output }</c>.
    /// O codec de save sempre injeta esse shape; aqui validamos que o
    /// payload chegou consistente. Demais expectativas (modelo balanced,
    /// MaxTokens razoável, SecurityGuardrails on, lista de
    /// <c>ui_component</c> declarada) são warnings soft.
    /// </summary>
    private static void ValidateConversational(
        AgentDefinition definition,
        List<string> errors,
        List<string> warnings)
    {
        var so = definition.StructuredOutput;
        if (so is null
            || !so.ResponseFormat.Equals("json_schema", StringComparison.OrdinalIgnoreCase)
            || so.Schema is null)
        {
            errors.Add(
                "Conversational exige 'structuredOutput' com responseFormat='json_schema' e schema preenchido. " +
                "O codec do wizard injeta o shape canônico { ui_component, message, output } automaticamente.");
        }
        else
        {
            var root = so.Schema.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("properties", out var props)
                || props.ValueKind != JsonValueKind.Object
                || !props.TryGetProperty("ui_component", out _)
                || !props.TryGetProperty("message", out _)
                || !props.TryGetProperty("output", out _))
            {
                errors.Add(
                    "Schema de Conversational precisa declarar 'ui_component', 'message' e 'output' como " +
                    "propriedades top-level. Reabra o agente no wizard pra que o codec gere o shape correto.");
            }
        }

        var deployment = definition.Model?.DeploymentName ?? string.Empty;
        var hasPredefined = !string.IsNullOrWhiteSpace(definition.Model?.PredefinedModelId);
        if (definition.Model?.MaxTokens is { } maxTokens && maxTokens > 4000)
        {
            warnings.Add(
                $"Conversational responde turns curtos a médios — 'model.maxTokens'={maxTokens} é alto. " +
                "Recomenda 1500–2000 pra latência (TTFT) razoável.");
        }
        if (definition.Model?.MaxTokens is { } lowMax && lowMax < 800)
        {
            warnings.Add(
                $"Conversational com 'model.maxTokens'={lowMax} pode truncar respostas naturais. " +
                "Recomenda 1500–2000.");
        }

        if (definition.Model?.Temperature is { } temperature
            && (temperature < 0.3f || temperature > 1.0f))
        {
            warnings.Add(
                $"Conversational recomenda 'model.temperature' entre 0.5 e 0.8 — recebido: {temperature}. " +
                "Valores muito baixos soam robóticos; muito altos perdem consistência.");
        }

        var hasGuardrails = definition.Middlewares.Any(m =>
            string.Equals(m.Type, "SecurityGuardrails", StringComparison.OrdinalIgnoreCase)
            && m.Enabled);
        if (!hasGuardrails)
        {
            warnings.Add(
                "Conversational é exposto a user externo — recomenda middleware 'SecurityGuardrails' pra " +
                "mitigar prompt injection ('esqueça regras', 'finja ser outro agente', etc.).");
        }

        // Lista de ui_component declarados vive em
        // metadata['x-conversational-ui-components'] como JSON array. Vazio ou
        // ausente = enum sem restrição no schema, com warning soft.
        var uiComponentsRaw = definition.Metadata is { } md
            && md.TryGetValue(AgentDefinition.ConversationalUiComponentsMetadataKey, out var raw)
                ? raw
                : null;
        var hasUiComponents = false;
        if (!string.IsNullOrWhiteSpace(uiComponentsRaw))
        {
            try
            {
                using var doc = JsonDocument.Parse(uiComponentsRaw);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    warnings.Add(
                        "Lista 'x-conversational-ui-components' em metadata precisa ser um array JSON " +
                        "(ex: [\"text\",\"card\"]). Frontend renderer cai pro fallback genérico.");
                }
                else if (doc.RootElement.GetArrayLength() == 0)
                {
                    // Trata como ausente — segue pro warning padrão abaixo.
                }
                else if (doc.RootElement.EnumerateArray()
                    .Any(item => item.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(item.GetString())))
                {
                    warnings.Add(
                        "Lista 'x-conversational-ui-components' contém items inválidos — todos precisam " +
                        "ser strings não-vazias (ex: \"card\"). Itens inválidos são ignorados pelo codec.");
                }
                else
                {
                    hasUiComponents = true;
                }
            }
            catch
            {
                warnings.Add(
                    "Lista 'x-conversational-ui-components' em metadata não é JSON válido — " +
                    "o frontend renderer cai pro fallback genérico.");
            }
        }
        if (!hasUiComponents)
        {
            warnings.Add(
                "Conversational sem lista de 'ui_component' declarada. Marque ao menos um valor no step " +
                "Persona pra dirigir o renderer (ex: 'text', 'card', 'list'). Sem isso, o frontend usa " +
                "fallback genérico (mostra message + output JSON cru).");
        }

        // Middleware AG-UI: StructuredOutputState intercepta o output JSON do
        // turn e dispara STATE_DELTA via SSE. Sem ele, o frontend chat
        // recebe só TEXT_MESSAGE_CONTENT (texto) e o output estruturado
        // não chega ao renderer no formato esperado. Pra Conversational é
        // o middleware que materializa o contrato { ui_component, message,
        // output } como evento AG-UI.
        var hasAgUiState = definition.Middlewares.Any(m =>
            string.Equals(m.Type, "StructuredOutputState", StringComparison.OrdinalIgnoreCase)
            && m.Enabled);
        if (!hasAgUiState)
        {
            warnings.Add(
                "Conversational recomenda middleware 'StructuredOutputState' — sem ele, o output " +
                "estruturado { ui_component, message, output } não dispara STATE_DELTA no SSE e o " +
                "frontend chat não consegue renderizar componentes em tempo real.");
        }

        // Modelo: warning quando deployment indica modelo grande/expensive.
        // Não bloqueia — chat com gpt-5/claude-opus faz sentido em casos
        // críticos (atendimento premium); só sinaliza custo de latência.
        if (!hasPredefined && !string.IsNullOrEmpty(deployment)
            && deployment.IndexOf("opus", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            warnings.Add(
                $"Conversational com 'model.deploymentName'='{deployment}' (modelo full premium) — " +
                "TTFT pode ficar acima de 1s. Avalie balanced (mini/sonnet) se latência for crítica.");
        }
    }
}
