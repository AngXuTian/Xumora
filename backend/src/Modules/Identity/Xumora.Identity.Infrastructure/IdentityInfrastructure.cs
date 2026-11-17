using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xumora.Identity.Application;
using Xumora.Identity.Infrastructure.Configuration;
using Xumora.Identity.Infrastructure.Persistence;
using Xumora.Identity.Infrastructure.Services;

namespace Xumora.Identity.Infrastructure;

public static class IdentityInfrastructure
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services)
    {
        services.AddDbContext<IdentityDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider
                .GetRequiredService<IConfiguration>()
                .GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "Database connection string is missing.");

            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(
                    typeof(IdentityDbContext).Assembly.FullName));
        });

        services
            .AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
                "JWT signing key must be at least 32 bytes.")
            .Validate(
                options => options.RefreshTokenDays <= options.RefreshTokenAbsoluteDays,
                "Refresh token lifetime cannot exceed its absolute lifetime.")
            .ValidateOnStart();

        services
            .AddOptions<GoogleOptions>()
            .BindConfiguration(GoogleOptions.SectionName);

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddSignInManager()
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddDefaultTokenProviders();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = "sub"
                };
            });

        services.AddAuthorization();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IIdentityService, IdentityService>();

        return services;
    }
}
