using ApexCharts;
using Microsoft.EntityFrameworkCore;
using OuraDashboard.Data;
using OuraDashboard.Sync;
using OuraDashboard.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<OuraOptions>(builder.Configuration.GetSection(OuraOptions.SectionName));
builder.Services.AddOuraDatabase(builder.Configuration.GetConnectionString("Default")!);
builder.Services.AddOuraSync(addBackgroundService: true);
builder.Services.AddApexCharts();
builder.Services.AddScoped<OuraDashboard.Web.Services.DashboardQueryService>();

var app = builder.Build();

// Apply EF Core migrations on startup (safe to run on every boot — idempotent).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OuraDbContext>();
    await db.Database.MigrateAsync();
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
