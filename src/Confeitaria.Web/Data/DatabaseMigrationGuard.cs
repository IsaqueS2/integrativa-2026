using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Confeitaria.Web.Data;

public static class DatabaseMigrationGuard
{
    public static void ValidatePostgres(AppDbContext db)
    {
        if (db.GetType() != typeof(PostgresAppDbContext)
            || db.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            throw new InvalidOperationException(
                "PostgreSQL requires PostgresAppDbContext with the Npgsql EF Core provider.");
        }

        var sqliteMigrationIds = typeof(AppDbContext).Assembly
            .GetTypes()
            .Where(type => typeof(Migration).IsAssignableFrom(type))
            .Where(type => type.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(AppDbContext))
            .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Id)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var selectedSqliteMigrations = db.Database.GetMigrations()
            .Where(sqliteMigrationIds.Contains)
            .ToArray();
        if (selectedSqliteMigrations.Length > 0)
        {
            throw new InvalidOperationException(
                $"PostgreSQL selected SQLite migrations: {string.Join(", ", selectedSqliteMigrations)}.");
        }

        var appliedSqliteMigrations = db.Database.GetAppliedMigrations()
            .Where(sqliteMigrationIds.Contains)
            .ToArray();
        if (appliedSqliteMigrations.Length > 0)
        {
            throw new InvalidOperationException(
                $"The PostgreSQL database has SQLite migration history ({string.Join(", ", appliedSqliteMigrations)}). " +
                "Automatic migrations were stopped to avoid changing a partially migrated database.");
        }
    }
}
