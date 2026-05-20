using ApexCharts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OuraDashboard.Data;
using OuraDashboard.Sync;
using OuraDashboard.Web.Components;
using OuraDashboard.Web.Services;
using OuraDashboard.Web.Services.Llm;

var builder = WebApplication.CreateBuilder(args);

// Register the in-memory log sink early so it captures every warning/error
// from the moment the app starts — including config validation below.
var logSink = new AppLogSink();
builder.Logging.AddProvider(logSink);
builder.Services.AddSingleton(logSink);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<OuraOptions>(builder.Configuration.GetSection(OuraOptions.SectionName));
builder.Services.Configure<WeatherOptions>(builder.Configuration.GetSection(WeatherOptions.SectionName));
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.AddOuraDatabase(builder.Configuration.GetConnectionString("Default")!);
builder.Services.AddOuraSync(addBackgroundService: true);
builder.Services.AddApexCharts();
builder.Services.AddScoped<OuraDashboard.Web.Services.DashboardQueryService>();
builder.Services.AddScoped<OuraDashboard.Web.Services.DebugInvestigationService>();
builder.Services.AddSingleton<LlmConcurrencyLimiter>();
builder.Services.AddSingleton<LlmDebugLog>();
builder.Services.AddScoped<LlmRequestStore>();
builder.Services.AddScoped<NightLlmService>();
builder.Services.AddHttpClient<ILlmClient, OllamaLlmClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
    http.BaseAddress = Uri.TryCreate(opts.BaseUrl, UriKind.Absolute, out var uri)
        ? uri
        : new Uri("http://neolinux:11434");
    http.Timeout = Timeout.InfiniteTimeSpan;
}).ConfigurePrimaryHttpMessageHandler(sp =>
{
    var opts = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
    return new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(Math.Max(1, opts.ConnectTimeoutSeconds))
    };
});

var app = builder.Build();

// Apply EF Core migrations on startup (safe to run on every boot — idempotent).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OuraDbContext>();
    await db.Database.MigrateAsync();
}

// Validate critical configuration and log warnings into AppLogSink
// so they surface in the UI without needing to read server logs.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    var ouraOpts = scope.ServiceProvider.GetRequiredService<IOptions<OuraOptions>>().Value;
    if (ouraOpts.Users.Count == 0)
        logger.LogWarning("Config: Oura has no users configured. Add users with API tokens to appsettings.Local.json.");
    else
        foreach (var u in ouraOpts.Users.Where(u => string.IsNullOrWhiteSpace(u.Token)))
            logger.LogWarning("Config: Oura user '{User}' has an empty token. Sync will fail for this user.", u.Name);

    var weatherOpts = scope.ServiceProvider.GetRequiredService<IOptions<WeatherOptions>>().Value;
    if (weatherOpts.Enabled && string.IsNullOrWhiteSpace(weatherOpts.LocationName))
        logger.LogWarning("Config: Weather is enabled but LocationName is empty.");

    var llmOpts = scope.ServiceProvider.GetRequiredService<IOptions<LlmOptions>>().Value;
    if (llmOpts.Enabled && !Uri.TryCreate(llmOpts.BaseUrl, UriKind.Absolute, out _))
        logger.LogWarning("Config: Llm is enabled but BaseUrl is not a valid absolute URL.");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
