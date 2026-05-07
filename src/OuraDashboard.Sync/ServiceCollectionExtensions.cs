using Microsoft.Extensions.DependencyInjection;

namespace OuraDashboard.Sync;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IHttpClientFactory, OuraSyncService, and
    /// optionally the background service + ISyncTrigger (for the web host).
    /// Call services.Configure&lt;OuraOptions&gt;() and AddOuraDatabase() separately.
    /// </summary>
    public static IServiceCollection AddOuraSync(
        this IServiceCollection services,
        bool addBackgroundService = false)
    {
        // Named HttpClient with sensible defaults; token is set per-request in OuraSyncService
        services.AddHttpClient("OuraApi", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        services.AddHttpClient("OpenMeteoApi", client =>
        {
            client.BaseAddress = new Uri("https://archive-api.open-meteo.com/");
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        services.AddHttpClient("EstonianEnvironmentAgencyApi", client =>
        {
            client.BaseAddress = new Uri("https://keskkonnaandmed.envir.ee/");
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // OuraSyncService is scoped — one per operation
        services.AddScoped<OuraSyncService>();
        services.AddScoped<WeatherSyncService>();

        if (addBackgroundService)
        {
            services.AddSingleton<SyncBackgroundService>();
            services.AddSingleton<ISyncTrigger>(sp => sp.GetRequiredService<SyncBackgroundService>());
            services.AddHostedService(sp => sp.GetRequiredService<SyncBackgroundService>());
        }

        return services;
    }
}
