namespace Xumora.Identity.Application;

public sealed record GoogleSignInResult(
    AuthenticationResult? Authentication,
    GoogleSignInFailure? Failure)
{
    public bool Succeeded => Authentication is not null;

    public static GoogleSignInResult Success(AuthenticationResult authentication) =>
        new(authentication, null);

    public static GoogleSignInResult Fail(GoogleSignInFailure failure) =>
        new(null, failure);
}
