using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Couples.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DateRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credit_entries",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "date", nullable: true),
                    WeekScore = table.Column<int>(type: "integer", nullable: true),
                    RedemptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_credit_entries_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalSchema: "couples",
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reward_accounts",
                schema: "couples",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Balance = table.Column<int>(type: "integer", nullable: false),
                    NextWeekToScore = table.Column<DateOnly>(type: "date", nullable: false),
                    PairSince = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Area = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reward_accounts", x => x.WorkspaceId);
                    table.ForeignKey(
                        name: "FK_reward_accounts_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalSchema: "couples",
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reward_offers",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PartnerName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Area = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CostCredits = table.Column<int>(type: "integer", nullable: false),
                    IsSample = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reward_offers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reward_redemptions",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    RedeemedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferTitle = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PartnerName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CostCredits = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    VoucherCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    IsSample = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reward_redemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reward_redemptions_reward_offers_OfferId",
                        column: x => x.OfferId,
                        principalSchema: "couples",
                        principalTable: "reward_offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reward_redemptions_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalSchema: "couples",
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_credit_entries_WorkspaceId_CreatedAt",
                schema: "couples",
                table: "credit_entries",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ux_credit_entries_week",
                schema: "couples",
                table: "credit_entries",
                columns: new[] { "WorkspaceId", "WeekStart" },
                unique: true,
                filter: "\"Kind\" = 'WeeklyEarned'");

            migrationBuilder.CreateIndex(
                name: "IX_reward_offers_IsActive_CostCredits",
                schema: "couples",
                table: "reward_offers",
                columns: new[] { "IsActive", "CostCredits" });

            migrationBuilder.CreateIndex(
                name: "IX_reward_redemptions_OfferId",
                schema: "couples",
                table: "reward_redemptions",
                column: "OfferId");

            migrationBuilder.CreateIndex(
                name: "IX_reward_redemptions_RedeemedByUserId",
                schema: "couples",
                table: "reward_redemptions",
                column: "RedeemedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_reward_redemptions_WorkspaceId_CreatedAt",
                schema: "couples",
                table: "reward_redemptions",
                columns: new[] { "WorkspaceId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_entries",
                schema: "couples");

            migrationBuilder.DropTable(
                name: "reward_accounts",
                schema: "couples");

            migrationBuilder.DropTable(
                name: "reward_redemptions",
                schema: "couples");

            migrationBuilder.DropTable(
                name: "reward_offers",
                schema: "couples");
        }
    }
}
