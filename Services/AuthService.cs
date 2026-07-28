    using BoilerPlateApi.Contexts;
    using BoilerPlateApi.Models.Responses;
    using BoilerPlateApi.Models.Users;
    using BoilerPlateApi.Utilities;
    using Google.Apis.Auth;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.IdentityModel.Tokens;
    using System.IdentityModel.Tokens.Jwt;
    using System.Security.Claims;
    using System.Text;
    using System.Web;

namespace BoilerPlateApi.Services
{
    public interface IAuthService
    {
        Task<Response<UserDetails>> Register(RegisterRequest req, CancellationToken ct);
        Task<Response<string>> EmailConfirmation(string userId, string token);
        Task<Response<LoginResponse>> Login(LoginRequest req, HttpResponse response);
        Task<Response<LoginResponse>> GoogleAuth(GoogleAuthRequest req, HttpResponse response);
        Task<Response<LoginResponse>> RefreshToken(string? refreshToken, HttpResponse response);
        Task<Response<object>> ForgotPassword(ForgotPasswordRequest req);
        Task<Response<object>> ResetPassword(ResetPasswordRequest req);
        Task<Response<UserInfoResponse>> GetMyInformations(ClaimsPrincipal principal);
        Task<Response<object>> Logout(ClaimsPrincipal principal, HttpResponse response);
        Task<string> GenerateAccessTokenAsync(UserApp user);
    }

    public class AuthService : IAuthService
    {
        private readonly MainContext _context;
        private readonly UserManager<UserApp> _userManager;
        private readonly MailService _mailService;
        private readonly ILogger<AuthService> _logger;
        private readonly IWebHostEnvironment _web;

        public AuthService(
            MainContext context,
            UserManager<UserApp> userManager,
            MailService mailService,
            ILogger<AuthService> logger,
            IWebHostEnvironment web
        )
        {
            _context = context;
            _userManager = userManager;
            _mailService = mailService;
            _logger = logger;
            _web = web;
        }

        // ---- Registration ----

        public async Task<Response<UserDetails>> Register(RegisterRequest req, CancellationToken ct)
        {
            if (req.Role != RoleEnum.Technician && req.Role != RoleEnum.Client)
                return Fail<UserDetails>(400, "Le rôle doit être Technician ou Client.");

            if (!req.DataProcessingConsent || !req.PrivacyPolicyConsent)
                return Fail<UserDetails>(400, "Le consentement est obligatoire.");

            if (await _userManager.FindByEmailAsync(req.Email) is not null)
                return Fail<UserDetails>(409, "Cet email est déjà utilisé.");

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var user = new UserApp
                {
                    UserName = req.Email,
                    Email = req.Email,
                    FirstName = req.FirstName,
                    LastName = req.LastName,
                    DateOfBirth = req.DateOfBirth,
                    DataProcessingConsent = req.DataProcessingConsent,
                    PrivacyPolicyConsent = req.PrivacyPolicyConsent,
                    AuthProvider = AuthProviderEnum.Local,
                    StatusId = HardCode.ACCOUNT_PENDING,
                    EmailConfirmed = false,
                };

                var createResult = await _userManager.CreateAsync(user, req.Password);
                if (!createResult.Succeeded)
                {
                    await transaction.RollbackAsync();
                    return Fail<UserDetails>(400, DescribeErrors(createResult));
                }

                var roleResult = await _userManager.AddToRoleAsync(user, RoleName(req.Role));
                if (!roleResult.Succeeded)
                {
                    await transaction.RollbackAsync();
                    return Fail<UserDetails>(400, DescribeErrors(roleResult));
                }

                //await CreateProfile(user, req.Role);
                await transaction.CommitAsync();

                // Email confirmation is best-effort — the account still exists if mail fails.
                try
                {
                    var link = await BuildConfirmationLink(user);
                    var name = $"{user.FirstName} {user.LastName}".Trim();
                    await _mailService.SendConfirmAccountAsync(user.Email, name, link, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Confirmation email failed for {Email}", user.Email);
                }

                user.Status = await _context.StatusAccounts.FindAsync(user.StatusId);
                return new Response<UserDetails>
                {
                    Status = 201,
                    Message = "Compte créé. Vérifiez votre email pour l'activer.",
                    Data = new UserDetails(user, null),
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Registration failed for {Email}", req.Email);
                return Fail<UserDetails>(500, "La création du compte a échoué.");
            }
        }

        public async Task<Response<string>> EmailConfirmation(string userId, string token)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
                return Fail<string>(400, "Validation échouée.");

            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (!result.Succeeded)
                return Fail<string>(400, "Validation échouée.");

            if (user.StatusId == HardCode.ACCOUNT_PENDING)
            {
                user.StatusId = HardCode.ACCOUNT_ACTIVE;
                await _userManager.UpdateAsync(user);
            }

            return new Response<string>
            {
                Status = 200,
                Message = $"{EnvironmentVariables.API_FRONT_URL}/auth/email-confirmation-success",
            };
        }

        // ---- Login / tokens ----

        public async Task<Response<LoginResponse>> Login(LoginRequest req, HttpResponse response)
        {
            var user = await _userManager.FindByEmailAsync(req.Email);

            // Generic message to avoid leaking which emails exist.
            if (user is null)
                return Fail<LoginResponse>(401, "Identifiants invalides.");

            if (
                user.AuthProvider == AuthProviderEnum.Google
                && !await _userManager.HasPasswordAsync(user)
            )
                return Fail<LoginResponse>(400, "Ce compte utilise la connexion Google.");

            if (!await _userManager.CheckPasswordAsync(user, req.Password))
                return Fail<LoginResponse>(401, "Identifiants invalides.");

            if (!user.EmailConfirmed)
                return Fail<LoginResponse>(
                    403,
                    "Veuillez confirmer votre email avant de vous connecter."
                );

            if (user.StatusId == HardCode.ACCOUNT_BANNED)
                return Fail<LoginResponse>(403, "Ce compte est suspendu.");

            return await IssueSession(user, response, "Connexion réussie.");
        }

        public async Task<Response<LoginResponse>> GoogleAuth(
            GoogleAuthRequest req,
            HttpResponse response
        )
        {
            if (string.IsNullOrWhiteSpace(EnvironmentVariables.GOOGLE_CLIENT_ID))
                return Fail<LoginResponse>(500, "La connexion Google n'est pas configurée.");

            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(
                    req.IdToken,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = new[] { EnvironmentVariables.GOOGLE_CLIENT_ID },
                    }
                );
            }
            catch (InvalidJwtException)
            {
                return Fail<LoginResponse>(401, "Jeton Google invalide.");
            }

            if (!payload.EmailVerified)
                return Fail<LoginResponse>(401, "Email Google non vérifié.");

            var user = await _userManager.FindByEmailAsync(payload.Email);
            if (user is null)
            {
                // First-time Google sign-in => account creation. Role is required here.
                if (req.Role is not RoleEnum.Technician and not RoleEnum.Client)
                    return Fail<LoginResponse>(
                        400,
                        "Le rôle (Technician ou Client) est requis pour créer un compte."
                    );

                if (!req.DataProcessingConsent || !req.PrivacyPolicyConsent)
                    return Fail<LoginResponse>(400, "Le consentement est obligatoire.");

                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    user = new UserApp
                    {
                        UserName = payload.Email,
                        Email = payload.Email,
                        FirstName = payload.GivenName ?? string.Empty,
                        LastName = payload.FamilyName ?? string.Empty,
                        ImgUrl = payload.Picture,
                        AuthProvider = AuthProviderEnum.Google,
                        ProviderKey = payload.Subject,
                        DataProcessingConsent = true,
                        PrivacyPolicyConsent = true,
                        EmailConfirmed = true, // Google already verified the address
                        StatusId = HardCode.ACCOUNT_ACTIVE,
                    };

                    var createResult = await _userManager.CreateAsync(user);
                    if (!createResult.Succeeded)
                    {
                        await transaction.RollbackAsync();
                        return Fail<LoginResponse>(400, DescribeErrors(createResult));
                    }

                    var roleResult = await _userManager.AddToRoleAsync(user, RoleName(req.Role!.Value));
                    if (!roleResult.Succeeded)
                    {
                        await transaction.RollbackAsync();
                        return Fail<LoginResponse>(400, DescribeErrors(roleResult));
                    }

                    //await CreateProfile(user, req.Role.Value);
                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Google signup failed for {Email}", payload.Email);
                    return Fail<LoginResponse>(500, "La création du compte Google a échoué.");
                }
            }
            else if (user.StatusId == HardCode.ACCOUNT_BANNED)
            {
                return Fail<LoginResponse>(403, "Ce compte est suspendu.");
            }
            else if (string.IsNullOrEmpty(user.ProviderKey))
            {
                // Link Google to an existing local account with the same verified email.
                user.ProviderKey = payload.Subject;
                if (!user.EmailConfirmed)
                    user.EmailConfirmed = true;
                await _userManager.UpdateAsync(user);
            }

            return await IssueSession(user, response, "Connexion Google réussie.");
        }

        public async Task<Response<LoginResponse>> RefreshToken(
            string? refreshToken,
            HttpResponse response
        )
        {
            if (string.IsNullOrEmpty(refreshToken))
                return Fail<LoginResponse>(401, "Refresh token manquant.");

            var hash = SecurityTokens.Hash(refreshToken);
            var stored = await _context.RefreshTokens.FirstOrDefaultAsync(r =>
                r.TokenHash == hash && r.ExpirationDate > DateTimeOffset.UtcNow
            );

            if (stored is null)
                return Fail<LoginResponse>(401, "Token expiré ou invalide.");

            var user = await _context
                .Users.Include(u => u.Status)
                .FirstOrDefaultAsync(u => u.Id == stored.UserId);
            if (user is null)
                return Fail<LoginResponse>(401, "Utilisateur introuvable.");

            return await IssueSession(user, response, "Session renouvelée.");
        }

        /// <summary>Rotates the refresh token, sets the cookie, and returns a fresh access token.</summary>
        private async Task<Response<LoginResponse>> IssueSession(
            UserApp user,
            HttpResponse response,
            string message
        )
        {
            // FindByEmailAsync / CreateAsync don't load the Status navigation; hydrate it
            // so the returned UserDetails reports the account status consistently.
            user.Status ??= await _context.StatusAccounts.FindAsync(user.StatusId);

            var plaintext = await CreateOrRotateRefreshToken(user);
            SetRefreshCookie(response, plaintext);

            return new Response<LoginResponse>
            {
                Status = 200,
                Message = message,
                Data = new LoginResponse
                {
                    Token = await GenerateAccessTokenAsync(user),
                    RefreshToken = plaintext,
                    User = new UserDetails(user, await GetRoleDetails(user)),
                },
            };
        }

        private async Task<string> CreateOrRotateRefreshToken(UserApp user)
        {
            var plaintext = SecurityTokens.GenerateOpaqueToken();
            var expires = DateTimeOffset.UtcNow.AddDays(EnvironmentVariables.COOKIES_VALIDITY_DAYS);

            var existing = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.UserId == user.Id);
            if (existing is null)
            {
                _context.RefreshTokens.Add(
                    new RefreshToken
                    {
                        Id = Guid.NewGuid(),
                        UserId = user.Id,
                        TokenHash = SecurityTokens.Hash(plaintext),
                        ExpirationDate = expires,
                    }
                );
            }
            else
            {
                existing.TokenHash = SecurityTokens.Hash(plaintext);
                existing.ExpirationDate = expires;
            }

            await _context.SaveChangesAsync();
            return plaintext;
        }

        private void SetRefreshCookie(HttpResponse response, string token)
        {
            // Prod: Secure + SameSite=None so the SPA on another origin can send it on
            // credentialed (HTTPS) requests. Dev over plain HTTP: a Secure/None cookie is
            // never stored by the browser, so relax to non-Secure + SameSite=Lax (still
            // sent for same-site localhost:4200 -> localhost:5275 requests).
            var isDev = _web.IsDevelopment();
            response.Cookies.Append(
                "refreshToken",
                token,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = !isDev,
                    SameSite = isDev ? SameSiteMode.Lax : SameSiteMode.None,
                    Path = "/",
                    Expires = DateTimeOffset.UtcNow.AddDays(EnvironmentVariables.COOKIES_VALIDITY_DAYS),
                }
            );
        }

        // ---- Password reset ----

        public async Task<Response<object>> ForgotPassword(ForgotPasswordRequest req)
        {
            var user = await _userManager.FindByEmailAsync(req.Email);

            // Send only for accounts that actually have a password; always respond the
            // same way so callers can't probe which emails exist.
            if (user is not null && await _userManager.HasPasswordAsync(user))
            {
                try
                {
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                    var link =
                        $"{EnvironmentVariables.API_FRONT_URL}/auth/reset-password"
                        + $"?userId={user.Id}&token={HttpUtility.UrlEncode(token)}";
                    await _mailService.SendResetPasswordAsync(user.Email, $"{user.FirstName} {user.LastName}".Trim(), link, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reset email failed for {Email}", req.Email);
                }
            }

            return new Response<object>
            {
                Status = 200,
                Message =
                    "Si un compte existe pour cet email, un lien de réinitialisation a été envoyé.",
            };
        }

        public async Task<Response<object>> ResetPassword(ResetPasswordRequest req)
        {
            var user = await _userManager.FindByIdAsync(req.UserId);
            if (user is null)
                return Fail<object>(400, "Réinitialisation impossible.");

            var result = await _userManager.ResetPasswordAsync(user, req.Token, req.Password);
            if (!result.Succeeded)
                return Fail<object>(400, "Le lien est invalide ou expiré.");

            // A successful reset proves control of the mailbox — activate if pending.
            if (!user.EmailConfirmed || user.StatusId == HardCode.ACCOUNT_PENDING)
            {
                user.EmailConfirmed = true;
                user.StatusId = HardCode.ACCOUNT_ACTIVE;
                await _userManager.UpdateAsync(user);
            }

            // Invalidate existing sessions by dropping the stored refresh token.
            var existing = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.UserId == user.Id);
            if (existing is not null)
            {
                _context.RefreshTokens.Remove(existing);
                await _context.SaveChangesAsync();
            }

            return new Response<object> { Status = 200, Message = "Mot de passe réinitialisé." };
        }

        // ---- Current user / logout ----

        public async Task<Response<UserInfoResponse>> GetMyInformations(ClaimsPrincipal principal)
        {
            var user = CheckUser.GetUserFromClaim(principal, _context);
            if (user is null)
                return Fail<UserInfoResponse>(401, "Vous n'êtes pas connecté.");

            return new Response<UserInfoResponse>
            {
                Status = 200,
                Message = "Demande acceptée.",
                Data = new UserInfoResponse
                {
                    Token = await GenerateAccessTokenAsync(user),
                    User = new UserDetails(user, await GetRoleDetails(user)),
                },
            };
        }

        public async Task<Response<object>> Logout(ClaimsPrincipal principal, HttpResponse response)
        {
            var user = CheckUser.GetUserFromClaim(principal, _context);
            if (user is not null)
            {
                var existing = await _context.RefreshTokens.FirstOrDefaultAsync(r =>
                    r.UserId == user.Id
                );
                if (existing is not null)
                {
                    _context.RefreshTokens.Remove(existing);
                    await _context.SaveChangesAsync();
                }
            }

            response.Cookies.Delete("refreshToken");
            return new Response<object> { Status = 200, Message = "Vous êtes déconnecté." };
        }

        // ---- JWT ----

        public async Task<string> GenerateAccessTokenAsync(UserApp user)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(EnvironmentVariables.JWT_KEY));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
        };

            foreach (var role in await _userManager.GetRolesAsync(user))
                claims.Add(new Claim(ClaimTypes.Role, role));

            var token = new JwtSecurityToken(
                issuer: EnvironmentVariables.API_BACK_URL,
                audience: EnvironmentVariables.API_BACK_URL,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(EnvironmentVariables.TOKEN_VALIDITY_MINUTES),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // ---- Helpers ----

        private async Task<List<RoleDetails>> GetRoleDetails(UserApp user)
        {
            var names = await _userManager.GetRolesAsync(user);
            return await _context
                .Roles.Where(r => r.Name != null && names.Contains(r.Name))
                .Select(r => new RoleDetails(r))
                .ToListAsync();
        }

        private async Task<string> BuildConfirmationLink(UserApp user)
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            return $"{EnvironmentVariables.API_BACK_URL}/auth/email-confirmation"
                + $"?userId={user.Id}&token={HttpUtility.UrlEncode(token)}";
        }

        private static string RoleName(RoleEnum role) =>
            role switch
            {
                RoleEnum.Technician => HardCode.ROLE_NAME_TECHNICIAN,
                RoleEnum.Client => HardCode.ROLE_NAME_CLIENT,
                RoleEnum.Admin => HardCode.ROLE_NAME_ADMIN,
                _ => HardCode.ROLE_NAME_SUPER_ADMIN,
            };

        private static string DescribeErrors(IdentityResult result) =>
            string.Join("; ", result.Errors.Select(e => e.Description));

        private static Response<T> Fail<T>(int status, string message) =>
            new() { Status = status, Message = message };
    }

}
