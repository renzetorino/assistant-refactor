using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace dataAccess.Migrations.AiDb
{
    /// <inheritdoc />
    public partial class AddPendingStateToSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "chat_sessions",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_activity_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    message_count = table.Column<int>(type: "integer", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    pending_plan_json = table.Column<string>(type: "jsonb", nullable: true),
                    pending_slot_name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "faq_search_logs",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    query = table.Column<string>(type: "text", nullable: false),
                    intent = table.Column<string>(type: "text", nullable: false),
                    answer_snippet = table.Column<string>(type: "text", nullable: true),
                    confidence = table.Column<decimal>(type: "numeric", nullable: false),
                    helpful = table.Column<bool>(type: "boolean", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_faq_search_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    intent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    domain = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    confidence = table.Column<decimal>(type: "numeric", nullable: true),
                    sql_generated = table.Column<string>(type: "text", nullable: true),
                    sql_validated = table.Column<bool>(type: "boolean", nullable: true),
                    sql_executed = table.Column<bool>(type: "boolean", nullable: true),
                    result_rows = table.Column<int>(type: "integer", nullable: true),
                    result_summary = table.Column<string>(type: "jsonb", nullable: true),
                    analysis = table.Column<string>(type: "text", nullable: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: true),
                    model_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_messages_chat_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "public",
                        principalTable: "chat_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_feedback",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feedback_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_feedback", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_feedback_chat_messages_message_id",
                        column: x => x.message_id,
                        principalSchema: "public",
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_feedback_chat_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "public",
                        principalTable: "chat_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_chat_feedback_feedback_type",
                schema: "public",
                table: "chat_feedback",
                column: "feedback_type");

            migrationBuilder.CreateIndex(
                name: "idx_chat_feedback_message_id",
                schema: "public",
                table: "chat_feedback",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "idx_chat_feedback_session_id",
                schema: "public",
                table: "chat_feedback",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "idx_chat_feedback_user_id",
                schema: "public",
                table: "chat_feedback",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "idx_chat_messages_created_at",
                schema: "public",
                table: "chat_messages",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "idx_chat_messages_intent",
                schema: "public",
                table: "chat_messages",
                column: "intent");

            migrationBuilder.CreateIndex(
                name: "idx_chat_messages_session_id",
                schema: "public",
                table: "chat_messages",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "idx_chat_sessions_expires_at",
                schema: "public",
                table: "chat_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "idx_chat_sessions_user_id",
                schema: "public",
                table: "chat_sessions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_feedback",
                schema: "public");

            migrationBuilder.DropTable(
                name: "faq_search_logs",
                schema: "public");

            migrationBuilder.DropTable(
                name: "chat_messages",
                schema: "public");

            migrationBuilder.DropTable(
                name: "chat_sessions",
                schema: "public");
        }
    }
}
