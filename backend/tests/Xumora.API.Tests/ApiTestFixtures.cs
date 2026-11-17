using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xumora.Identity.Infrastructure.Persistence;

namespace Xumora.API.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL";
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("xumora_test")
        .WithUsername("xumora")
        .WithPassword("xumora-test-password")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

public sealed class XumoraApiFactory(
    string connectionString,
    string environment = "Testing",
    string? googleWebClientId = "xumora-test.apps.googleusercontent.com")
    : WebApplicationFactory<Program>
{
    public async Task ApplyMigrationsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<IdentityDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Jwt:Issuer"] = "Xumora.Tests",
                ["Jwt:Audience"] = "Xumora.Tests",
                ["Jwt:SigningKey"] = "integration-test-only-signing-key-32-bytes-minimum",
                ["Google:WebClientId"] = googleWebClientId
            });
        });
    }
}
