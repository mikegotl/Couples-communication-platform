using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Couples.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SmsMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sms_deliveries",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sms_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sms_deliveries_AspNetUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalSchema: "couples",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sms_settings",
                schema: "couples",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneE164 = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceivingSince = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IncludeMessageText = table.Column<bool>(type: "boolean", nullable: false),
                    OptedOutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CodeExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CodeAttempts = table.Column<int>(type: "integer", nullable: false),
                    CodeWindowStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CodesInWindow = table.Column<int>(type: "integer", nullable: false),
                    PendingDraftId = table.Column<Guid>(type: "uuid", nullable: true),
                    PendingFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PendingExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sms_settings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_sms_settings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "couples",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sms_deliveries_NotificationId",
                schema: "couples",
                table: "sms_deliveries",
                column: "NotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sms_deliveries_RecipientUserId_CreatedAt",
                schema: "couples",
                table: "sms_deliveries",
                columns: new[] { "RecipientUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sms_settings_PhoneE164",
                schema: "couples",
                table: "sms_settings",
                column: "PhoneE164",
                unique: true,
                filter: "\"VerifiedAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sms_deliveries",
                schema: "couples");

            migrationBuilder.DropTable(
                name: "sms_settings",
                schema: "couples");
        }
    }
}
