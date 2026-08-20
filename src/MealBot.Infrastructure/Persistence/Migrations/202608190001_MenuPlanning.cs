using System;
using MealBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealBot.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MealBotDbContext))]
[Migration("202608190001_MenuPlanning")]
public partial class MenuPlanning : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MealPlans",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                DaysCount = table.Column<int>(type: "integer", nullable: false),
                AllowMissingProducts = table.Column<bool>(type: "boolean", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MealPlans", x => x.Id);
                table.ForeignKey(
                    name: "FK_MealPlans_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PlannedMeals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MealPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                MealDate = table.Column<DateOnly>(type: "date", nullable: false),
                MealType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Servings = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlannedMeals", x => x.Id);
                table.ForeignKey(
                    name: "FK_PlannedMeals_MealPlans_MealPlanId",
                    column: x => x.MealPlanId,
                    principalTable: "MealPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MealPlans_UserId",
            table: "MealPlans",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_PlannedMeals_MealPlanId_MealDate_MealType",
            table: "PlannedMeals",
            columns: new[] { "MealPlanId", "MealDate", "MealType" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PlannedMeals");
        migrationBuilder.DropTable(name: "MealPlans");
    }
}
