using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xumora.API.Models;
using Xumora.Identity.Infrastructure.Persistence;

namespace Xumora.API.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ApiIntegrationTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task Live_endpoint_returns_typed_health_response_and_correlation_id()
    {
        using var factory = new XumoraApiFactory(database.ConnectionString);
        await factory.ApplyMigrationsAsync();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/health/live");
        request.Headers.Add("X-Correlation-ID", "integration-test-123");

        using var response = await client.SendAsync(request);
        var health = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("integration-test-123",
            response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.NotNull(health);
        Assert.Equal("Healthy", health.Status);
        Assert.Equal("Xumora.API", health.Service);
        Assert.Null(health.Database);
    }

    [Fact]
    public async Task Invalid_correlation_id_is_replaced()
    {
        using var factory = new XumoraApiFactory(database.ConnectionString);
        await factory.ApplyMigrationsAsync();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/health/live");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", "invalid id");

        using var response = await client.SendAsync(request);
        var correlationId = response.Headers
            .GetValues("X-Correlation-ID")
            .Single();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual("invalid id", correlationId);
        Assert.Matches("^[A-Za-z0-9._-]{1,64}$", correlationId);
    }

    [Fact]
    public async Task Readiness_endpoint_connects_to_test_postgres()
    {
        using var factory = new XumoraApiFactory(database.ConnectionString);
        await factory.ApplyMigrationsAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/health/ready");
        var health = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(health);
        Assert.Equal("Healthy", health.Status);
        Assert.Equal("Connected", health.Database);
    }

    [Fact]
    public async Task Development_openapi_document_is_generated_per_version()
    {
        using var factory = new XumoraApiFactory(
            database.ConnectionString,
            Environments.Development);
        await factory.ApplyMigrationsAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Xumora API",
            document.RootElement.GetProperty("info").GetProperty("title").GetString());
        Assert.True(document.RootElement.GetProperty("paths")
            .TryGetProperty("/api/v1/health/live", out _));
    }

    [Fact]
    public async Task Registration_login_and_profile_require_valid_credentials()
    {
        using var factory = new XumoraApiFactory(database.ConnectionString);
        await factory.ApplyMigrationsAsync();
        using var client = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        const string password = "Strong-password-47!";

        using var unauthenticatedProfile = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedProfile.StatusCode);

        using var registerResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest { Email = email, Password = password });
        var registered = await registerResponse.Content
            .ReadFromJsonAsync<AuthenticationResponse>();

        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        Assert.NotNull(registered);
        Assert.Equal(email, registered.User.Email);
        Assert.False(string.IsNullOrWhiteSpace(registered.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(registered.RefreshToken));

        using var duplicateRegistration = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.Conflict, duplicateRegistration.StatusCode);

        using var invalidLogin = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = email, Password = "incorrect-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, invalidLogin.StatusCode);

        using var loginResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = email, Password = password });
        var loggedIn = await loginResponse.Content
            .ReadFromJsonAsync<AuthenticationResponse>();
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.NotNull(loggedIn);

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                loggedIn.AccessToken);
        var profile = await client.GetFromJsonAsync<UserProfileResponse>(
            "/api/v1/auth/me");
        Assert.NotNull(profile);
        Assert.Equal(email, profile.Email);
    }

    [Fact]
    public async Task Reusing_rotated_refresh_token_revokes_its_family()
    {
        using var factory = new XumoraApiFactory(database.ConnectionString);
        await factory.ApplyMigrationsAsync();
        using var client = factory.CreateClient();
        var email = $"rotate-{Guid.NewGuid():N}@example.com";

        using var registerResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest
            {
                Email = email,
                Password = "Strong-password-47!"
            });
        var initial = await registerResponse.Content
            .ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(initial);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<IdentityDbContext>();
            var expectedHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(initial.RefreshToken)));
            Assert.True(await dbContext.RefreshTokens.AnyAsync(
                token => token.TokenHash == expectedHash));
            Assert.False(await dbContext.RefreshTokens.AnyAsync(
                token => token.TokenHash == initial.RefreshToken));
        }

        using var rotateResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest { RefreshToken = initial.RefreshToken });
        var rotated = await rotateResponse.Content
            .ReadFromJsonAsync<AuthenticationResponse>();
        Assert.Equal(HttpStatusCode.OK, rotateResponse.StatusCode);
        Assert.NotNull(rotated);
        Assert.NotEqual(initial.RefreshToken, rotated.RefreshToken);

        using var replayResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest { RefreshToken = initial.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        using var revokedFamilyResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest { RefreshToken = rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, revokedFamilyResponse.StatusCode);
    }

    [Fact]
    public async Task Revoked_refresh_token_cannot_be_used()
    {
        using var factory = new XumoraApiFactory(database.ConnectionString);
        await factory.ApplyMigrationsAsync();
        using var client = factory.CreateClient();

        using var registerResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest
            {
                Email = $"revoke-{Guid.NewGuid():N}@example.com",
                Password = "Strong-password-47!"
            });
        var registered = await registerResponse.Content
            .ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(registered);

        using var revokeResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/revoke",
            new RefreshTokenRequest { RefreshToken = registered.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        using var refreshResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest { RefreshToken = registered.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Google_sign_in_requires_server_configuration()
    {
        using var factory = new XumoraApiFactory(
            database.ConnectionString,
            googleWebClientId: null);
        await factory.ApplyMigrationsAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/google",
            new GoogleSignInRequest
            {
                IdToken = "a-valid-shaped-id-token-value-for-validation"
            });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Google_sign_in_rejects_an_invalid_id_token()
    {
        using var factory = new XumoraApiFactory(database.ConnectionString);
        await factory.ApplyMigrationsAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/google",
            new GoogleSignInRequest
            {
                IdToken = "not-a-valid-google-id-token-value"
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
