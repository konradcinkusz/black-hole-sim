using System.ComponentModel.DataAnnotations;

namespace BlackHoleSim.Web.Services;

/// <summary>
/// The email/password pair behind the sign-in and registration forms.
/// </summary>
/// <remarks>
/// Validated here only to keep the user from a pointless round trip; the identity service
/// applies its own rules (and is the one that decides what a good password is) regardless of
/// what this bundle checked first.
/// </remarks>
public sealed class CredentialsForm
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "That does not look like an email address")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters")]
    public string Password { get; set; } = "";
}
