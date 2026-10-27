using Confeitaria.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Confeitaria.Web.Data;

public partial class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<BusinessSettings> Settings => Set<BusinessSettings>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<CashEntry> CashEntries => Set<CashEntry>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<BusinessSettings>().ToTable("Settings", t => t.HasCheckConstraint("CK_Settings", "Id = 1 AND OpeningBalanceCents >= 0 AND ReserveCents >= 0"));
        b.Entity<Category>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Channel>().HasIndex(x => x.Name).IsUnique();
        b.Entity<PaymentMethod>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Channel>().ToTable("Channels", t => t.HasCheckConstraint("CK_Channel_Fees", "RateBasisPoints BETWEEN 0 AND 10000 AND FixedFeeCents >= 0"));
        b.Entity<PaymentMethod>().ToTable("PaymentMethods", t => t.HasCheckConstraint("CK_Payment_Fees", "RateBasisPoints BETWEEN 0 AND 10000 AND FixedFeeCents >= 0"));
        b.Entity<Sale>().ToTable("Sales", t => t.HasCheckConstraint("CK_Sale_Amounts", "GrossCents > 0 AND DiscountCents >= 0 AND DiscountCents <= GrossCents AND EstimatedFeeCents >= 0 AND EstimatedFeeCents <= GrossCents - DiscountCents AND (ActualFeeCents IS NULL OR (ActualFeeCents >= 0 AND ActualFeeCents <= GrossCents - DiscountCents)) AND (DirectCostCents IS NULL OR DirectCostCents >= 0)"));
        b.Entity<Sale>().HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Sale>().HasOne<PaymentMethod>().WithMany().HasForeignKey(x => x.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Sale>().HasIndex(x => new { x.Date, x.ChannelId, x.PaymentMethodId });
        b.Entity<Expense>().ToTable("Expenses", t => t.HasCheckConstraint("CK_Expense_Amount", "AmountCents > 0"));
        b.Entity<Expense>().HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Expense>().HasOne<PaymentMethod>().WithMany().HasForeignKey(x => x.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Expense>().HasIndex(x => new { x.Date, x.CategoryId });
        b.Entity<CashEntry>().ToTable("CashEntries", t => t.HasCheckConstraint("CK_Cash_Amount", "AmountCents >= 0"));
        b.Entity<CashEntry>().HasIndex(x => x.SaleId).IsUnique();
        b.Entity<CashEntry>().HasIndex(x => x.ExpenseId).IsUnique();
        b.Entity<CashEntry>().HasIndex(x => x.Date);
        b.Entity<CashEntry>().HasOne<Sale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CashEntry>().HasOne<Expense>().WithMany().HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CashEntry>().HasOne<PaymentMethod>().WithMany().HasForeignKey(x => x.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);
        ConfigureRecipes(b);
    }
    partial void ConfigureRecipes(ModelBuilder b);
    public override int SaveChanges()
    {
        foreach (var e in ChangeTracker.Entries<Entity>().Where(e => e.State == EntityState.Modified)) e.Entity.UpdatedAt = DateTime.UtcNow;
        return base.SaveChanges();
    }
}
