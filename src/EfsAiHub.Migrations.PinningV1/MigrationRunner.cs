using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Orchestration.Workflows;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EfsAiHub.Migrations.PinningV1;

internal sealed class MigrationRunner
{
    private readonly MigrationOptions _options;
    private readonly ILogger<MigrationRunner> _logger;

    public MigrationRunner(MigrationOptions options, ILogger<MigrationRunner> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            _logger.LogInformation("[Migration] Iniciando regeneração v1 final em transação.");

            // Evaluation runs históricos referenciam AgentVersionId via FK; como vamos
            // regenerar todos os agent_versions, esses runs ficam órfãos. Cleanup em
            // cascata pro DELETE de agent_versions liberar.
            var deletedProgress = await ExecuteAsync(conn, tx,
                "DELETE FROM aihub.evaluation_run_progress;", ct);
            var deletedResults = await ExecuteAsync(conn, tx,
                "DELETE FROM aihub.evaluation_results;", ct);
            var deletedRuns = await ExecuteAsync(conn, tx,
                "DELETE FROM aihub.evaluation_runs;", ct);
            _logger.LogInformation(
                "[Migration] Cleanup evaluation: {Runs} runs + {Progress} progress + {Results} results.",
                deletedRuns, deletedProgress, deletedResults);

            // Snapshots legacy + audit auto-pinned → tabula rasa.
            var deletedVersions = await ExecuteAsync(conn, tx,
                "DELETE FROM aihub.agent_versions;", ct);
            var deletedAudits = await ExecuteAsync(conn, tx,
                "DELETE FROM aihub.admin_audit_log WHERE \"Action\" = 'workflow.agent_version_auto_pinned';", ct);
            _logger.LogInformation(
                "[Migration] Deletados {Versions} agent_versions legacy + {Audits} audit rows obsoletas.",
                deletedVersions, deletedAudits);

            // Regenera AgentVersion (revision=1, BreakingChange=false, lossless) pra cada agent.
            var currentVersionByAgentId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var agents = await LoadAgentsAsync(conn, tx, ct);
            _logger.LogInformation("[Migration] {Count} agent_definitions encontrados.", agents.Count);

            foreach (var def in agents)
            {
                var promptInfo = await LoadActivePromptAsync(conn, tx, def.Id, ct);
                var version = AgentVersion.FromDefinition(
                    def,
                    revision: 1,
                    promptContent: promptInfo?.Content,
                    promptVersionId: promptInfo?.VersionId,
                    createdBy: "system:migration-pinning-v1",
                    changeReason: "pre-prod regen v1",
                    breakingChange: false);
                await InsertAgentVersionAsync(conn, tx, version, ct);
                currentVersionByAgentId[def.Id] = version.AgentVersionId;
            }
            _logger.LogInformation("[Migration] {Count} agent_versions regeneradas (SchemaVersion sumiu, BreakingChange=false).", agents.Count);

            // Auto-pin workflows ou deleta os que tem refs órfãos.
            var workflows = await LoadWorkflowsAsync(conn, tx, ct);
            _logger.LogInformation("[Migration] {Count} workflow_definitions encontrados.", workflows.Count);

            int pinned = 0;
            int deletedWorkflows = 0;
            var deletedIds = new List<string>();
            foreach (var (workflowId, workflow) in workflows)
            {
                var orphanRefs = workflow.Agents
                    .Where(a => !currentVersionByAgentId.ContainsKey(a.AgentId))
                    .Select(a => a.AgentId)
                    .ToList();

                if (orphanRefs.Count > 0)
                {
                    _logger.LogWarning(
                        "[Migration] Workflow '{WorkflowId}' tem {Count} agent refs órfãos: [{Ids}]. Deletando workflow + workflow_versions.",
                        workflowId, orphanRefs.Count, string.Join(", ", orphanRefs));
                    await DeleteWorkflowAsync(conn, tx, workflowId, ct);
                    deletedWorkflows++;
                    deletedIds.Add(workflowId);
                    continue;
                }

                foreach (var agentRef in workflow.Agents)
                    agentRef.AgentVersionId = currentVersionByAgentId[agentRef.AgentId];

                var newDataJson = JsonSerializer.Serialize(workflow, JsonDefaults.Domain);
                await UpdateWorkflowDataAsync(conn, tx, workflowId, newDataJson, ct);
                pinned++;
            }

            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "[Migration] Done. Agents regenerados: {Agents}. Workflows pinados: {Pinned}. Workflows deletados (orphan): {Deleted}{DeletedList}.",
                agents.Count, pinned, deletedWorkflows,
                deletedIds.Count == 0 ? "" : " — " + string.Join(", ", deletedIds));
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<AgentDefinition>> LoadAgentsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct)
    {
        const string sql = """
            SELECT "Id", "Name", "Data", "ProjectId", "Visibility", "TenantId", "AllowedProjectIds"
              FROM aihub.agent_definitions
             ORDER BY "Id";
            """;
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var list = new List<AgentDefinition>();
        while (await reader.ReadAsync(ct))
        {
            var data = reader.GetString(reader.GetOrdinal("Data"));
            var def = JsonSerializer.Deserialize<AgentDefinition>(data, JsonDefaults.Domain)
                ?? throw new InvalidOperationException($"AgentDefinition '{reader.GetString(0)}' tem JSON inválido em \"Data\".");

            // Promoted columns sobrescrevem o snapshot JSON (source of truth).
            // Id/Name são init-only; vêm do JSON corretos pois UpsertAsync grava
            // o JSON sempre com os valores que viraram Id/Name das colunas.
            def.ProjectId = reader.GetString(reader.GetOrdinal("ProjectId"));
            def.Visibility = reader.GetString(reader.GetOrdinal("Visibility"));
            def.TenantId = reader.GetString(reader.GetOrdinal("TenantId"));

            var allowedOrdinal = reader.GetOrdinal("AllowedProjectIds");
            if (!await reader.IsDBNullAsync(allowedOrdinal, ct))
            {
                var allowedJson = reader.GetString(allowedOrdinal);
                def.AllowedProjectIds = JsonSerializer.Deserialize<List<string>>(allowedJson, JsonDefaults.Domain);
            }

            list.Add(def);
        }
        return list;
    }

    private static async Task<(string Content, string VersionId)?> LoadActivePromptAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string agentId, CancellationToken ct)
    {
        const string sql = """
            SELECT "Content", "VersionId"
              FROM aihub.agent_prompt_versions
             WHERE "AgentId" = @agentId
               AND "IsActive" = TRUE
             ORDER BY "RowId" DESC
             LIMIT 1;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("agentId", agentId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct)) return null;
        return (reader.GetString(0), reader.GetString(1));
    }

    private static async Task InsertAgentVersionAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, AgentVersion version, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO aihub.agent_versions
                ("AgentVersionId","AgentDefinitionId","Revision","CreatedAt","CreatedBy","ChangeReason",
                 "Status","ContentHash","Snapshot","BreakingChange")
            VALUES
                (@vid,@aid,@rev,@createdAt,@createdBy,@reason,@status,@hash,@snapshot::jsonb,@breaking);
            """;
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("vid", version.AgentVersionId);
        cmd.Parameters.AddWithValue("aid", version.AgentDefinitionId);
        cmd.Parameters.AddWithValue("rev", version.Revision);
        cmd.Parameters.AddWithValue("createdAt", version.CreatedAt);
        cmd.Parameters.AddWithValue("createdBy", (object?)version.CreatedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("reason", (object?)version.ChangeReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", version.Status.ToString());
        cmd.Parameters.AddWithValue("hash", version.ContentHash);
        cmd.Parameters.AddWithValue("snapshot", JsonSerializer.Serialize(version, JsonDefaults.Domain));
        cmd.Parameters.AddWithValue("breaking", version.BreakingChange);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<(string Id, WorkflowDefinition Workflow)>> LoadWorkflowsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct)
    {
        const string sql = """
            SELECT "Id", "Data"
              FROM aihub.workflow_definitions
             ORDER BY "Id";
            """;
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var list = new List<(string, WorkflowDefinition)>();
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetString(0);
            var data = reader.GetString(1);
            var wf = JsonSerializer.Deserialize<WorkflowDefinition>(data, JsonDefaults.Domain)
                ?? throw new InvalidOperationException($"WorkflowDefinition '{id}' tem JSON inválido em \"Data\".");
            list.Add((id, wf));
        }
        return list;
    }

    private static async Task UpdateWorkflowDataAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string workflowId, string newDataJson, CancellationToken ct)
    {
        const string sql = """
            UPDATE aihub.workflow_definitions
               SET "Data" = @data, "UpdatedAt" = NOW()
             WHERE "Id" = @id;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", workflowId);
        cmd.Parameters.AddWithValue("data", newDataJson);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task DeleteWorkflowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string workflowId, CancellationToken ct)
    {
        const string deleteVersionsSql = """
            DELETE FROM aihub.workflow_versions WHERE "WorkflowDefinitionId" = @id;
            """;
        const string deleteDefinitionSql = """
            DELETE FROM aihub.workflow_definitions WHERE "Id" = @id;
            """;
        await using var v = new NpgsqlCommand(deleteVersionsSql, conn, tx);
        v.Parameters.AddWithValue("id", workflowId);
        await v.ExecuteNonQueryAsync(ct);

        await using var d = new NpgsqlCommand(deleteDefinitionSql, conn, tx);
        d.Parameters.AddWithValue("id", workflowId);
        await d.ExecuteNonQueryAsync(ct);
    }
}
