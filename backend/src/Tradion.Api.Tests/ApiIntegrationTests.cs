using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tradion.Api.DTOs.Auth;
using Xunit;

namespace Tradion.Api.Tests;

/// <summary>
/// Integration tests that hit the real API (in-memory DB, Testing environment).
/// </summary>
public class ApiIntegrationTests : IClassFixture<TradionWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiIntegrationTests(TradionWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dashboard_GetCounts_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync("/api/Dashboard/counts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Auth_Register_Then_Login_Then_Dashboard_GetCounts_Returns200()
    {
        var email = $"integration-{Guid.NewGuid():N}@test.local";
        var password = "TestPass1!";

        // Register
        var registerResponse = await _client.PostAsJsonAsync("/api/Auth/register", new RegisterRequest
        {
            Email = email,
            Password = password,
            FullName = "Integration Test User",
            CompanyName = "Integration Test Company"
        });
        registerResponse.EnsureSuccessStatusCode();

        // Login
        var loginResponse = await _client.PostAsJsonAsync("/api/Auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(loginResult?.Token);

        // Dashboard (authenticated)
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginResult.Token);
        var dashboardResponse = await _client.GetAsync("/api/Dashboard/counts");
        dashboardResponse.EnsureSuccessStatusCode();

        var json = await dashboardResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("unprocessedRequests", out _));
        Assert.True(root.TryGetProperty("ongoingJobCards", out _));
        Assert.True(root.TryGetProperty("overdueInvoices", out _));
    }
}
