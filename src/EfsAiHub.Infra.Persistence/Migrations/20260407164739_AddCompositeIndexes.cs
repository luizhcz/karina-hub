using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EfsAiHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompositeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_definitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Data = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agent_prompt_versions",
                columns: table => new
                {
                    RowId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AgentId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    VersionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_prompt_versions", x => x.RowId);
                });

            migrationBuilder.CreateTable(
                name: "agent_sessions",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AgentId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SerializedState = table.Column<string>(type: "text", nullable: false),
                    TurnCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAccessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_sessions", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "ativos",
                columns: table => new
                {
                    Ticker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Nome = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Setor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Descricao = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ativos", x => x.Ticker);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    MessageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConversationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    StructuredOutput = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TokenCount = table.Column<int>(type: "integer", nullable: false),
                    ExecutionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_messages", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    ConversationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UserType = table.Column<string>(type: "text", nullable: true),
                    WorkflowId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ActiveExecutionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastActiveAgentId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ContextClearedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastMessageAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversations", x => x.ConversationId);
                });

            migrationBuilder.CreateTable(
                name: "human_interactions",
                columns: table => new
                {
                    InteractionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExecutionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    WorkflowId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    Context = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Resolution = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_human_interactions", x => x.InteractionId);
                });

            migrationBuilder.CreateTable(
                name: "llm_token_usage",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AgentId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ModelId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExecutionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    WorkflowId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalTokens = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<double>(type: "double precision", nullable: false),
                    PromptVersionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_llm_token_usage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "model_pricing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModelId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PricePerInputToken = table.Column<decimal>(type: "numeric(20,10)", nullable: false),
                    PricePerOutputToken = table.Column<decimal>(type: "numeric(20,10)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_pricing", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "node_executions",
                columns: table => new
                {
                    RowId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExecutionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NodeId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Data = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_node_executions", x => x.RowId);
                });

            migrationBuilder.CreateTable(
                name: "tool_invocations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExecutionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AgentId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ToolName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Arguments = table.Column<string>(type: "jsonb", nullable: true),
                    Result = table.Column<string>(type: "text", nullable: true),
                    DurationMs = table.Column<double>(type: "double precision", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_invocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_checkpoints",
                columns: table => new
                {
                    ExecutionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_checkpoints", x => x.ExecutionId);
                });

            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Data = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_event_audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExecutionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_event_audit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_executions",
                columns: table => new
                {
                    ExecutionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    WorkflowId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Data = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_executions", x => x.ExecutionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_prompt_versions_AgentId",
                table: "agent_prompt_versions",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_prompt_versions_AgentId_VersionId",
                table: "agent_prompt_versions",
                columns: new[] { "AgentId", "VersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_sessions_AgentId",
                table: "agent_sessions",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_sessions_ExpiresAt",
                table: "agent_sessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ativos_Setor",
                table: "ativos",
                column: "Setor");

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_ConversationId",
                table: "chat_messages",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_ConversationId_CreatedAt",
                table: "chat_messages",
                columns: new[] { "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_LastMessageAt",
                table: "conversations",
                column: "LastMessageAt");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_UserId",
                table: "conversations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_human_interactions_ExecutionId",
                table: "human_interactions",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_human_interactions_Status",
                table: "human_interactions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_llm_token_usage_AgentId",
                table: "llm_token_usage",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_llm_token_usage_CreatedAt",
                table: "llm_token_usage",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_llm_token_usage_ExecutionId",
                table: "llm_token_usage",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_model_pricing_ModelId",
                table: "model_pricing",
                column: "ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_node_executions_ExecutionId",
                table: "node_executions",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_node_executions_ExecutionId_NodeId",
                table: "node_executions",
                columns: new[] { "ExecutionId", "NodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tool_invocations_AgentId",
                table: "tool_invocations",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_tool_invocations_ExecutionId",
                table: "tool_invocations",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_tool_invocations_ToolName",
                table: "tool_invocations",
                column: "ToolName");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_event_audit_ExecutionId",
                table: "workflow_event_audit",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_event_audit_ExecutionId_Id",
                table: "workflow_event_audit",
                columns: new[] { "ExecutionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_event_audit_Timestamp",
                table: "workflow_event_audit",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_executions_StartedAt",
                table: "workflow_executions",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_executions_Status",
                table: "workflow_executions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_executions_WorkflowId",
                table: "workflow_executions",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_executions_WorkflowId_Status_StartedAt",
                table: "workflow_executions",
                columns: new[] { "WorkflowId", "Status", "StartedAt" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_definitions");

            migrationBuilder.DropTable(
                name: "agent_prompt_versions");

            migrationBuilder.DropTable(
                name: "agent_sessions");

            migrationBuilder.DropTable(
                name: "ativos");

            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "human_interactions");

            migrationBuilder.DropTable(
                name: "llm_token_usage");

            migrationBuilder.DropTable(
                name: "model_pricing");

            migrationBuilder.DropTable(
                name: "node_executions");

            migrationBuilder.DropTable(
                name: "tool_invocations");

            migrationBuilder.DropTable(
                name: "workflow_checkpoints");

            migrationBuilder.DropTable(
                name: "workflow_definitions");

            migrationBuilder.DropTable(
                name: "workflow_event_audit");

            migrationBuilder.DropTable(
                name: "workflow_executions");
        }
    }
}
