using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Couples.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntakeAndConcerns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "concern_categories",
                schema: "couples",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ContentWarning = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    AllowsCustomLabel = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_concern_categories", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "intake_profiles",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SkippedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intake_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_intake_profiles_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "couples",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "concern_selections",
                schema: "couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IntakeProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CustomLabel = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    WarningAcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_concern_selections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_concern_selections_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "couples",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_concern_selections_concern_categories_CategoryKey",
                        column: x => x.CategoryKey,
                        principalSchema: "couples",
                        principalTable: "concern_categories",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_concern_selections_intake_profiles_IntakeProfileId",
                        column: x => x.IntakeProfileId,
                        principalSchema: "couples",
                        principalTable: "intake_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "couples",
                table: "concern_categories",
                columns: new[] { "Key", "AllowsCustomLabel", "ContentWarning", "Description", "Name", "SortOrder" },
                values: new object[,]
                {
                    { "conflict", false, "If arguments involve threats, fear for your safety, or anyone being hurt, this app is not the right place to work through them. Please contact local emergency services or a domestic abuse support service.", "How disagreements start, how they escalate, and how you recover from them.", "Conflict and arguments", 50 },
                    { "custom", true, null, "Name a topic in your own words.", "Something else", 100 },
                    { "finances", false, null, "Spending, saving, debt, income, and how you make money decisions together.", "Money and finances", 10 },
                    { "infidelity", false, "This topic can bring up intense feelings. Go at your own pace and take a break whenever you need. What you write here stays private to you.", "An affair, a secret relationship, or another breach of what you agreed.", "Infidelity or betrayal", 30 },
                    { "intimacy", false, "This topic includes your sexual relationship. Write only what you are comfortable with. What you write here stays private to you.", "Emotional and physical closeness, affection and your sexual relationship.", "Intimacy and closeness", 60 },
                    { "parenting", false, null, "Raising children, step-family life, wider family, and sharing the load at home.", "Parenting and family", 40 },
                    { "trust", false, null, "Feeling able to rely on each other: honesty, keeping promises and following through.", "Trust", 20 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_concern_selections_CategoryKey",
                schema: "couples",
                table: "concern_selections",
                column: "CategoryKey");

            migrationBuilder.CreateIndex(
                name: "IX_concern_selections_IntakeProfileId",
                schema: "couples",
                table: "concern_selections",
                column: "IntakeProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_concern_selections_OwnerUserId",
                schema: "couples",
                table: "concern_selections",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_intake_profiles_OwnerUserId",
                schema: "couples",
                table: "intake_profiles",
                column: "OwnerUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "concern_selections",
                schema: "couples");

            migrationBuilder.DropTable(
                name: "concern_categories",
                schema: "couples");

            migrationBuilder.DropTable(
                name: "intake_profiles",
                schema: "couples");
        }
    }
}
