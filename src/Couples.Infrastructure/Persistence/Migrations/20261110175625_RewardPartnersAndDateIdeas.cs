using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Couples.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RewardPartnersAndDateIdeas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UsedAt",
                schema: "couples",
                table: "reward_redemptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PartnerId",
                schema: "couples",
                table: "reward_offers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "date_ideas",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Area = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Url = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_date_ideas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reward_partners",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ContactName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Website = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Area = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProposedOffer = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CheckKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reward_partners", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reward_redemptions_VoucherCode",
                schema: "couples",
                table: "reward_redemptions",
                column: "VoucherCode");

            migrationBuilder.CreateIndex(
                name: "IX_reward_offers_PartnerId",
                schema: "couples",
                table: "reward_offers",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_reward_partners_CheckKeyHash",
                schema: "couples",
                table: "reward_partners",
                column: "CheckKeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reward_partners_Status_CreatedAt",
                schema: "couples",
                table: "reward_partners",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_reward_offers_reward_partners_PartnerId",
                schema: "couples",
                table: "reward_offers",
                column: "PartnerId",
                principalSchema: "couples",
                principalTable: "reward_partners",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reward_offers_reward_partners_PartnerId",
                schema: "couples",
                table: "reward_offers");

            migrationBuilder.DropTable(
                name: "date_ideas",
                schema: "couples");

            migrationBuilder.DropTable(
                name: "reward_partners",
                schema: "couples");

            migrationBuilder.DropIndex(
                name: "IX_reward_redemptions_VoucherCode",
                schema: "couples",
                table: "reward_redemptions");

            migrationBuilder.DropIndex(
                name: "IX_reward_offers_PartnerId",
                schema: "couples",
                table: "reward_offers");

            migrationBuilder.DropColumn(
                name: "UsedAt",
                schema: "couples",
                table: "reward_redemptions");

            migrationBuilder.DropColumn(
                name: "PartnerId",
                schema: "couples",
                table: "reward_offers");
        }
    }
}
