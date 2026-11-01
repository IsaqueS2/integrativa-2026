using Confeitaria.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Confeitaria.Tests;

public class PostgresMigrationTests
{
    [Fact]
    public void Postgres_context_exposes_only_Postgres_migrations_and_SQL()
    {
        var options = new DbContextOptionsBuilder<PostgresAppDbContext>()
            .UseNpgsql("Host=localhost;Database=confeitaria;Username=postgres;Password=test")
            .Options;
        using var db = new PostgresAppDbContext(options);

        var migrations = db.Database.GetMigrations().ToArray();
        var migrationSql = db.GetService<IMigrator>().GenerateScript();

        Assert.Equal(["20261101161028_InitialPostgres"], migrations);
        Assert.DoesNotContain("PRAGMA", migrationSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("20261027014748", migrationSql, StringComparison.Ordinal);
        Assert.DoesNotContain("20261027015326", migrationSql, StringComparison.Ordinal);
        Assert.DoesNotContain("20261030143855", migrationSql, StringComparison.Ordinal);
    }
}
