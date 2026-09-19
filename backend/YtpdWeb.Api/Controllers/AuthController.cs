using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using YtpdWeb.Api.Auth;
using YtpdWeb.Api.Models;

namespace YtpdWeb.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    IOptions<AuthCredentialsOptions> credentials,
    JwtService jwtService
) : ControllerBase
{
    [HttpPost("login")]
    public ActionResult<LoginResponse> Login(LoginRequest request)
    {
        var creds = credentials.Value;

        if (string.IsNullOrEmpty(creds.Username) || string.IsNullOrEmpty(creds.PasswordHash))
            return StatusCode(500, "Server auth is not configured. Set Auth__Username and Auth__PasswordHash.");

        if (!string.Equals(request.Username, creds.Username, StringComparison.Ordinal))
            return Unauthorized();

        if (!PasswordHasher.Verify(request.Password, creds.PasswordHash))
            return Unauthorized();

        var token = jwtService.IssueToken(creds.Username);
        return Ok(new LoginResponse(token, creds.Username));
    }
}
