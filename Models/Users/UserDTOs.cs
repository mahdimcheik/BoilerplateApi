using InteractivesApi.Utilities;
using System.ComponentModel.DataAnnotations;

namespace InteractivesApi.Models.Users
{
    /// <summary>Payload to register a new local account (technician or client).</summary>
    public class RegisterRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(8)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string LastName { get; set; } = string.Empty;

        public DateTimeOffset? DateOfBirth { get; set; }

        /// <summary>Only <see cref="RoleEnum.Technician"/> or <see cref="RoleEnum.Client"/> are accepted.</summary>
        [Required]
        public RoleEnum Role { get; set; }

        [Required]
        public bool DataProcessingConsent { get; set; }

        [Required]
        public bool PrivacyPolicyConsent { get; set; }
    }

    public class LoginRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// Google sign-in/up. The frontend performs the Google OAuth flow and posts the
    /// resulting ID token here; the server validates it. <see cref="Role"/> is only
    /// used the first time (account creation) and ignored for an existing account.
    /// </summary>
    public class GoogleAuthRequest
    {
        [Required]
        public string IdToken { get; set; } = string.Empty;

        public RoleEnum? Role { get; set; }

        public bool DataProcessingConsent { get; set; }
        public bool PrivacyPolicyConsent { get; set; }
    }

    public class ForgotPasswordRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public string Token { get; set; } = string.Empty;

        [Required]
        [MinLength(8)]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string PasswordConfirmation { get; set; } = string.Empty;
    }

    // ---- Response shapes ----

    public class RoleDetails
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public RoleDetails() { }

        public RoleDetails(RoleApp role)
        {
            Id = role.Id;
            Name = role.Name ?? string.Empty;
        }
    }

    public class UserDetails
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? ImgUrl { get; set; }
        public AuthProviderEnum AuthProvider { get; set; }
        public string? Status { get; set; }
        public List<RoleDetails> Roles { get; set; } = new();

        public UserDetails() { }

        public UserDetails(UserApp user, List<RoleDetails>? roles)
        {
            Id = user.Id;
            Email = user.Email ?? string.Empty;
            FirstName = user.FirstName;
            LastName = user.LastName;
            ImgUrl = user.ImgUrl;
            AuthProvider = user.AuthProvider;
            Status = user.Status?.Name;
            Roles = roles ?? new List<RoleDetails>();
        }
    }

    /// <summary>Login/refresh result. RefreshToken is also set as an HttpOnly cookie.</summary>
    public class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public UserDetails? User { get; set; }
    }

    /// <summary>Current-user response with a fresh access token.</summary>
    public class UserInfoResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDetails? User { get; set; }
    }

}
