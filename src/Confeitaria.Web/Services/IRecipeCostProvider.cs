namespace Confeitaria.Web.Services;

public record RecipeSaleCost(long TotalCostCents, string SnapshotJson);
public interface IRecipeCostProvider
{
    RecipeSaleCost GetSaleCost(int recipeId, decimal units);
}
