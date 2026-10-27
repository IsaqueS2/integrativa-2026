using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Confeitaria.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecipesAndPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Ingredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Dimension = table.Column<int>(type: "INTEGER", nullable: false),
                    PackageQuantityMicros = table.Column<long>(type: "INTEGER", nullable: false),
                    PackagePriceCents = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ingredients", x => x.Id);
                    table.CheckConstraint("CK_Ingredient_Amounts", "PackageQuantityMicros > 0 AND PackagePriceCents >= 0 AND Dimension BETWEEN 0 AND 2");
                });

            migrationBuilder.CreateTable(
                name: "Recipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    YieldQuantityMicros = table.Column<long>(type: "INTEGER", nullable: false),
                    YieldUnit = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PackagingCents = table.Column<long>(type: "INTEGER", nullable: false),
                    LaborMinutesMicros = table.Column<long>(type: "INTEGER", nullable: false),
                    HourlyRateCents = table.Column<long>(type: "INTEGER", nullable: false),
                    LossBasisPoints = table.Column<int>(type: "INTEGER", nullable: false),
                    AllocatedCents = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recipes", x => x.Id);
                    table.CheckConstraint("CK_Recipe_Amounts", "YieldQuantityMicros > 0 AND PackagingCents >= 0 AND LaborMinutesMicros >= 0 AND HourlyRateCents >= 0 AND AllocatedCents >= 0 AND LossBasisPoints BETWEEN 0 AND 10000");
                });

            migrationBuilder.CreateTable(
                name: "RecipeItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    IngredientId = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityMicros = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeItems", x => x.Id);
                    table.CheckConstraint("CK_RecipeItem_Quantity", "QuantityMicros > 0");
                    table.ForeignKey(
                        name: "FK_RecipeItems_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecipeItems_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_RecipeId",
                table: "Sales",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_Name",
                table: "Ingredients",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecipeItems_IngredientId",
                table: "RecipeItems",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeItems_RecipeId_IngredientId",
                table: "RecipeItems",
                columns: new[] { "RecipeId", "IngredientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_Name",
                table: "Recipes",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Recipes_RecipeId",
                table: "Sales",
                column: "RecipeId",
                principalTable: "Recipes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Recipes_RecipeId",
                table: "Sales");

            migrationBuilder.DropTable(
                name: "RecipeItems");

            migrationBuilder.DropTable(
                name: "Ingredients");

            migrationBuilder.DropTable(
                name: "Recipes");

            migrationBuilder.DropIndex(
                name: "IX_Sales_RecipeId",
                table: "Sales");
        }
    }
}
