using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace YtpdWeb.Api.Data;

// Used only by `dotnet ef migrations add` at design time, to generate
// Postgres-targeted migrations for the hosted deployment. The connection
// string here never needs to actually connect - scaffolding just needs to
// know which provider's SQL dialect to generate. Desktop's SQLite path
// intentionally has no migrations of its own; it still uses
// Database.EnsureCreated() at runtime (see Program.cs), since that's
// simpler and already proven for a SQLite file that doesn't pre-exist.
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        builder.UseNpgsql("Host=localhost;Database=design_time_only;Username=x;Password=x");
        return new AppDbContext(builder.Options);
    }
}
