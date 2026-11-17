using Xumora.Identity.Application;

namespace Xumora.API.Models;

public sealed record AuthenticationResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserProfileResponse User)
{
    public static AuthenticationResponse From(AuthenticationResult result) =>
        new(
            result.AccessToken,
            result.AccessTokenExpiresAt,
            result.RefreshToken,
            result.RefreshTokenExpiresAt,
            new UserProfileResponse(result.UserId, result.Email));
}

public sealed record UserProfileResponse(string Id, string Email);
