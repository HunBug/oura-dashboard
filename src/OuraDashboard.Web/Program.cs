using ApexCharts;
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
