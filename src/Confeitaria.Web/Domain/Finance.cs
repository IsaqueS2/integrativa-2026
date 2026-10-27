using System.Text.Json.Serialization;

namespace Confeitaria.Web.Domain;

public enum ExpenseKind { Operating, Supplies, AllocatedCost }
public enum CashKind { Receipt, Payment, Contribution, Withdrawal }
public abstract class Entity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
public class BusinessSettings : Entity
{
    public string Name { get; set; } = "Minha confeitaria";
    public DateOnly OpeningDate { get; set; } = BusinessClock.Today;
    public long OpeningBalanceCents { get; set; }
    public long ReserveCents { get; set; }
}
public class Category : Entity
{
    public string Name { get; set; } = "";
    public ExpenseKind Kind { get; set; }
}
public class Channel : Entity
{
    public string Name { get; set; } = "";
    public int RateBasisPoints { get; set; }
    public long FixedFeeCents { get; set; }
}
public class PaymentMethod : Entity
{
    public string Name { get; set; } = "";
    public int RateBasisPoints { get; set; }
    public long FixedFeeCents { get; set; }
}
public class Sale : Entity
{
    public DateOnly Date { get; set; }
    public DateOnly DueDate { get; set; }
    public int ChannelId { get; set; }
    public int PaymentMethodId { get; set; }
    public long GrossCents { get; set; }
    public long DiscountCents { get; set; }
    public int RateBasisPointsSnapshot { get; set; }
    public long FixedFeeCentsSnapshot { get; set; }
    public long EstimatedFeeCents { get; set; }
    public long? ActualFeeCents { get; set; }
    public long? DirectCostCents { get; set; }
    public int? RecipeId { get; set; }
    public string? CostSnapshotJson { get; set; }
    public string Description { get; set; } = "";
    public bool Consolidated { get; set; }
    public DateOnly? CancelledOn { get; set; }
    public string? CancellationReason { get; set; }
    public DateOnly? ReceivedOn { get; set; }
    [JsonIgnore] public long EffectiveFeeCents => ActualFeeCents ?? EstimatedFeeCents;
    public long NetCents => GrossCents - DiscountCents - EffectiveFeeCents;
    public string Status => CancelledOn != null ? "Cancelled" : ReceivedOn != null ? "Received" : "Pending";
}
public class Expense : Entity
{
    public DateOnly Date { get; set; }
    public DateOnly DueDate { get; set; }
    public int CategoryId { get; set; }
    public ExpenseKind KindSnapshot { get; set; }
    public int? PaymentMethodId { get; set; }
    public long AmountCents { get; set; }
    public string Description { get; set; } = "";
    public DateOnly? PaidOn { get; set; }
    public DateOnly? CancelledOn { get; set; }
    public string? CancellationReason { get; set; }
    public string Status => CancelledOn != null ? "Cancelled" : PaidOn != null ? "Paid" : "Pending";
}
public class CashEntry : Entity
{
    public DateOnly Date { get; set; }
    public CashKind Kind { get; set; }
    public long AmountCents { get; set; }
    public int? SaleId { get; set; }
    public int? ExpenseId { get; set; }
    public int? PaymentMethodId { get; set; }
    public string Description { get; set; } = "";
    public DateOnly? ReversedOn { get; set; }
    public string? ReversalReason { get; set; }
    public long SignedCents => Kind is CashKind.Receipt or CashKind.Contribution ? AmountCents : -AmountCents;
}
public static class BusinessClock
{
    public static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")));
}
