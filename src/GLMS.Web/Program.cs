using System.Globalization;
using GLMS.Shared;
using GLMS.Web.ApiClients;
using GLMS.Web.Filters;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

// =====================================================================================================
// GLMS.Web - the PRESENTATION LAYER (Part 3). It has no database connection and no reference to GLMS.Core:
// every page gets and saves its data by calling GLMS.Api over HTTP (typed HttpClients in ApiClients/).
// =====================================================================================================

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- MVC
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()); // every POST form carries an anti-forgery token
    options.Filters.Add<ApiExceptionFilter>();                         // API down / 401 / 403 / 404 handled once
});

// Signed agreements are capped at 10 MB (the API enforces it again).
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = ValidationLimits.AgreementMaxBytes + 1024 * 1024);

// ---------------------------------------------------------------- Authentication: cookie for the browser, JWT for the API
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.Cookie.Name = "glms.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.SlidingExpiration = false;   // the cookie must not outlive the JWT inside it
    });

// Every page needs a signed-in user unless it is marked [AllowAnonymous] (login, error pages).
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// In Docker the keys that encrypt the cookie live on a volume, so users stay signed in across container restarts.
var keysPath = builder.Configuration.GetValue<string>("DataProtection:KeysPath");
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("glms-web");
if (!string.IsNullOrWhiteSpace(keysPath))
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

// ---------------------------------------------------------------- Typed HttpClients for GLMS.Api (the frontend "service layer")
// In Docker Compose: GlmsApi__BaseUrl=http://glms-backend-api:8080/ (the API's service name on the internal network).
var apiBaseUrl = builder.Configuration.GetValue<string>("GlmsApi:BaseUrl")
    ?? throw new InvalidOperationException("GlmsApi:BaseUrl is missing (appsettings.json or the GlmsApi__BaseUrl environment variable).");
var apiTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("GlmsApi:TimeoutSeconds", 20));

void ConfigureApiClient(HttpClient client)
{
    client.BaseAddress = new Uri(apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/");
    client.Timeout = apiTimeout;
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<BearerTokenHandler>();

builder.Services.AddHttpClient<IAuthApi, AuthApi>(ConfigureApiClient); // login: no token yet
builder.Services.AddHttpClient<IClientsApi, ClientsApi>(ConfigureApiClient).AddHttpMessageHandler<BearerTokenHandler>();
builder.Services.AddHttpClient<IContractsApi, ContractsApi>(ConfigureApiClient).AddHttpMessageHandler<BearerTokenHandler>();
builder.Services.AddHttpClient<IServiceRequestsApi, ServiceRequestsApi>(ConfigureApiClient).AddHttpMessageHandler<BearerTokenHandler>();
builder.Services.AddHttpClient<IRatesApi, RatesApi>(ConfigureApiClient).AddHttpMessageHandler<BearerTokenHandler>();
builder.Services.AddHttpClient<IDashboardApi, DashboardApi>(ConfigureApiClient).AddHttpMessageHandler<BearerTokenHandler>();

builder.Services.AddHealthChecks();

var app = builder.Build();

var culture = new CultureInfo("en-GB");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(culture),
    SupportedCultures = new[] { culture },
    SupportedUICultures = new[] { culture }
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    if (app.Configuration.GetValue("Https:Redirect", false)) app.UseHsts();
}

// Friendly pages for 404 (unknown address or missing record) and other error status codes.
app.UseStatusCodePagesWithReExecute("/status/{0}");

// In Docker the site is served on plain HTTP (port 8080); HTTPS redirection is only switched on outside containers.
if (app.Configuration.GetValue("Https:Redirect", false))
    app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
