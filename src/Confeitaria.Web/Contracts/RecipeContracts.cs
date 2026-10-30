namespace Confeitaria.Web.Contracts;

public record IngredientRequest(string Name, string Unit, decimal PackageQuantity, long PackagePriceCents);
public record IngredientResponse(int Id, string Name, string Unit, decimal PackageQuantity, long PackagePriceCents, decimal? StockQuantity);
public record RecipeItemRequest(int IngredientId, decimal Quantity, string Unit);
public record RecipeRequest(string Name, decimal YieldQuantity, string YieldUnit, List<RecipeItemRequest> Items,
    long PackagingCents = 0, decimal LaborMinutes = 0, long HourlyRateCents = 0,
    int LossBasisPoints = 0, long AllocatedCents = 0);
public record RecipeResponse(int Id, string Name, decimal YieldQuantity, string YieldUnit,
    IReadOnlyList<RecipeItemRequest> Items, long PackagingCents, decimal LaborMinutes,
    long HourlyRateCents, int LossBasisPoints, long AllocatedCents);
public record IngredientCostLine(int IngredientId, string Name, decimal Quantity, string Unit,
    decimal PackageQuantity, long PackagePriceCents, long CostCents);
public record RecipeCostResponse(int RecipeId, string Name, decimal YieldQuantity, string YieldUnit,
    long IngredientCostCents, long PackagingCents, long LossCents, long LaborCents, long AllocatedCents,
    long TotalCostCents, long UnitCostCents, IReadOnlyList<IngredientCostLine> Lines, string RoundingNote);
public record PricingRequest(int MarginBasisPoints, int SalesRateBasisPoints = 0, long FixedFeeCents = 0);
public record PricingResponse(int RecipeId, string YieldUnit, long UnitCostCents, int MarginBasisPoints,
    int SalesRateBasisPoints, long FixedFeeCents, long SuggestedPriceCents, long? MarkupBasisPoints);
public record PurchaseImportLinePreview(string Name, decimal Quantity, string Unit, long TotalCents, int? IngredientId);
public record PurchaseImportPreview(string DocumentHash, string FileName, string Format, string? AccessKey,
    string? InvoiceNumber, string Supplier, DateOnly PurchaseDate, long TotalCents,
    IReadOnlyList<PurchaseImportLinePreview> Items, bool AlreadyImported);
public record PurchaseImportLineRequest(string Name, decimal Quantity, string Unit, long TotalCents, int? IngredientId);
public record PurchaseImportCommitRequest(string DocumentHash, string? AccessKey, string? InvoiceNumber,
    string Supplier, DateOnly PurchaseDate, long TotalCents, List<PurchaseImportLineRequest> Items);
public record PurchaseImportResponse(int Id, int ExpenseId, long TotalCents, int ItemCount);
