using Npgsql;

namespace EfsAiHub.Tests.Integration.Fixtures;

/// <summary>
/// Helper para limpar tabelas entre testes de integração,
/// evitando dados residuais que causam flaky tests.
/// </summary>
public static class DatabaseCleanup
{
    /// <summary>
    /// Tabelas que podem ser truncadas com segurança entre testes.
    /// Ordenadas para respeitar foreign keys (filhas antes de pais).
    /// </summary>
    private static readonly string[] TruncatableTables =
    [
        "aihub.document_extraction_events",
        "aihub.document_extraction_cache",
        "aihub.document_extraction_jobs",
        "aihub.workflow_event_audit",
        "aihub.workflow_checkpoints",
        "aihub.node_executions",
        "aihub.tool_invocations",
        "aihub.llm_token_usage",
        "aihub.workflow_executions",
        "aihub.human_interactions",
        "aihub.background_response_jobs",
        "aihub.agent_prompt_versions",
        "aihub.agent_sessions",
        "aihub.chat_messages",
        "aihub.conversations",
        "aihub.model_catalog",
        "aihub.model_pricing",
        "aihub.agent_versions",
        "aihub.workflow_versions",
        "aihub.skill_versions",
        "aihub.skills",
        "aihub.agent_definitions",
        "aihub.workflow_definitions",
    ];

    /// <summary>
    /// Trunca todas as tabelas de dados de teste usando CASCADE.
    /// Chamado entre testes para garantir isolamento.
    /// </summary>
    public static async Task TruncateAllAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var tables = string.Join(", ", TruncatableTables);
        await using var cmd = new NpgsqlCommand($"TRUNCATE {tables} CASCADE;", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Trunca tabelas específicas. Use quando o teste só afeta algumas tabelas.
    /// </summary>
    public static async Task TruncateAsync(string connectionString, params string[] tables)
    {
        if (tables.Length == 0) return;

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var tableList = string.Join(", ", tables);
        await using var cmd = new NpgsqlCommand($"TRUNCATE {tableList} CASCADE;", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
