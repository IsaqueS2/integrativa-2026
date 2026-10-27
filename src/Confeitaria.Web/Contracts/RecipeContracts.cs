namespace Confeitaria.Web.Contracts;

public record IngredientRequest(string Name, string Unit, decimal PackageQuantity, long PackagePriceCents);
public record IngredientResponse(int Id, string Name, string Unit, decimal PackageQuantity, long PackagePriceCents);
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
