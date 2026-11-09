using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Couples.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AsyncResponsesAndPace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InReplyToMessageId",
                schema: "couples",
                table: "shared_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecipientOpenedAt",
                schema: "couples",
                table: "shared_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AboutMessageId",
                schema: "couples",
                table: "private_reflections",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InReplyToMessageId",
                schema: "couples",
                table: "draft_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisibleAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notifications_AspNetUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalSchema: "couples",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pace_preferences",
                schema: "couples",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuietStart = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    QuietEnd = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Muted = table.Column<bool>(type: "boolean", nullable: false),
                    PausedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pace_preferences", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_pace_preferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "couples",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shared_messages_InReplyToMessageId",
                schema: "couples",
                table: "shared_messages",
                column: "InReplyToMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_private_reflections_AboutMessageId",
                schema: "couples",
                table: "private_reflections",
                column: "AboutMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_private_reflections_OwnerUserId_AboutMessageId",
                schema: "couples",
                table: "private_reflections",
                columns: new[] { "OwnerUserId", "AboutMessageId" },
                unique: true,
                filter: "\"AboutMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_draft_messages_InReplyToMessageId",
                schema: "couples",
                table: "draft_messages",
                column: "InReplyToMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_RecipientUserId_VisibleAt",
                schema: "couples",
                table: "notifications",
                columns: new[] { "RecipientUserId", "VisibleAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_SubjectId",
                schema: "couples",
                table: "notifications",
                column: "SubjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_draft_messages_shared_messages_InReplyToMessageId",
                schema: "couples",
                table: "draft_messages",
                column: "InReplyToMessageId",
                principalSchema: "couples",
                principalTable: "shared_messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_private_reflections_shared_messages_AboutMessageId",
                schema: "couples",
                table: "private_reflections",
                column: "AboutMessageId",
                principalSchema: "couples",
                principalTable: "shared_messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_shared_messages_shared_messages_InReplyToMessageId",
                schema: "couples",
                table: "shared_messages",
                column: "InReplyToMessageId",
                principalSchema: "couples",
                principalTable: "shared_messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_draft_messages_shared_messages_InReplyToMessageId",
                schema: "couples",
                table: "draft_messages");

            migrationBuilder.DropForeignKey(
                name: "FK_private_reflections_shared_messages_AboutMessageId",
                schema: "couples",
                table: "private_reflections");

            migrationBuilder.DropForeignKey(
                name: "FK_shared_messages_shared_messages_InReplyToMessageId",
                schema: "couples",
                table: "shared_messages");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "couples");

            migrationBuilder.DropTable(
                name: "pace_preferences",
                schema: "couples");

            migrationBuilder.DropIndex(
                name: "IX_shared_messages_InReplyToMessageId",
                schema: "couples",
                table: "shared_messages");

            migrationBuilder.DropIndex(
                name: "IX_private_reflections_AboutMessageId",
                schema: "couples",
                table: "private_reflections");

            migrationBuilder.DropIndex(
                name: "IX_private_reflections_OwnerUserId_AboutMessageId",
                schema: "couples",
                table: "private_reflections");

            migrationBuilder.DropIndex(
                name: "IX_draft_messages_InReplyToMessageId",
                schema: "couples",
                table: "draft_messages");

            migrationBuilder.DropColumn(
                name: "InReplyToMessageId",
                schema: "couples",
                table: "shared_messages");

            migrationBuilder.DropColumn(
                name: "RecipientOpenedAt",
                schema: "couples",
                table: "shared_messages");

            migrationBuilder.DropColumn(
                name: "AboutMessageId",
                schema: "couples",
                table: "private_reflections");

            migrationBuilder.DropColumn(
                name: "InReplyToMessageId",
                schema: "couples",
                table: "draft_messages");
        }
    }
}
