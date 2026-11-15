using System.Net;
using System.Net.Http.Json;
using GLMS.IntegrationTests.Infrastructure;

namespace GLMS.IntegrationTests.Api;

/// <summary>Authentication, health, Swagger, dashboard and rates.</summary>
[Collection(ApiCollection.Name)]
public sealed class PlatformApiTests : ApiTestBase
{
    public PlatformApiTests(GlmsApiFixture api) : base(api) { }

    [Fact]
    public async Task Login_WithValidCredentials_Returns200AndAToken()
    {
        using var anonymous = Api.CreateAnonymousClient();

        var response = await anonymous.PostAsync("api/auth/login", Json(new LoginRequest { Username = "admin", Password = Api.AdminPassword }));

        await AssertStatusAsync(HttpStatusCode.OK, response);
        var login = await ReadAsync<LoginResponse>(response);
        Assert.Equal(3, login.AccessToken.Split('.').Length);   // header.payload.signature
        Assert.Equal(GlmsRoles.Admin, login.Role);
        Assert.True(login.ExpiresUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        using var anonymous = Api.CreateAnonymousClient();

        var response = await anonymous.PostAsync("api/auth/login", Json(new LoginRequest { Username = "admin", Password = "wrong-password" }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("The username or password is incorrect.", (await ReadProblemAsync(response)).Detail);
    }

    [Theory]
    [InlineData("api/contracts")]
    [InlineData("api/clients")]
    [InlineData("api/service-requests")]
    [InlineData("api/dashboard")]
    [InlineData("api/rates/usd-zar")]
    public async Task ProtectedEndpoints_WithoutAToken_Return401(string url)
    {
        using var anonymous = Api.CreateAnonymousClient();

        var response = await anonymous.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithATamperedToken_Returns401()
    {
        using var anonymous = Api.CreateAnonymousClient();
        anonymous.DefaultRequestHeaders.Authorization = new("Bearer", "eyJhbGciOiJIUzI1NiJ9.eyJuYW1lIjoiaGFja2VyIn0.invalid-signature");

        var response = await anonymous.GetAsync("api/contracts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsTheSignedInUser()
    {
        var me = await GetJsonAsync<CurrentUserDto>("api/auth/me");

        Assert.Equal("admin", me.Username);
        Assert.Equal(GlmsRoles.Admin, me.Role);
    }

    [Fact]
    public async Task Health_Returns200_WithoutAToken()
    {
        using var anonymous = Api.CreateAnonymousClient();

        var response = await anonymous.GetAsync("health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_DocumentListsTheContractEndpoints()
    {
        using var anonymous = Api.CreateAnonymousClient();

        var response = await anonymous.GetAsync("swagger/v1/swagger.json");

        await AssertStatusAsync(HttpStatusCode.OK, response);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"/api/contracts\"", json);
        Assert.Contains("\"/api/contracts/{id}/status\"", json);
        Assert.Contains("\"patch\"", json);
    }

    [Fact]
    public async Task Dashboard_Returns200_WithCountsAndWorkItems()
    {
        var dashboard = await GetJsonAsync<DashboardDto>("api/dashboard");

        Assert.True(dashboard.TotalContracts > 0);
        Assert.NotNull(dashboard.WorkItems);
        Assert.NotNull(dashboard.Requests);
    }

    [Fact]
    public async Task UsdToZarRate_Returns200WithAPositiveRate_Or503WhenTheRateServiceIsDown()
    {
        var response = await Client.GetAsync("api/rates/usd-zar");

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable && Api.IsExternal)
        {
            await ReadProblemAsync(response); // live mode without internet: a clear 503, never a crash
            return;
        }

        await AssertStatusAsync(HttpStatusCode.OK, response);
        var quote = await response.Content.ReadFromJsonAsync<RateQuoteDto>(GlmsJson.Options);
        Assert.Equal("USD", quote!.FromCurrency);
        Assert.Equal("ZAR", quote.ToCurrency);
        Assert.True(quote.Rate > 0);
    }
}
