using Fluxor;
using Fluxor.Blazor.Web.ReduxDevTools;
using Microsoft.AspNetCore.Components;
using StateManagementPoc.Components;
using StateManagementPoc.Models;
using StateManagementPoc.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<CaseSelectionState>();
builder.Services.AddScoped<CaseRepository>();
builder.Services.AddScoped<OrderDraftState>();
builder.Services.AddScoped<StateContainer>();

// CascadingValueSource demo — SharedStringState distributed per-circuit via CascadingValueSource.
// Scoped so each user gets their own isolated instance (contrast with Singleton UserContext above).
builder.Services.AddScoped<SharedStringState>();
builder.Services.AddScoped<CascadingValueSource<SharedStringState>>(sp =>
    new CascadingValueSource<SharedStringState>(
        sp.GetRequiredService<SharedStringState>(), isFixed: false));
builder.Services.AddCascadingValue(sp =>
    sp.GetRequiredService<CascadingValueSource<SharedStringState>>());

builder.Services.AddScoped<UserContext>();
builder.Services.AddScoped<CascadingValueSource<UserContext>>(sp =>
    new CascadingValueSource<UserContext>(
        sp.GetRequiredService<UserContext>(), isFixed: false));
builder.Services.AddCascadingValue(sp =>
    sp.GetRequiredService<CascadingValueSource<UserContext>>());

// Fluxor: third-party Redux-style state management, added for comparison purposes only —
// see docs/state-management.md for built-in alternatives used elsewhere in this project.
builder.Services.AddFluxor(options => options
    .ScanAssemblies(typeof(Program).Assembly)
    .UseReduxDevTools());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
