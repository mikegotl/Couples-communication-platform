using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Couples.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrivacyConsentAndContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workspaces_AspNetUsers_CreatedByUserId",
                schema: "couples",
                table: "workspaces");

            migrationBuilder.DropIndex(
                name: "IX_workspaces_CreatedByUserId",
                schema: "couples",
                table: "workspaces");

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "consent_records",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WithdrawnAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consent_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_consent_records_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "couples",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "data_deletion_requests",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_deletion_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "draft_messages",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DiscardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_draft_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_draft_messages_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "couples",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_draft_messages_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalSchema: "couples",
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "private_reflections",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_private_reflections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_private_reflections_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "couples",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shared_messages",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDraftId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConsentRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SharedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shared_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shared_messages_AspNetUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalSchema: "couples",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_shared_messages_AspNetUsers_SenderUserId",
                        column: x => x.SenderUserId,
                        principalSchema: "couples",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_shared_messages_draft_messages_SourceDraftId",
                        column: x => x.SourceDraftId,
                        principalSchema: "couples",
                        principalTable: "draft_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_shared_messages_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalSchema: "couples",
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_ActorUserId",
                schema: "couples",
                table: "audit_events",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_OccurredAt",
                schema: "couples",
                table: "audit_events",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_consent_records_UserId_Purpose_SubjectId",
                schema: "couples",
                table: "consent_records",
                columns: new[] { "UserId", "Purpose", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_data_deletion_requests_Status",
                schema: "couples",
                table: "data_deletion_requests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_draft_messages_OwnerUserId",
                schema: "couples",
                table: "draft_messages",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_draft_messages_WorkspaceId",
                schema: "couples",
                table: "draft_messages",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_private_reflections_OwnerUserId",
                schema: "couples",
                table: "private_reflections",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_shared_messages_RecipientUserId_SharedAt",
                schema: "couples",
                table: "shared_messages",
                columns: new[] { "RecipientUserId", "SharedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_shared_messages_SenderUserId_SharedAt",
                schema: "couples",
                table: "shared_messages",
                columns: new[] { "SenderUserId", "SharedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_shared_messages_SourceDraftId",
                schema: "couples",
                table: "shared_messages",
                column: "SourceDraftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shared_messages_WorkspaceId",
                schema: "couples",
                table: "shared_messages",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "couples");

            migrationBuilder.DropTable(
                name: "consent_records",
                schema: "couples");

            migrationBuilder.DropTable(
                name: "data_deletion_requests",
                schema: "couples");

            migrationBuilder.DropTable(
                name: "private_reflections",
                schema: "couples");

            migrationBuilder.DropTable(
                name: "shared_messages",
                schema: "couples");

            migrationBuilder.DropTable(
                name: "draft_messages",
                schema: "couples");

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_CreatedByUserId",
                schema: "couples",
                table: "workspaces",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_workspaces_AspNetUsers_CreatedByUserId",
                schema: "couples",
                table: "workspaces",
                column: "CreatedByUserId",
                principalSchema: "couples",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
