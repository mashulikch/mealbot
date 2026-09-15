using System;
using MealBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealBot.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MealBotDbContext))]
[Migration("202609140001_ProductAliases")]
public partial class ProductAliases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProductAliases",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                Alias = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                NormalizedAlias = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductAliases", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProductAliases_Products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProductAliases_NormalizedAlias",
            table: "ProductAliases",
            column: "NormalizedAlias",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProductAliases_ProductId",
            table: "ProductAliases",
            column: "ProductId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProductAliases");
    }
}
