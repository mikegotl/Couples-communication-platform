using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Couples.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JointChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "joint_chat_eligibility",
                schema: "couples",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_joint_chat_eligibility", x => x.WorkspaceId);
                    table.ForeignKey(
                        name: "FK_joint_chat_eligibility_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalSchema: "couples",
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "joint_chat_sessions",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    InitiatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InviteeUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InvitedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndReason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    EndedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TurnUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TurnStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PausedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PausedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MessageCount = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_joint_chat_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_joint_chat_sessions_AspNetUsers_InitiatorUserId",
                        column: x => x.InitiatorUserId,
                        principalSchema: "couples",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_joint_chat_sessions_AspNetUsers_InviteeUserId",
                        column: x => x.InviteeUserId,
                        principalSchema: "couples",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_joint_chat_sessions_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalSchema: "couples",
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "joint_chat_messages",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_joint_chat_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_joint_chat_messages_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalSchema: "couples",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_joint_chat_messages_joint_chat_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "couples",
                        principalTable: "joint_chat_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_joint_chat_messages_AuthorUserId",
                schema: "couples",
                table: "joint_chat_messages",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_joint_chat_messages_SessionId_SentAt",
                schema: "couples",
                table: "joint_chat_messages",
                columns: new[] { "SessionId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_joint_chat_sessions_InitiatorUserId",
                schema: "couples",
                table: "joint_chat_sessions",
                column: "InitiatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_joint_chat_sessions_InviteeUserId",
                schema: "couples",
                table: "joint_chat_sessions",
                column: "InviteeUserId");

            migrationBuilder.CreateIndex(
                name: "ix_joint_chat_sessions_open_per_workspace",
                schema: "couples",
                table: "joint_chat_sessions",
                column: "WorkspaceId",
                unique: true,
                filter: "\"Status\" <> 'Ended'");

            migrationBuilder.CreateIndex(
                name: "IX_joint_chat_sessions_WorkspaceId_EndedAt",
                schema: "couples",
                table: "joint_chat_sessions",
                columns: new[] { "WorkspaceId", "EndedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "joint_chat_eligibility",
                schema: "couples");

            migrationBuilder.DropTable(
                name: "joint_chat_messages",
                schema: "couples");

            migrationBuilder.DropTable(
                name: "joint_chat_sessions",
                schema: "couples");
        }
    }
}
