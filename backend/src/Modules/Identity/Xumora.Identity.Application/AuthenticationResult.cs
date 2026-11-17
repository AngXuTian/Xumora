namespace Xumora.Identity.Application;

public sealed record AuthenticationResult(
    string UserId,
    string Email,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
