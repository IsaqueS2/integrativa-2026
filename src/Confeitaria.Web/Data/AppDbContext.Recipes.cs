using Confeitaria.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Confeitaria.Web.Data;

public partial class AppDbContext
{
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeItem> RecipeItems => Set<RecipeItem>();
    public DbSet<PurchaseDocument> PurchaseDocuments => Set<PurchaseDocument>();

    partial void ConfigureRecipes(ModelBuilder b)
    {
        b.Entity<Ingredient>().ToTable("Ingredients", t => t.HasCheckConstraint("CK_Ingredient_Amounts", "PackageQuantityMicros > 0 AND PackagePriceCents >= 0 AND (StockQuantityMicros IS NULL OR StockQuantityMicros >= 0) AND Dimension BETWEEN 0 AND 2"));
        b.Entity<Ingredient>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Ingredient>().Property(x => x.Name).HasMaxLength(120);
        b.Entity<Recipe>().ToTable("Recipes", t => t.HasCheckConstraint("CK_Recipe_Amounts", "YieldQuantityMicros > 0 AND PackagingCents >= 0 AND LaborMinutesMicros >= 0 AND HourlyRateCents >= 0 AND AllocatedCents >= 0 AND LossBasisPoints BETWEEN 0 AND 10000"));
        b.Entity<Recipe>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Recipe>().Property(x => x.Name).HasMaxLength(120);
        b.Entity<Recipe>().Property(x => x.YieldUnit).HasMaxLength(20);
        b.Entity<Recipe>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RecipeItem>().ToTable("RecipeItems", t => t.HasCheckConstraint("CK_RecipeItem_Quantity", "QuantityMicros > 0"));
        b.Entity<RecipeItem>().HasIndex(x => new { x.RecipeId, x.IngredientId }).IsUnique();
        b.Entity<RecipeItem>().HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Sale>().HasOne<Recipe>().WithMany().HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PurchaseDocument>().ToTable("PurchaseDocuments", t => t.HasCheckConstraint("CK_PurchaseDocument_Values", "length(DocumentHash) = 64 AND TotalCents > 0"));
        b.Entity<PurchaseDocument>().Property(x => x.DocumentHash).HasMaxLength(64);
        b.Entity<PurchaseDocument>().Property(x => x.AccessKey).HasMaxLength(44);
        b.Entity<PurchaseDocument>().Property(x => x.InvoiceNumber).HasMaxLength(40);
        b.Entity<PurchaseDocument>().Property(x => x.Supplier).HasMaxLength(200);
        b.Entity<PurchaseDocument>().HasIndex(x => x.DocumentHash).IsUnique();
        b.Entity<PurchaseDocument>().HasIndex(x => x.AccessKey).IsUnique();
        b.Entity<PurchaseDocument>().HasIndex(x => x.ExpenseId).IsUnique();
        b.Entity<PurchaseDocument>().HasOne<Expense>().WithMany().HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Restrict);
    }
}
