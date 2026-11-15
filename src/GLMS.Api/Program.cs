using System.Globalization;
using GLMS.Api.Auth;
using GLMS.Api.Data;
using GLMS.Api.Infrastructure;
using GLMS.Api.Services;
using GLMS.Core.Abstractions;
using GLMS.Core.Files;
using GLMS.Core.Patterns.Factory;
using GLMS.Core.Patterns.Observer;
using GLMS.Core.Patterns.Strategy;
using GLMS.Core.Services;
using GLMS.Shared;
using Microsoft.AspNetCore.Identity;

// =====================================================================================================
// GLMS.Api - the SERVICE LAYER of the Part 3 service-oriented architecture.
// It is the only process with a database connection. The MVC front end (GLMS.Web) reaches the data
// exclusively through these REST endpoints over HTTP.
// =====================================================================================================

var builder = WebApplication.CreateBuilder(args);

var culture = new CultureInfo("en-GB");
CultureInfo.DefaultThreadCurrentCulture = culture;   // business-rule messages format numbers/dates consistently
CultureInfo.DefaultThreadCurrentUICulture = culture;

// ---------------------------------------------------------------- Web API + JSON (enums as text, camelCase)
builder.Services
    .AddControllers(o =>
    {
        // JSON only (no text/plain or XML). Errors keep their own "application/problem+json" content type.
        o.OutputFormatters.RemoveType<Microsoft.AspNetCore.Mvc.Formatters.StringOutputFormatter>();
        o.ReturnHttpNotAcceptable = false;
    })
    .AddJsonOptions(o => GlmsJson.Configure(o.JsonSerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddGlmsSwagger();

// ---------------------------------------------------------------- Database (repositories, health check)
builder.Services.AddGlmsData(builder.Configuration);

//  Authentication (JWT)
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<SeedUserOptions>(builder.Configuration.GetSection("SeedUsers"));
builder.Services.AddGlmsJwtAuthentication(builder.Configuration);
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<AuthService>();

// Cross-cutting
builder.Services.AddSingleton<IClock, SystemClock>();

// Signed agreements live on a Docker volume in containers
var storageRoot = Path.Combine(builder.Environment.ContentRootPath,
    builder.Configuration.GetValue<string>("FileStorage:RootPath") ?? "App_Data/agreements");
builder.Services.AddSingleton<IFileStorageService>(new LocalFileStorageService(storageRoot));

// STRATEGY pattern wiring 
builder.Services.AddHttpClient<LiveRateApiStrategy>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration.GetValue<string>("CurrencyApi:BaseUrl") ?? LiveRateApiStrategy.DefaultBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("CurrencyApi:TimeoutSeconds", 8));
});
builder.Services.AddSingleton<ExchangeRateCache>();
var cacheMinutes = builder.Configuration.GetValue("CurrencyApi:CacheMinutes", 60);
var maxStaleHours = builder.Configuration.GetValue("CurrencyApi:MaxStaleHours", 24);

var currencyMode = builder.Configuration.GetValue<string>("CurrencyApi:Mode") ?? "Live";
if (string.Equals(currencyMode, "Fixed", StringComparison.OrdinalIgnoreCase))
{
    var fixedRates = builder.Configuration.GetSection("CurrencyApi:FixedRates").Get<Dictionary<string, decimal>>()
        ?? throw new InvalidOperationException("CurrencyApi:Mode is Fixed but CurrencyApi:FixedRates is missing.");
    builder.Services.AddSingleton<ICurrencyConversionStrategy>(sp => new FixedRateStrategy(fixedRates, sp.GetRequiredService<IClock>()));
}
else
{
    builder.Services.AddScoped<ICurrencyConversionStrategy>(sp => new CachedRateStrategy(
        sp.GetRequiredService<LiveRateApiStrategy>(),
        sp.GetRequiredService<ExchangeRateCache>(),
        sp.GetRequiredService<IClock>(),
        TimeSpan.FromMinutes(cacheMinutes),
        TimeSpan.FromHours(maxStaleHours)));
}
builder.Services.AddScoped<FinancialService>();

// OBSERVER pattern wiring
builder.Services.AddScoped<ServiceRequestValidator>();
builder.Services.AddScoped<IContractObserver>(sp => sp.GetRequiredService<ServiceRequestValidator>());
builder.Services.AddScoped<IContractObserver, ComplianceLogger>();

// FACTORY METHOD pattern wiring
builder.Services.AddScoped<ServiceRequestFactory, StandardSLAFactory>();
builder.Services.AddScoped<ServiceRequestFactory, PriorityLogisticsFactory>();
builder.Services.AddScoped<ServiceRequestFactoryResolver>();

// Application services (GLMS.Core) + API read models
builder.Services.AddScoped<ClientService>();
builder.Services.AddScoped<ContractService>();
builder.Services.AddScoped<ServiceRequestService>();
builder.Services.AddScoped<ContractReadService>();
builder.Services.AddScoped<DashboardService>();

if (builder.Configuration.GetValue("ContractExpiry:Enabled", true))
    builder.Services.AddHostedService<ContractExpiryWorker>();

var app = builder.Build();

// ---------------------------------------------------------------- Database: migrate + seed (retries while SQL Server starts)
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
    await DatabaseInitializer.InitializeAsync(app.Services);

// ---------------------------------------------------------------- HTTP pipeline
app.UseExceptionHandler();      // -> GlobalExceptionHandler (problem details)
app.UseStatusCodePages();       // empty 401/403/404/405 responses get a problem-details body too

if (app.Configuration.GetValue("Swagger:Enabled", true))
    app.UseGlmsSwagger();

// The API sits behind the MVC app on an internal network (plain HTTP on 8080 in Docker). A redirect to HTTPS would
// make HttpClient drop the Authorization header, so it is opt-in.
if (app.Configuration.GetValue("Https:Redirect", false))
    app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

/// <summary>Exposed so the integration tests can host the API in memory with WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program { }
