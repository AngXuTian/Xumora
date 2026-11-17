namespace Xumora.Identity.Application;

public interface IIdentityService
{
    Task<IdentityOperationResult<AuthenticationResult>> RegisterAsync(
        string email,
        string password,
        CancellationToken cancellationToken);

    Task<AuthenticationResult?> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken);

    Task<GoogleSignInResult> SignInWithGoogleAsync(
        string idToken,
        CancellationToken cancellationToken);

    Task<AuthenticationResult?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    Task RevokeAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    Task<UserProfile?> GetProfileAsync(
        string userId,
        CancellationToken cancellationToken);
}
