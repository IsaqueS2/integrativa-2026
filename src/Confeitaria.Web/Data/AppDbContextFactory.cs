using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Confeitaria.Web.Data;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Register the exact SQLite context so EF does not use the covariant
        // PostgreSQL factory for its AppDbContext base class.
        var configuration = WebApplication.CreateBuilder(args).Configuration;
        var connectionString = configuration.GetConnectionString("Database")
            ?? "Data Source=confeitaria.db;Foreign Keys=True;Default Timeout=30";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
