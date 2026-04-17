using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OuraDashboard.Data;
using OuraDashboard.Sync;

// ── Args ──────────────────────────────────────────────────────────────────────
int days = 30;
bool applyMigrations = false;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--days" && i + 1 < args.Length && int.TryParse(args[i + 1], out var d))
        days = d;
    if (args[i] == "--migrate")
        applyMigrations = true;
}

// ── Host ──────────────────────────────────────────────────────────────────────
var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(config => config
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile("appsettings.Local.json", optional: true)
        .AddEnvironmentVariables())
    .ConfigureServices((ctx, services) =>
    {
        var connectionString = ctx.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required");

        services.Configure<OuraOptions>(ctx.Configuration.GetSection(OuraOptions.SectionName));
        services.AddOuraDatabase(connectionString);
        services.AddOuraSync();

        services.AddLogging(b => b.AddConsole());
    })
    .Build();

// ── Migrate ───────────────────────────────────────────────────────────────────
if (applyMigrations)
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OuraDbContext>();
    await db.Database.MigrateAsync();
    Console.WriteLine("Migrations applied.");
}

// ── Sync ──────────────────────────────────────────────────────────────────────
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var options = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<OuraOptions>>().Value;

if (options.Users.Count == 0)
{
    logger.LogError("No users configured. Check Oura:Users in appsettings.Local.json");
    return 1;
}

logger.LogInformation("Starting sync for {Count} user(s), {Days} day(s) back", options.Users.Count, days);

int exitCode = 0;
using var syncScope = host.Services.CreateScope();
var syncService = syncScope.ServiceProvider.GetRequiredService<OuraSyncService>();

foreach (var user in options.Users)
{
    var result = await syncService.SyncUserAsync(user.Name, days);

    Console.WriteLine(
        $"[{user.Name}] sleep={result.DailySleepCount} sessions={result.SleepSessionCount} " +
        $"readiness={result.ReadinessCount} hr={result.HeartRateSampleCount} " +
        $"stress={result.DailyStressCount} hrv={result.DailyHrvCount} " +
        $"activity={result.DailyActivityCount} vo2={result.Vo2MaxCount}");

    if (result.Errors.Count > 0)
    {
        foreach (var err in result.Errors)
            logger.LogWarning("  {Error}", err);
        exitCode = 1;
    }
}

return exitCode;

