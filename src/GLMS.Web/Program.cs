using System.Globalization;
using GLMS.Core.Abstractions;
using GLMS.Core.Files;
using GLMS.Core.Patterns.Factory;
using GLMS.Core.Patterns.Observer;
using GLMS.Core.Patterns.Strategy;
using GLMS.Core.Services;
using GLMS.Web.Data;
using GLMS.Web.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- MVC
builder.Services.AddControllersWithViews(options =>
{
    // Every POST form must carry an anti-forgery token.
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

// Signed agreements are capped at 10 MB (also enforced in FileValidator).
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = FileValidator.MaxFileSizeBytes + 1024 * 1024);

// ---------------------------------------------------------------- Database (connection string lives in appsettings.json)
var connectionString = builder.Configuration.GetConnectionString("GlmsDatabase")
    ?? throw new InvalidOperationException("Connection string 'GlmsDatabase' is missing from appsettings.json.");

builder.Services.AddDbContext<GlmsDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3)));

builder.Services.AddScoped<IClientRepository, ClientRepository>();
builder.Services.AddScoped<IContractRepository, ContractRepository>();
builder.Services.AddScoped<IServiceRequestRepository, ServiceRequestRepository>();
builder.Services.AddScoped<IAuditSink, EfAuditSink>();

// ---------------------------------------------------------------- Cross-cutting
builder.Services.AddSingleton<IClock, SystemClock>();

var storageRoot = Path.Combine(builder.Environment.ContentRootPath,
    builder.Configuration.GetValue<string>("FileStorage:RootPath") ?? "App_Data/agreements");
builder.Services.AddSingleton<IFileStorageService>(new LocalFileStorageService(storageRoot));

// ---------------------------------------------------------------- STRATEGY pattern wiring
// Live API strategy gets a typed HttpClient from IHttpClientFactory (sockets are pooled correctly).
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
        ?? throw new InvalidOperationException("CurrencyApi:Mode is Fixed but CurrencyApi:FixedRates is missing from appsettings.json.");
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
// One ServiceRequestValidator instance per request is used both as the validator AND as an observer.
builder.Services.AddScoped<ServiceRequestValidator>();
builder.Services.AddScoped<IContractObserver>(sp => sp.GetRequiredService<ServiceRequestValidator>());
builder.Services.AddScoped<IContractObserver, ComplianceLogger>();

//  FACTORY METHOD pattern wiring
builder.Services.AddScoped<ServiceRequestFactory, StandardSLAFactory>();
builder.Services.AddScoped<ServiceRequestFactory, PriorityLogisticsFactory>();
builder.Services.AddScoped<ServiceRequestFactoryResolver>();

//  Application services
builder.Services.AddScoped<ClientService>();
builder.Services.AddScoped<ContractService>();
builder.Services.AddScoped<ServiceRequestService>();

builder.Services.AddHostedService<ContractExpiryWorker>();

var app = builder.Build();

var culture = new CultureInfo("en-GB");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(culture),
    SupportedCultures = new[] { culture },
    SupportedUICultures = new[] { culture }
});

//e migrate + seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GlmsDbContext>();
    if (!db.Database.GetMigrations().Any())
    {
        throw new InvalidOperationException(
            "No EF Core migrations found. In Visual Studio open Tools > NuGet Package Manager > Package Manager Console, " +
            "select GLMS.Web as the default project and run:  Add-Migration InitialCreate   (see README.md).");
    }

    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db,
        scope.ServiceProvider.GetRequiredService<IFileStorageService>(),
        scope.ServiceProvider.GetRequiredService<IClock>());
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Friendly pages for 404 (unknown address or missing record) and other error status codes.
app.UseStatusCodePagesWithReExecute("/status/{0}");

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
