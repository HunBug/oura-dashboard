using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace OuraDashboard.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOuraDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextPool<OuraDbContext>(options =>
            options.UseNpgsql(connectionString,
                o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

        return services;
    }
}
