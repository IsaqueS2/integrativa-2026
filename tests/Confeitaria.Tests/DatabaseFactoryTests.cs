using System.Data;
using Confeitaria.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Confeitaria.Tests;

public class DatabaseFactoryTests
{
    [Fact]
    public void Every_context_has_an_exact_design_time_factory()
    {
        var factories = typeof(AppDbContext).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && !type.IsInterface)
            .SelectMany(type => type.GetInterfaces()
                .Where(contract => contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>))
                .Select(contract => (Factory: type, Context: contract.GenericTypeArguments[0])))
            .ToArray();

        Assert.Equal(typeof(AppDbContextFactory),
            Assert.Single(factories, item => item.Context == typeof(AppDbContext)).Factory);
        Assert.Equal(typeof(PostgresAppDbContextFactory),
            Assert.Single(factories, item => item.Context == typeof(PostgresAppDbContext)).Factory);
    }

    [Fact]
    public void Sqlite_factory_uses_supplied_configuration_and_only_Sqlite_migrations_without_opening_database()
    {
        var path = Path.Combine(Path.GetTempPath(), $"confeitaria-factory-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path};Foreign Keys=True;Default Timeout=30";
        using var db = new AppDbContextFactory().CreateDbContext(
            ["--ConnectionStrings:Database", connectionString]);

        Assert.IsType<AppDbContext>(db);
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
        Assert.Equal(connectionString, db.Database.GetConnectionString());
        Assert.Equal(
            ["20261027014748_InitialFinance", "20261027015326_RecipesAndPricing", "20261030143855_PurchaseImportsAndInventory"],
            db.Database.GetMigrations().ToArray());

        var sql = db.GetService<IMigrator>().GenerateScript();
        Assert.Contains("20261030143855_PurchaseImportsAndInventory", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("20261101161028_InitialPostgres", sql, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Postgres_factory_still_uses_only_Postgres_migrations_without_opening_database()
    {
        using var db = new PostgresAppDbContextFactory().CreateDbContext([]);

        Assert.IsType<PostgresAppDbContext>(db);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
        Assert.Equal(["20261101161028_InitialPostgres"], db.Database.GetMigrations().ToArray());

        var sql = db.GetService<IMigrator>().GenerateScript();
        Assert.Contains("20261101161028_InitialPostgres", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("PRAGMA", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("20261030143855_PurchaseImportsAndInventory", sql, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
