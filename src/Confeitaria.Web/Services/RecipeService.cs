using System.Text.Json;
using Confeitaria.Web.Contracts;
using Confeitaria.Web.Data;
using Confeitaria.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Confeitaria.Web.Services;

public class RecipeService(AppDbContext db) : IRecipeCostProvider
{
    public const long QuantityScale = 1_000_000;
    private const long MaxQuantityMicros = 1_000_000_000_000;
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<IngredientResponse> Ingredients() => db.Ingredients.AsNoTracking().OrderBy(x => x.Name).ToList().Select(ToResponse).ToList();
    public IngredientResponse Ingredient(int id) => ToResponse(db.Ingredients.AsNoTracking().SingleOrDefault(x => x.Id == id)
        ?? throw new DomainException("Ingrediente não encontrado.", 404));

    public IngredientResponse SaveIngredient(IngredientRequest request, int? id = null)
    {
        var name = Rules.Name(request.Name);
        Rules.Money(request.PackagePriceCents, allowZero: true);
        var (dimension, micros) = ConvertQuantity(request.PackageQuantity, request.Unit);
        Rules.Require(!db.Ingredients.Any(x => x.Name == name && x.Id != (id ?? 0)), "Já existe um ingrediente com esse nome.", 409);
        var item = id.HasValue ? db.Ingredients.SingleOrDefault(x => x.Id == id.Value)
            ?? throw new DomainException("Ingrediente não encontrado.", 404) : new Ingredient();
        Rules.Require(!id.HasValue || item.Dimension == dimension || !db.RecipeItems.Any(x => x.IngredientId == id.Value),
            "A dimensão de um ingrediente utilizado em receitas não pode ser alterada. Cadastre um novo ingrediente.", 409);
        item.Name = name;
        item.Dimension = dimension;
        item.PackageQuantityMicros = micros;
        item.PackagePriceCents = request.PackagePriceCents;
        if (!id.HasValue) db.Ingredients.Add(item);
        db.SaveChanges();
        return ToResponse(item);
    }

    public IReadOnlyList<RecipeResponse> Recipes() => db.Recipes.AsNoTracking().Include(x => x.Items).OrderBy(x => x.Name).ToList().Select(ToResponse).ToList();
    public RecipeResponse Recipe(int id) => ToResponse(FindRecipe(id, tracking: false));

    public RecipeResponse SaveRecipe(RecipeRequest request, int? id = null)
    {
        var name = Rules.Name(request.Name);
        Rules.Require(!db.Recipes.Any(x => x.Name == name && x.Id != (id ?? 0)), "Já existe uma receita com esse nome.", 409);
        var yieldMicros = Scaled(request.YieldQuantity, false, "O rendimento");
        var outputUnit = request.YieldUnit?.Trim().ToLowerInvariant();
        Rules.Require(outputUnit is "un" or "fatia" or "porcao" or "g" or "kg" or "ml" or "l", "Unidade de rendimento inválida. Use un, fatia, porcao, g, kg, ml ou l.");
        Rules.Money(request.PackagingCents, true);
        Rules.Money(request.HourlyRateCents, true);
        Rules.Money(request.AllocatedCents, true);
        var laborMicros = Scaled(request.LaborMinutes, true, "O tempo de trabalho");
        Rules.Require(request.LossBasisPoints is >= 0 and <= 10000, "A perda estimada deve estar entre 0 e 10000 pontos-base.");
        Rules.Require(request.Items is { Count: > 0 and <= 100 }, "Informe de 1 a 100 ingredientes na receita.");
        Rules.Require(request.Items!.All(x => x is not null), "Item de receita inválido.");
        Rules.Require(request.Items.Select(x => x.IngredientId).Distinct().Count() == request.Items.Count, "Agrupe o mesmo ingrediente em um único item.");
        var ingredientIds = request.Items.Select(x => x.IngredientId).ToList();
        var ingredients = db.Ingredients.AsNoTracking().Where(x => ingredientIds.Contains(x.Id)).ToDictionary(x => x.Id);
        var items = new List<RecipeItem>();
        foreach (var input in request.Items)
        {
            Rules.Require(ingredients.TryGetValue(input.IngredientId, out var ingredient), "Um ingrediente da receita não existe.");
            var (dimension, quantity) = ConvertQuantity(input.Quantity, input.Unit);
            Rules.Require(dimension == ingredient!.Dimension, "Não é possível converter massa, volume e unidades entre si. Use a mesma dimensão do ingrediente.");
            items.Add(new RecipeItem { IngredientId = input.IngredientId, QuantityMicros = quantity });
        }
        using var tx = db.Database.BeginTransaction();
        var recipe = id.HasValue ? FindRecipe(id.Value, tracking: true) : new Recipe();
        recipe.Name = name;
        recipe.YieldQuantityMicros = yieldMicros;
        recipe.YieldUnit = outputUnit!;
        recipe.PackagingCents = request.PackagingCents;
        recipe.LaborMinutesMicros = laborMicros;
        recipe.HourlyRateCents = request.HourlyRateCents;
        recipe.LossBasisPoints = request.LossBasisPoints;
        recipe.AllocatedCents = request.AllocatedCents;
        if (id.HasValue)
        {
            db.RecipeItems.RemoveRange(recipe.Items);
            // Remove old unique (recipe, ingredient) keys before adding the new items.
            recipe.Items.Clear();
            db.SaveChanges();
        }
        else db.Recipes.Add(recipe);
        recipe.Items.AddRange(items);
        db.SaveChanges();
        // Reject recipes with unusable costs immediately and roll back as one operation.
        _ = Cost(recipe);
        tx.Commit();
        return ToResponse(recipe);
    }

    public RecipeCostResponse Cost(int id) => Cost(FindRecipe(id, tracking: false));

    public PricingResponse Price(int id, PricingRequest request)
    {
        Rules.Require(request.MarginBasisPoints is >= 0 and < 10000 && request.SalesRateBasisPoints is >= 0 and < 10000
            && (long)request.MarginBasisPoints + request.SalesRateBasisPoints < 10000,
            "A soma da margem e dos percentuais sobre a venda deve ser menor que 100%.");
        Rules.Money(request.FixedFeeCents, true);
        var cost = Cost(id);
        var exactUnitCost = cost.TotalCostCents / cost.YieldQuantity;
        var priceDecimal = (exactUnitCost + request.FixedFeeCents) / (1m - (request.MarginBasisPoints + request.SalesRateBasisPoints) / 10000m);
        Rules.Require(priceDecimal <= Rules.MaxMoney, "O preço calculado ultrapassa o limite permitido.");
        var price = checked((long)decimal.Ceiling(priceDecimal));
        var markupDecimal = exactUnitCost == 0 ? (decimal?)null : (price / exactUnitCost - 1m) * 10000m;
        long? markup = markupDecimal is null || markupDecimal > long.MaxValue ? null : Rules.Round(markupDecimal.Value);
        return new PricingResponse(id, cost.YieldUnit, cost.UnitCostCents, request.MarginBasisPoints,
            request.SalesRateBasisPoints, request.FixedFeeCents, price, markup);
    }

    public RecipeSaleCost GetSaleCost(int recipeId, decimal units)
    {
        _ = Scaled(units, false, "A quantidade vendida");
        var recipe = FindRecipe(recipeId, tracking: false);
        var cost = Cost(recipe);
        var total = RoundMoney(cost.TotalCostCents * units / cost.YieldQuantity);
        var snapshot = JsonSerializer.Serialize(new
        {
            formulaVersion = 1, recipeId, recipeName = recipe.Name, recipeUpdatedAt = recipe.UpdatedAt,
            calculatedAtUtc = DateTime.UtcNow, soldUnits = units, yieldUnit = recipe.YieldUnit,
            totalCostCents = total, lotCost = cost,
            policy = "Custo total de precificação. Despesas já rateadas na receita devem ser classificadas como AllocatedCost para não serem descontadas duas vezes."
        }, SnapshotOptions);
        return new RecipeSaleCost(total, snapshot);
    }

    public void SeedDemo()
    {
        if (db.Recipes.Any(x => x.Name == "Brigadeiro demonstrativo")) return;
        var ingredient = db.Ingredients.AsNoTracking().SingleOrDefault(x => x.Name == "Chocolate fictício");
        var ingredientId = ingredient?.Id ?? SaveIngredient(new IngredientRequest("Chocolate fictício", "kg", 1, 2000)).Id;
        SaveRecipe(new RecipeRequest("Brigadeiro demonstrativo", 20, "un",
            [new RecipeItemRequest(ingredientId, 250, "g")], PackagingCents: 200, LaborMinutes: 30,
            HourlyRateCents: 1200, LossBasisPoints: 500));
    }

    private Recipe FindRecipe(int id, bool tracking)
    {
        IQueryable<Recipe> query = db.Recipes.Include(x => x.Items);
        if (!tracking) query = query.AsNoTracking();
        return query.SingleOrDefault(x => x.Id == id) ?? throw new DomainException("Receita não encontrada.", 404);
    }

    private RecipeCostResponse Cost(Recipe recipe)
    {
        var ids = recipe.Items.Select(x => x.IngredientId).ToArray();
        var ingredients = db.Ingredients.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionary(x => x.Id);
        var lines = new List<IngredientCostLine>();
        decimal ingredientsExact = 0;
        foreach (var item in recipe.Items.OrderBy(x => x.Id))
        {
            var ingredient = ingredients[item.IngredientId];
            var exact = (decimal)ingredient.PackagePriceCents * item.QuantityMicros / ingredient.PackageQuantityMicros;
            ingredientsExact += exact;
            lines.Add(new IngredientCostLine(ingredient.Id, ingredient.Name, item.QuantityMicros / (decimal)QuantityScale,
                Unit(ingredient.Dimension), ingredient.PackageQuantityMicros / (decimal)QuantityScale,
                ingredient.PackagePriceCents, RoundMoney(exact)));
        }
        var ingredientCost = RoundMoney(ingredientsExact);
        var loss = RoundMoney(ingredientsExact * recipe.LossBasisPoints / 10000m);
        var labor = RoundMoney(recipe.LaborMinutesMicros / (decimal)QuantityScale * recipe.HourlyRateCents / 60m);
        var total = RoundMoney((decimal)ingredientCost + recipe.PackagingCents + loss + labor + recipe.AllocatedCents);
        var yield = recipe.YieldQuantityMicros / (decimal)QuantityScale;
        return new RecipeCostResponse(recipe.Id, recipe.Name, yield, recipe.YieldUnit, ingredientCost,
            recipe.PackagingCents, loss, labor, recipe.AllocatedCents, total, RoundMoney(total / yield), lines,
            "Ingredientes são somados antes de arredondar o subtotal; as linhas exibidas podem diferir dele em centavos. O custo unitário exibido é arredondado; vendas e preço usam a proporção exata do lote. Perda estimada é um acréscimo sobre o custo de ingredientes, sem gerar saída de caixa.");
    }

    private RecipeResponse ToResponse(Recipe recipe)
    {
        var dimensions = db.Ingredients.AsNoTracking().Where(x => recipe.Items.Select(i => i.IngredientId).Contains(x.Id)).ToDictionary(x => x.Id, x => x.Dimension);
        return new RecipeResponse(recipe.Id, recipe.Name, recipe.YieldQuantityMicros / (decimal)QuantityScale,
            recipe.YieldUnit, recipe.Items.Select(x => new RecipeItemRequest(x.IngredientId, x.QuantityMicros / (decimal)QuantityScale,
                Unit(dimensions[x.IngredientId]))).ToList(), recipe.PackagingCents,
            recipe.LaborMinutesMicros / (decimal)QuantityScale, recipe.HourlyRateCents, recipe.LossBasisPoints, recipe.AllocatedCents);
    }

    private static IngredientResponse ToResponse(Ingredient ingredient) => new(ingredient.Id, ingredient.Name,
        Unit(ingredient.Dimension), ingredient.PackageQuantityMicros / (decimal)QuantityScale, ingredient.PackagePriceCents);

    public static (IngredientDimension Dimension, long Micros) ConvertQuantity(decimal quantity, string? unit)
    {
        var normalized = unit?.Trim().ToLowerInvariant();
        var (dimension, multiplier) = normalized switch
        {
            "g" => (IngredientDimension.Mass, 1m), "kg" => (IngredientDimension.Mass, 1000m),
            "ml" => (IngredientDimension.Volume, 1m), "l" => (IngredientDimension.Volume, 1000m),
            "un" => (IngredientDimension.Count, 1m),
            _ => throw new DomainException("Unidade inválida. Use g, kg, ml, l ou un.")
        };
        Rules.Require(quantity > 0 && quantity <= 1_000_000m, "A quantidade deve ser positiva e estar no intervalo permitido.");
        return (dimension, Scaled(quantity * multiplier, false, "A quantidade"));
    }

    private static long Scaled(decimal quantity, bool allowZero, string label)
    {
        Rules.Require(quantity >= (allowZero ? 0m : 0.000001m) && quantity <= MaxQuantityMicros / (decimal)QuantityScale,
            $"{label} deve ser {(allowZero ? "não negativa" : "positiva")} e não ultrapassar 1.000.000 unidades-base.");
        var scaled = quantity * QuantityScale;
        Rules.Require(scaled == decimal.Truncate(scaled), $"{label} aceita no máximo 6 casas decimais na unidade-base.");
        return checked((long)scaled);
    }

    private static string Unit(IngredientDimension dimension) => dimension switch
    { IngredientDimension.Mass => "g", IngredientDimension.Volume => "ml", _ => "un" };

    private static long RoundMoney(decimal cents)
    {
        Rules.Require(cents >= 0 && cents <= Rules.MaxMoney, "O custo calculado ultrapassa o intervalo permitido.");
        return Rules.Round(cents);
    }
}
