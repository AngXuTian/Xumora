using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xumora.Identity.Application;
using Xumora.Identity.Domain;
using Xumora.Identity.Infrastructure.Configuration;
using Xumora.Identity.Infrastructure.Persistence;

namespace Xumora.Identity.Infrastructure.Services;

internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IdentityDbContext dbContext,
    IOptions<JwtOptions> jwtOptions,
    IOptions<GoogleOptions> googleOptions,
    TimeProvider timeProvider,
    ILogger<IdentityService> logger) : IIdentityService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;
    private readonly GoogleOptions _google = googleOptions.Value;

    public async Task<IdentityOperationResult<AuthenticationResult>> RegisterAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var user = new ApplicationUser
            {
                Email = email,
                UserName = email
            };

            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                var errors = createResult.Errors
                    .GroupBy(error => error.Code.StartsWith(
                        "Password",
                        StringComparison.Ordinal)
                        ? "password"
                        : "email")
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.Description).ToArray());

                var conflict = createResult.Errors.Any(error =>
                    error.Code is "DuplicateEmail" or "DuplicateUserName");

                return IdentityOperationResult<AuthenticationResult>.Failure(
                    errors,
                    conflict);
            }

            var (result, _) = CreateAuthentication(
                user,
                Guid.NewGuid(),
                familyExpiresAt: null);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return IdentityOperationResult<AuthenticationResult>.Success(result);
        }
        catch (DbUpdateException exception) when (
            IsUniqueConstraintViolation(exception, "EmailIndex")
            || IsUniqueConstraintViolation(exception, "UserNameIndex"))
        {
            return IdentityOperationResult<AuthenticationResult>.Failure(
                new Dictionary<string, string[]>
                {
                    ["email"] = ["An account with this email already exists."]
                },
                conflict: true);
        }
    }

    public async Task<AuthenticationResult?> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null
            || user.Email is null
            || !(await signInManager.CheckPasswordSignInAsync(
                user,
                password,
                lockoutOnFailure: true)).Succeeded)
        {
            return null;
        }

        var (result, _) = CreateAuthentication(
            user,
            Guid.NewGuid(),
            familyExpiresAt: null);

        await dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<GoogleSignInResult> SignInWithGoogleAsync(
        string idToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_google.WebClientId))
        {
            return GoogleSignInResult.Fail(GoogleSignInFailure.NotConfigured);
        }

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = [_google.WebClientId]
                });
        }
        catch (InvalidJwtException)
        {
            return GoogleSignInResult.Fail(GoogleSignInFailure.InvalidCredential);
        }

        if (string.IsNullOrWhiteSpace(payload.Subject)
            || string.IsNullOrWhiteSpace(payload.Email)
            || !payload.EmailVerified)
        {
            return GoogleSignInResult.Fail(GoogleSignInFailure.InvalidCredential);
        }

        var user = await userManager.FindByLoginAsync("Google", payload.Subject);
        var isNewUser = user is null;
        if (isNewUser)
        {
            if (await userManager.FindByEmailAsync(payload.Email) is not null)
            {
                return GoogleSignInResult.Fail(GoogleSignInFailure.AccountConflict);
            }

            user = new ApplicationUser
            {
                Email = payload.Email,
                UserName = payload.Email,
                EmailConfirmed = true
            };
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (isNewUser)
            {
                var createResult = await userManager.CreateAsync(user!);
                if (!createResult.Succeeded)
                {
                    var duplicateAccount = createResult.Errors.Any(error =>
                        error.Code is "DuplicateEmail" or "DuplicateUserName");
                    if (!duplicateAccount)
                    {
                        logger.LogError(
                            "Google sign-in account creation failed: {ErrorCodes}",
                            string.Join(", ", createResult.Errors.Select(error => error.Code)));
                    }

                    return GoogleSignInResult.Fail(
                        duplicateAccount
                            ? GoogleSignInFailure.AccountConflict
                            : GoogleSignInFailure.Failed);
                }

                var loginResult = await userManager.AddLoginAsync(
                    user!,
                    new UserLoginInfo("Google", payload.Subject, "Google"));
                if (!loginResult.Succeeded)
                {
                    logger.LogError(
                        "Google sign-in provider link creation failed: {ErrorCodes}",
                        string.Join(", ", loginResult.Errors.Select(error => error.Code)));
                    return GoogleSignInResult.Fail(GoogleSignInFailure.Failed);
                }
            }

            var (authentication, _) = CreateAuthentication(
                user!,
                Guid.NewGuid(),
                familyExpiresAt: null);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return GoogleSignInResult.Success(authentication);
        }
        catch (DbUpdateException exception) when (
            IsUniqueConstraintViolation(exception, "EmailIndex")
            || IsUniqueConstraintViolation(exception, "UserNameIndex")
            || IsUniqueConstraintViolation(exception, "PK_AspNetUserLogins"))
        {
            return GoogleSignInResult.Fail(GoogleSignInFailure.AccountConflict);
        }
    }

    public async Task<AuthenticationResult?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(refreshToken);
        var storedToken = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(
                token => token.TokenHash == tokenHash,
                cancellationToken);

        if (storedToken is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (storedToken.RevokedAt is not null)
        {
            await RevokeFamilyAsync(storedToken.FamilyId, now, cancellationToken);
            return null;
        }

        if (!storedToken.IsActive(now))
        {
            storedToken.Revoke(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        var user = await userManager.FindByIdAsync(storedToken.UserId);
        if (user?.Email is null)
        {
            storedToken.Revoke(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        var (next, replacementToken) = CreateAuthentication(
            user,
            storedToken.FamilyId,
            storedToken.FamilyExpiresAt);

        storedToken.Revoke(now, replacementToken.Id);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return next;
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }
    }

    public async Task RevokeAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(refreshToken);
        var storedToken = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(
                token => token.TokenHash == tokenHash,
                cancellationToken);

        if (storedToken is null || storedToken.RevokedAt is not null)
        {
            return;
        }

        storedToken.Revoke(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserProfile?> GetProfileAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId);
        return user?.Email is null
            ? null
            : new UserProfile(user.Id, user.Email);
    }

    private (AuthenticationResult Authentication, RefreshToken RefreshToken)
        CreateAuthentication(
        ApplicationUser user,
        Guid familyId,
        DateTimeOffset? familyExpiresAt)
    {
        var now = timeProvider.GetUtcNow();
        var accessTokenExpiresAt = now.AddMinutes(_jwt.AccessTokenMinutes);
        var absoluteExpiration = familyExpiresAt
            ?? now.AddDays(_jwt.RefreshTokenAbsoluteDays);
        var refreshTokenExpiresAt = Min(
            now.AddDays(_jwt.RefreshTokenDays),
            absoluteExpiration);
        var refreshToken = CreateRefreshToken();

        var jwt = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email!),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ],
            notBefore: now.UtcDateTime,
            expires: accessTokenExpiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256));

        var storedRefreshToken = new RefreshToken(
            user.Id,
            HashToken(refreshToken),
            familyId,
            now,
            refreshTokenExpiresAt,
            absoluteExpiration);
        dbContext.RefreshTokens.Add(storedRefreshToken);

        return (
            new AuthenticationResult(
                user.Id,
                user.Email!,
                new JwtSecurityTokenHandler().WriteToken(jwt),
                accessTokenExpiresAt,
                refreshToken,
                refreshTokenExpiresAt),
            storedRefreshToken);
    }

    private async Task RevokeFamilyAsync(
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var activeTokens = await dbContext.RefreshTokens
            .Where(token => token.FamilyId == familyId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.Revoke(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string CreateRefreshToken() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool IsUniqueConstraintViolation(
        DbUpdateException exception,
        string constraintName) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: var actualConstraintName
        } && actualConstraintName == constraintName;

    private static DateTimeOffset Min(
        DateTimeOffset left,
        DateTimeOffset right) => left < right ? left : right;
}
