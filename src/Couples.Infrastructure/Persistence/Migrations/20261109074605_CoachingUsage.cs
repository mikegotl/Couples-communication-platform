using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Couples.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoachingUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "coaching_usage",
                schema: "couples",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Requests = table.Column<int>(type: "integer", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coaching_usage", x => new { x.UserId, x.Day });
                    table.ForeignKey(
                        name: "FK_coaching_usage_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "couples",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_coaching_usage_Day",
                schema: "couples",
                table: "coaching_usage",
                column: "Day");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coaching_usage",
                schema: "couples");
        }
    }
}
