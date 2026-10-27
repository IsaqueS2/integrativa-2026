using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Confeitaria.Web.Contracts;
using Confeitaria.Web.Domain;

namespace Confeitaria.Tests;

public sealed class RecipeApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Kilogram_package_and_gram_usage_produce_proportional_cost()
    {
        using var s = new Scenario();
        var ingredient = await s.Post<IngredientResponse>("ingredients", new IngredientRequest("Chocolate", "kg", 1, 2000));
        Assert.Equal("g", ingredient.Unit);
        Assert.Equal(1000, ingredient.PackageQuantity);
        var recipe = await s.Post<RecipeResponse>("recipes", Recipe(ingredient.Id));
        var cost = await s.Get<RecipeCostResponse>($"recipes/{recipe.Id}/cost");
        Assert.Equal(500, cost.IngredientCostCents);
        Assert.Equal(500, cost.TotalCostCents);
        Assert.Equal(500, cost.UnitCostCents);
        Assert.Equal(500, Assert.Single(cost.Lines).CostCents);
    }

    [Fact]
    public async Task Liter_conversion_and_fractional_quantities_keep_exact_decimal_math()
    {
        using var s = new Scenario();
        var ingredient = await s.Post<IngredientResponse>("ingredients", new IngredientRequest("Leite", "l", 1, 600));
        var recipe = await s.Post<RecipeResponse>("recipes", new RecipeRequest("Creme", 2, "un",
            [new RecipeItemRequest(ingredient.Id, 0.25m, "l")]));
        var cost = await s.Get<RecipeCostResponse>($"recipes/{recipe.Id}/cost");
        Assert.Equal(150, cost.TotalCostCents);
        Assert.Equal(75, cost.UnitCostCents);
        Assert.Equal("ml", Assert.Single(cost.Lines).Unit);
        Assert.Equal(250, Assert.Single(cost.Lines).Quantity);
    }

    [Fact]
    public async Task Cost_breakdown_and_sale_quantity_use_lot_proportions_without_rounding_unit_first()
    {
        using var s = new Scenario();
        await s.InitializeFinance();
        var ingredient = await s.Post<IngredientResponse>("ingredients", new IngredientRequest("Chocolate", "kg", 1, 2000));
        var recipe = await s.Post<RecipeResponse>("recipes", Recipe(ingredient.Id) with
        { YieldQuantity = 20, PackagingCents = 200, LaborMinutes = 30, HourlyRateCents = 1200, LossBasisPoints = 500, AllocatedCents = 100 });
        var cost = await s.Get<RecipeCostResponse>($"recipes/{recipe.Id}/cost");
        Assert.Equal(500, cost.IngredientCostCents);
        Assert.Equal(25, cost.LossCents);
        Assert.Equal(600, cost.LaborCents);
        Assert.Equal(1425, cost.TotalCostCents);
        Assert.Equal(71, cost.UnitCostCents);
        var sale = await s.Sale(recipe.Id, 3);
        Assert.Equal(214, sale.DirectCostCents); // 1425 / 20 * 3 = 213.75, not 71 * 3.
        Assert.Contains("\"formulaVersion\":1", sale.CostSnapshotJson);
        Assert.Contains("\"soldUnits\":3", sale.CostSnapshotJson);
    }

    [Fact]
    public async Task Margin_is_percentage_of_price_and_fixed_fees_are_in_numerator()
    {
        using var s = new Scenario();
        var ingredient = await s.Post<IngredientResponse>("ingredients", new IngredientRequest("Base", "g", 250, 1000));
        var recipe = await s.Post<RecipeResponse>("recipes", Recipe(ingredient.Id));
        var price = await s.Post<PricingResponse>($"recipes/{recipe.Id}/price", new PricingRequest(5000));
        Assert.Equal(2000, price.SuggestedPriceCents);
        Assert.Equal(10000, price.MarkupBasisPoints); // 100% markup = 50% price margin.
        var withFee = await s.Post<PricingResponse>($"recipes/{recipe.Id}/price", new PricingRequest(5000, 1000, 100));
        Assert.Equal(2750, withFee.SuggestedPriceCents); // (1000 + 100) / (1 - .5 - .1)
    }

    [Theory]
    [InlineData(10000, 0)]
    [InlineData(5000, 5000)]
    [InlineData(-1, 0)]
    [InlineData(0, 10000)]
    public async Task Impossible_pricing_percentages_are_rejected(int margin, int rate)
    {
        using var s = new Scenario();
        var ingredient = await s.Post<IngredientResponse>("ingredients", new IngredientRequest("Base", "g", 250, 1000));
        var recipe = await s.Post<RecipeResponse>("recipes", Recipe(ingredient.Id));
        await s.Rejected($"recipes/{recipe.Id}/price", new PricingRequest(margin, rate));
    }

    [Fact]
    public async Task Invalid_dimensions_zero_yield_and_unrepresentable_precision_are_rejected()
    {
        using var s = new Scenario();
        var ingredient = await s.Post<IngredientResponse>("ingredients", new IngredientRequest("Base", "g", 250, 1000));
        await s.Rejected("recipes", Recipe(ingredient.Id) with { Items = [new RecipeItemRequest(ingredient.Id, 250, "ml")] });
        await s.Rejected("recipes", Recipe(ingredient.Id) with { YieldQuantity = 0 });
        await s.Rejected("ingredients", new IngredientRequest("Invalido", "g", 0.0000001m, 1));
        await s.Rejected("ingredients", new IngredientRequest("Invalido", "colher", 1, 1));
        Assert.Empty((await s.Get<PageResult<RecipeResponse>>("recipes")).Items);
    }

    [Fact]
    public async Task Ingredient_price_updates_recalculate_new_queries_but_preserve_sale_snapshot()
    {
        using var s = new Scenario();
        await s.InitializeFinance();
        var ingredient = await s.Post<IngredientResponse>("ingredients", new IngredientRequest("Chocolate", "kg", 1, 2000));
        var recipe = await s.Post<RecipeResponse>("recipes", Recipe(ingredient.Id));
        var first = await s.Sale(recipe.Id);
        await s.Put<IngredientResponse>($"ingredients/{ingredient.Id}", new IngredientRequest("Chocolate", "kg", 1, 4000));
        var newer = await s.Sale(recipe.Id);
        var old = await s.Get<Sale>($"sales/{first.Id}");
        Assert.Equal(500, old.DirectCostCents);
        Assert.Equal(first.CostSnapshotJson, old.CostSnapshotJson);
        Assert.Equal(1000, newer.DirectCostCents);
        Assert.Equal(1000, (await s.Get<RecipeCostResponse>($"recipes/{recipe.Id}/cost")).TotalCostCents);
    }

    [Fact]
    public async Task Used_ingredient_dimension_cannot_be_reinterpreted_and_recipe_edits_replace_items()
    {
        using var s = new Scenario();
        var ingredient = await s.Post<IngredientResponse>("ingredients", new IngredientRequest("Base", "g", 250, 1000));
        var recipe = await s.Post<RecipeResponse>("recipes", Recipe(ingredient.Id));
        using var invalid = await s.Client.PutAsJsonAsync($"/api/v1/ingredients/{ingredient.Id}", new IngredientRequest("Base", "ml", 250, 1000), Json);
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        var edited = await s.Put<RecipeResponse>($"recipes/{recipe.Id}", Recipe(ingredient.Id) with
        { Items = [new RecipeItemRequest(ingredient.Id, 125, "g")], YieldQuantity = 2 });
        Assert.Single(edited.Items);
        Assert.Equal(125, edited.Items[0].Quantity);
        Assert.Equal(500, (await s.Get<RecipeCostResponse>($"recipes/{recipe.Id}/cost")).TotalCostCents);
    }

    [Fact]
    public async Task Manual_cost_and_recipe_cannot_be_combined_and_allocated_expense_is_not_double_counted()
    {
        using var s = new Scenario();
        await s.InitializeFinance();
        var ingredient = await s.Post<IngredientResponse>("ingredients", new IngredientRequest("Base", "g", 250, 1000));
        var recipe = await s.Post<RecipeResponse>("recipes", Recipe(ingredient.Id) with { AllocatedCents = 200 });
        await s.Rejected("sales", new SaleRequest(new(2026, 1, 10), 5000, s.ChannelId, s.PaymentId, DirectCostCents: 1000, RecipeId: recipe.Id));
        await s.Sale(recipe.Id);
        var category = await s.Post<Category>("categories", new CategoryRequest("Rateio já no custo", ExpenseKind.AllocatedCost));
        await s.Post<Expense>("expenses", new ExpenseRequest(new(2026, 1, 10), 200, category.Id));
        var report = await s.Get<DashboardResult>("dashboard?from=2026-01-01&to=2026-01-31");
        Assert.Equal(1200, report.KnownDirectCostsCents);
        Assert.Equal(3800, report.EstimatedOperatingResultCents);
        Assert.Equal(200, report.AllocatedExpensesCents);
        Assert.Equal(0, report.OperatingExpensesCents);
    }

    [Fact]
    public async Task Fractional_ingredient_lines_are_summed_before_rounding_and_price_rounds_up()
    {
        using var s = new Scenario();
        var a = await s.Post<IngredientResponse>("ingredients", new IngredientRequest("Base A", "g", 3, 1));
        var b = await s.Post<IngredientResponse>("ingredients", new IngredientRequest("Base B", "g", 3, 1));
        var recipe = await s.Post<RecipeResponse>("recipes", new RecipeRequest("Fração", 3, "un",
            [new(a.Id, 1, "g"), new(b.Id, 1, "g")]));
        var cost = await s.Get<RecipeCostResponse>($"recipes/{recipe.Id}/cost");
        Assert.Equal(1, cost.IngredientCostCents); // 1/3 + 1/3 rounds to 1 cent.
        Assert.Equal(0, cost.Lines.Sum(x => x.CostCents));
        var price = await s.Post<PricingResponse>($"recipes/{recipe.Id}/price", new PricingRequest(0));
        Assert.Equal(1, price.SuggestedPriceCents); // positive cost cannot round down to free.
    }

    private static RecipeRequest Recipe(int ingredientId) => new("Receita teste", 1, "un", [new(ingredientId, 250, "g")]);

    private sealed class Scenario : IDisposable
    {
        private readonly TestAppFactory _factory = new();
        public HttpClient Client { get; }
        public int ChannelId { get; private set; }
        public int PaymentId { get; private set; }
        public Scenario() => Client = _factory.CreateClient(new() { BaseAddress = new Uri("http://localhost") });
        public async Task InitializeFinance()
        {
            await Put<BusinessSettings>("settings", new SettingsRequest("Receitas teste", new(2026, 1, 1), 10000));
            ChannelId = (await Post<Channel>("channels", new FeeCatalogRequest("Venda teste"))).Id;
            PaymentId = (await Post<PaymentMethod>("payment-methods", new FeeCatalogRequest("Dinheiro teste"))).Id;
        }
        public Task<Sale> Sale(int recipeId, decimal units = 1) => Post<Sale>("sales", new SaleRequest(new(2026, 1, 10), 5000, ChannelId, PaymentId, RecipeId: recipeId, RecipeUnits: units));
        public async Task<T> Get<T>(string path) => await Read<T>(await Client.GetAsync("/api/v1/" + path));
        public async Task<T> Post<T>(string path, object request) => await Read<T>(await Client.PostAsJsonAsync("/api/v1/" + path, request, Json));
        public async Task<T> Put<T>(string path, object request) => await Read<T>(await Client.PutAsJsonAsync("/api/v1/" + path, request, Json));
        public async Task Rejected(string path, object request)
        {
            using var response = await Client.PostAsJsonAsync("/api/v1/" + path, request, Json);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        private static async Task<T> Read<T>(HttpResponseMessage response)
        {
            using (response)
            {
                var text = await response.Content.ReadAsStringAsync();
                Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
                return JsonSerializer.Deserialize<T>(text, Json)!;
            }
        }
        public void Dispose() { Client.Dispose(); _factory.Dispose(); }
    }
}
