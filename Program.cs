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

// Cascading value demo — UserContext distributed via CascadingValueSource.
// Registered as Singleton so all circuits share the same instance and the toggle
// on CascadingDemo.razor is visible to all active sessions (demo-only; real apps
// would use Scoped to isolate per user).
var userContext = new UserContext();
var userContextSource = new CascadingValueSource<UserContext>(userContext, isFixed: false);
builder.Services.AddSingleton(userContext);
builder.Services.AddSingleton(userContextSource);
builder.Services.AddCascadingValue(_ => userContextSource);

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
