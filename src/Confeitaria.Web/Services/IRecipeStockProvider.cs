namespace Confeitaria.Web.Services;

public interface IRecipeStockProvider
{
    string? ConsumeForSale(int recipeId, decimal units);
    void RestoreForSale(string? stockConsumptionJson);
}
