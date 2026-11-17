using System.ComponentModel.DataAnnotations;

namespace Xumora.API.Models;

public sealed class RegisterRequest
{
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 12)]
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string Password { get; set; } = string.Empty;
}

public sealed class GoogleSignInRequest
{
    [Required]
    [StringLength(8192, MinimumLength = 32)]
    public string IdToken { get; set; } = string.Empty;
}

public sealed class RefreshTokenRequest
{
    [Required]
    [StringLength(256, MinimumLength = 32)]
    public string RefreshToken { get; set; } = string.Empty;
}
