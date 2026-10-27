namespace Confeitaria.Web.Domain;

public enum IngredientDimension { Mass, Volume, Count }

public class Ingredient : Entity
{
    public string Name { get; set; } = "";
    public IngredientDimension Dimension { get; set; }
    // One canonical g, mL or unit is represented by 1,000,000 micros.
    public long PackageQuantityMicros { get; set; }
    public long PackagePriceCents { get; set; }
}

public class Recipe : Entity
{
    public string Name { get; set; } = "";
    public long YieldQuantityMicros { get; set; }
    public string YieldUnit { get; set; } = "un";
    public long PackagingCents { get; set; }
    public long LaborMinutesMicros { get; set; }
    public long HourlyRateCents { get; set; }
    public int LossBasisPoints { get; set; }
    public long AllocatedCents { get; set; }
    public List<RecipeItem> Items { get; set; } = [];
}

public class RecipeItem : Entity
{
    public int RecipeId { get; set; }
    public int IngredientId { get; set; }
    public long QuantityMicros { get; set; }
}
