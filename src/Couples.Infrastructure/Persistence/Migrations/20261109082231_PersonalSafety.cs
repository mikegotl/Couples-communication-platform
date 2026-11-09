using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Couples.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersonalSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "personal_safety",
                schema: "couples",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnswersJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ContentVersion = table.Column<int>(type: "integer", nullable: true),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SelfHarmSupport = table.Column<bool>(type: "boolean", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SafetyMode = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personal_safety", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_personal_safety_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "couples",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "personal_safety",
                schema: "couples");
        }
    }
}
