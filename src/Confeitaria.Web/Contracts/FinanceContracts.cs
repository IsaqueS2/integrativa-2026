using Confeitaria.Web.Domain;

namespace Confeitaria.Web.Contracts;

public record SettingsRequest(string Name, DateOnly OpeningDate, long OpeningBalanceCents = 0, long ReserveCents = 0);
public record CategoryRequest(string Name, ExpenseKind Kind);
public record FeeCatalogRequest(string Name, int RateBasisPoints = 0, long FixedFeeCents = 0);
public record SaleRequest(DateOnly Date, long GrossCents, int ChannelId, int PaymentMethodId,
    long DiscountCents = 0, long? DirectCostCents = null, DateOnly? DueDate = null,
    string Description = "", bool Consolidated = false, long? FeeOverrideCents = null,
    int? RecipeId = null, decimal RecipeUnits = 1);
public record ReceiptRequest(DateOnly Date, long? ActualFeeCents = null);
public record ExpenseRequest(DateOnly Date, long AmountCents, int CategoryId, DateOnly? DueDate = null,
    int? PaymentMethodId = null, string Description = "");
public record PaymentRequest(DateOnly Date, int? PaymentMethodId = null);
public record CancelRequest(DateOnly Date, string Reason);
public record OwnerMovementRequest(DateOnly Date, CashKind Kind, long AmountCents, string Description = "", int? PaymentMethodId = null);
public record ReportFilter(DateOnly? From = null, DateOnly? To = null, int? ChannelId = null,
    int? PaymentMethodId = null, int? CategoryId = null);
public record PageResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public record LedgerRow(DateOnly Date, string Kind, int SourceId, long SignedCents, string Description,
    int? ChannelId, int? PaymentMethodId, int? CategoryId, bool IsReversal);
public record ChannelResult(int ChannelId, string Name, long NetSalesCents, long KnownCostsCents,
    int SalesCount, int SalesWithCost, long? EstimatedMarginCents);
public record DashboardTrendPoint(DateOnly PeriodStart, long EstimatedProfitCents, long ExpensesCents);
public record DashboardTrendsResult(bool Monthly, IReadOnlyList<DashboardTrendPoint> Points);
public record DashboardResult(DateOnly From, DateOnly To, long GrossSalesCents, long DiscountsCents,
    long FeesCents, long NetSalesCents, long KnownDirectCostsCents, int SalesCount, int SalesWithCost,
    long? EstimatedOperatingResultCents, long PartialOperatingResultCents, long OperatingExpensesCents,
    long SuppliesPurchasesCents, long AllocatedExpensesCents, long ReceiptsCents, long PaymentsCents,
    long ContributionsCents, long WithdrawalsCents, long AccountsReceivableCents, long AccountsPayableCents,
    long CashBalanceCents, long FilteredNetMovementCents, long ProjectedCashCents, long ReserveCents,
    IReadOnlyList<ChannelResult> Channels, IReadOnlyList<string> Warnings);
