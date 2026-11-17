using System.IdentityModel.Tokens.Jwt;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xumora.API.Models;
using Xumora.Identity.Application;

namespace Xumora.API.Controllers;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[ApiController]
public sealed class AuthenticationController(
    IIdentityService identityService) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthenticationResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await identityService.RegisterAsync(
            request.Email,
            request.Password,
            cancellationToken);

        if (result.Succeeded)
        {
            return Ok(AuthenticationResponse.From(result.Value!));
        }

        if (result.Conflict)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Account already exists",
                detail: "An account with this email already exists.");
        }

        return ValidationProblem(new ValidationProblemDetails(
            result.Errors!.ToDictionary(
                error => error.Key,
                error => error.Value)));
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthenticationResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await identityService.LoginAsync(
            request.Email,
            request.Password,
            cancellationToken);

        return result is null
            ? Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials",
                detail: "The email or password is incorrect.")
            : Ok(AuthenticationResponse.From(result));
    }

    [HttpPost("google")]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AuthenticationResponse>> GoogleSignIn(
        GoogleSignInRequest request,
        CancellationToken cancellationToken)
    {
        var result = await identityService.SignInWithGoogleAsync(
            request.IdToken,
            cancellationToken);

        if (result.Succeeded)
        {
            return Ok(AuthenticationResponse.From(result.Authentication!));
        }

        return result.Failure switch
        {
            GoogleSignInFailure.NotConfigured => Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Google sign-in is unavailable",
                detail: "Google sign-in has not been configured for this service."),
            GoogleSignInFailure.AccountConflict => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Account already exists",
                detail: "This email already belongs to an account. Sign in using its existing method."),
            GoogleSignInFailure.InvalidCredential => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid Google credential",
                detail: "The Google ID token is invalid or expired."),
            _ => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Google sign-in failed")
        };
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthenticationResponse>> Refresh(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await identityService.RefreshAsync(
            request.RefreshToken,
            cancellationToken);

        return result is null
            ? Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid refresh token",
                detail: "The refresh token is invalid or expired.")
            : Ok(AuthenticationResponse.From(result));
    }

    [HttpPost("revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        await identityService.RevokeAsync(
            request.RefreshToken,
            cancellationToken);

        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserProfileResponse>> Me(
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (userId is null)
        {
            return Unauthorized();
        }

        var profile = await identityService.GetProfileAsync(
            userId,
            cancellationToken);

        return profile is null
            ? NotFound()
            : Ok(new UserProfileResponse(profile.Id, profile.Email));
    }
}
