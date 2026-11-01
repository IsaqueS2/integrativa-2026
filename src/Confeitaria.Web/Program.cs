using System.Text.Json.Serialization;
using Confeitaria.Web.Api;
using Confeitaria.Web.Data;
using Confeitaria.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL;

var builder = WebApplication.CreateBuilder(args);
var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("ConnectionStrings:Database must be configured.");
if (string.Equals(databaseProvider, "Postgres", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContext<PostgresAppDbContext>(o =>
        o.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(PostgresAppDbContext).Assembly.FullName)));
    builder.Services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<PostgresAppDbContext>());
}
else if (string.Equals(databaseProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
}
else
{
    throw new InvalidOperationException($"Unsupported database provider '{databaseProvider}'. Use Sqlite or Postgres.");
}
builder.Services.AddScoped<FinanceService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<RecipeService>();
builder.Services.AddScoped<IRecipeCostProvider>(sp => sp.GetRequiredService<RecipeService>());
builder.Services.AddScoped<IRecipeStockProvider>(sp => sp.GetRequiredService<RecipeService>());
builder.Services.AddScoped<PurchaseImportService>();
builder.Services.AddRazorPages();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Swagger resolves JSON contracts through MVC options, even for these minimal APIs.
// Keep its enum representation aligned with the actual HTTP JSON serializer.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));
var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (DomainException ex) { await Problem(ex.StatusCode, ex.Message); }
    catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
    { await Problem(409, "Registro duplicado ou relacionamento inválido. Confira nomes e referências."); }
    catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
    { await Problem(409, "Banco ocupado. Repita a operação com os mesmos dados."); }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.CheckViolation or PostgresErrorCodes.NotNullViolation })
    { await Problem(409, "Registro duplicado ou relacionamento inválido. Confira nomes e referências."); }
    catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
    { await Problem(409, "Banco ocupado. Repita a operação com os mesmos dados."); }
    catch (OverflowException) { await Problem(400, "Os valores excedem o limite numérico permitido."); }
    catch (BadHttpRequestException ex) { await Problem(400, "Requisição inválida: " + ex.Message); }
    async Task Problem(int code, string message)
    {
        context.Response.StatusCode = code;
        await Results.Problem(statusCode: code, title: "Não foi possível concluir a operação", detail: message).ExecuteAsync(context);
    }
});
app.UseStaticFiles();
app.UseCors();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o =>
    {
        o.RoutePrefix = "docs";
        o.SwaggerEndpoint("/swagger/v1/swagger.json", "Confeitaria API v1");
        o.InjectStylesheet("/css/swagger-ui.css");
    });
}
app.MapFinance();
app.MapRecipes();
app.MapPurchaseImports();
app.MapRazorPages();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (string.Equals(databaseProvider, "Postgres", StringComparison.OrdinalIgnoreCase))
    {
        DatabaseMigrationGuard.ValidatePostgres(db);
        app.Logger.LogInformation(
            "Using {ContextType} with {Provider}; PostgreSQL migrations: {Migrations}",
            db.GetType().Name,
            db.Database.ProviderName,
            string.Join(", ", db.Database.GetMigrations()));
    }
    DatabaseSeeder.Initialize(db);
    if (builder.Configuration.GetValue<bool>("SeedDemo") || args.Contains("--seed-demo"))
    {
        DatabaseSeeder.Demo(db, scope.ServiceProvider.GetRequiredService<FinanceService>());
        scope.ServiceProvider.GetRequiredService<RecipeService>().SeedDemo();
    }
}
app.Run();
public partial class Program;
