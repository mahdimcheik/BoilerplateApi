using InteractivesApi.Models.Responses;
using InteractivesApi.Models.Users;
using InteractivesApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace InteractivesApi.Controllers
{
    [Produces("application/json")]
    [Route("[controller]")]
    [ApiController]
    [Authorize]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [AllowAnonymous]
        [HttpPost("register")]
        public async Task<ActionResult<Response<UserDetails>>> Register([FromBody] RegisterRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new Response<object> { Status = 400, Message = "Problème de validation." });

            var result = await _authService.Register(model, CancellationToken.None);
            return StatusCode(result.Status, result);
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<ActionResult<Response<LoginResponse>>> Login([FromBody] LoginRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new Response<object> { Status = 400, Message = "Problème de validation." });

            var result = await _authService.Login(model, HttpContext.Response);
            return StatusCode(result.Status, result);
        }

        [AllowAnonymous]
        [HttpPost("google")]
        public async Task<ActionResult<Response<LoginResponse>>> Google([FromBody] GoogleAuthRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new Response<object> { Status = 400, Message = "Problème de validation." });

            var result = await _authService.GoogleAuth(model, HttpContext.Response);
            return StatusCode(result.Status, result);
        }

        [AllowAnonymous]
        [HttpGet("email-confirmation")]
        public async Task<IActionResult> EmailConfirmation([FromQuery] string userId, [FromQuery] string token)
        {
            var result = await _authService.EmailConfirmation(userId, token);
            if (result.Status == 200)
                return Redirect(result.Message);

            return StatusCode(result.Status, result);
        }

        [AllowAnonymous]
        [HttpGet("refresh-token")]
        public async Task<ActionResult<Response<LoginResponse>>> RefreshToken()
        {
            Request.Cookies.TryGetValue("refreshToken", out var token);
            var result = await _authService.RefreshToken(token, HttpContext.Response);
            return StatusCode(result.Status, result);
        }

        [AllowAnonymous]
        [HttpPost("forgot-password")]
        public async Task<ActionResult<Response<object>>> ForgotPassword([FromBody] ForgotPasswordRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new Response<object> { Status = 400, Message = "Problème de validation." });

            var result = await _authService.ForgotPassword(model);
            return StatusCode(result.Status, result);
        }

        [AllowAnonymous]
        [HttpPost("reset-password")]
        public async Task<ActionResult<Response<object>>> ResetPassword([FromBody] ResetPasswordRequest model)
        {
            if (!ModelState.IsValid || model.Password != model.PasswordConfirmation)
                return BadRequest(new Response<object> { Status = 400, Message = "Les mots de passe ne correspondent pas." });

            var result = await _authService.ResetPassword(model);
            return StatusCode(result.Status, result);
        }

        [HttpGet("my-informations")]
        public async Task<ActionResult<Response<UserInfoResponse>>> GetMyInformations()
        {
            var result = await _authService.GetMyInformations(HttpContext.User);
            return StatusCode(result.Status, result);
        }

        [AllowAnonymous]
        [HttpGet("logout")]
        public async Task<ActionResult<Response<object>>> Logout()
        {
            var result = await _authService.Logout(HttpContext.User, HttpContext.Response);
            return StatusCode(result.Status, result);
        }
    }

}
