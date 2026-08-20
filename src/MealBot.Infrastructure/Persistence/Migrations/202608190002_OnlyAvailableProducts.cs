using System;
using MealBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealBot.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MealBotDbContext))]
[Migration("202608190002_OnlyAvailableProducts")]
public partial class OnlyAvailableProducts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AllowMissingProducts",
            table: "MealPlans");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "AllowMissingProducts",
            table: "MealPlans",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }
}
