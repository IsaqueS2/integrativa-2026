using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Confeitaria.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseImportsAndInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Ingredient_Amounts",
                table: "Ingredients");

            migrationBuilder.AddColumn<string>(
                name: "StockConsumptionJson",
                table: "Sales",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StockQuantityMicros",
                table: "Ingredients",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PurchaseDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AccessKey = table.Column<string>(type: "TEXT", maxLength: 44, nullable: true),
                    InvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Supplier = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PurchaseDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TotalCents = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpenseId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseDocuments", x => x.Id);
                    table.CheckConstraint("CK_PurchaseDocument_Values", "length(DocumentHash) = 64 AND TotalCents > 0");
                    table.ForeignKey(
                        name: "FK_PurchaseDocuments_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Ingredient_Amounts",
                table: "Ingredients",
                sql: "PackageQuantityMicros > 0 AND PackagePriceCents >= 0 AND (StockQuantityMicros IS NULL OR StockQuantityMicros >= 0) AND Dimension BETWEEN 0 AND 2");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseDocuments_AccessKey",
                table: "PurchaseDocuments",
                column: "AccessKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseDocuments_DocumentHash",
                table: "PurchaseDocuments",
                column: "DocumentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseDocuments_ExpenseId",
                table: "PurchaseDocuments",
                column: "ExpenseId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseDocuments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Ingredient_Amounts",
                table: "Ingredients");

            migrationBuilder.DropColumn(
                name: "StockConsumptionJson",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "StockQuantityMicros",
                table: "Ingredients");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Ingredient_Amounts",
                table: "Ingredients",
                sql: "PackageQuantityMicros > 0 AND PackagePriceCents >= 0 AND Dimension BETWEEN 0 AND 2");
        }
    }
}
