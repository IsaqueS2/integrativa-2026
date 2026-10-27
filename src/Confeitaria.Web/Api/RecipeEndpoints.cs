using Confeitaria.Web.Contracts;
using Confeitaria.Web.Services;

namespace Confeitaria.Web.Api;

public static class RecipeEndpoints
{
    public static void MapRecipes(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        api.MapGet("/ingredients", (RecipeService service, int page = 1, int pageSize = 100) => ReportService.Paginate(service.Ingredients(), page, pageSize));
        api.MapGet("/ingredients/{id:int}", (int id, RecipeService service) => service.Ingredient(id));
        api.MapPost("/ingredients", (IngredientRequest request, RecipeService service) =>
        { var item = service.SaveIngredient(request); return Results.Created($"/api/v1/ingredients/{item.Id}", item); });
        api.MapPut("/ingredients/{id:int}", (int id, IngredientRequest request, RecipeService service) => service.SaveIngredient(request, id));
        api.MapGet("/recipes", (RecipeService service, int page = 1, int pageSize = 100) => ReportService.Paginate(service.Recipes(), page, pageSize));
        api.MapGet("/recipes/{id:int}", (int id, RecipeService service) => service.Recipe(id));
        api.MapPost("/recipes", (RecipeRequest request, RecipeService service) =>
        { var item = service.SaveRecipe(request); return Results.Created($"/api/v1/recipes/{item.Id}", item); });
        api.MapPut("/recipes/{id:int}", (int id, RecipeRequest request, RecipeService service) => service.SaveRecipe(request, id));
        api.MapGet("/recipes/{id:int}/cost", (int id, RecipeService service) => service.Cost(id));
        api.MapPost("/recipes/{id:int}/price", (int id, PricingRequest request, RecipeService service) => service.Price(id, request));
    }
}
