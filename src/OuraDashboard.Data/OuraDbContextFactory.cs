using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace OuraDashboard.Data;

/// <summary>
/// Used by `dotnet ef` at design time (migrations). Reads connection string from
/// environment variable OURA_CONNECTION_STRING or falls back to a local default.
/// </summary>
public class OuraDbContextFactory : IDesignTimeDbContextFactory<OuraDbContext>
{
    public OuraDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("OURA_CONNECTION_STRING")
            ?? "Host=localhost;Port=5433;Database=oura;Username=oura;Password=changeme";

        var options = new DbContextOptionsBuilder<OuraDbContext>()
            .UseNpgsql(connectionString, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .Options;

        return new OuraDbContext(options);
    }
}
