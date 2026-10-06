using ILAPI.API.Services;
using ILAPI.Core;
using ILAPI.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ILAPI.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController(IUserRepository users, ITokenRepository tokens, TokenService tokenService, ILogger<AuthController> log) : ControllerBase
{
    private static ApiResponse<T> Ok<T>(string message, T data) => new(true, message, data);
    private static ApiResponse<object> Fail(string message) => new(false, message, null);

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Email) || !r.Email.Contains('@') || string.IsNullOrWhiteSpace(r.FullName))
            return BadRequest(Fail("Valid email and full name are required"));
        if (r.Password is null || r.Password.Length < 8)
            return BadRequest(Fail("Password must be at least 8 characters"));
        var user = await users.CreateAsync(r.Email.Trim(), BCrypt.Net.BCrypt.HashPassword(r.Password), r.FullName.Trim(), r.Phone);
        if (user is null) return Conflict(Fail("Email already registered"));
        return Ok(Ok("Registration successful", await IssueAsync(user)));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest r)
    {
        var user = await users.FindByLoginAsync(r.Username?.Trim() ?? "");
        // same response for unknown user and bad password; never log the password or tokens
        if (user is null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(r.Password ?? "", user.PasswordHash))
        {
            log.LogWarning("Failed login for {Username}", r.Username);
            return Unauthorized(Fail("Invalid username or password"));
        }
        return Ok(Ok("Login successful", await IssueAsync(user)));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest r)
    {
        var userId = await tokens.ConsumeRefreshAsync(TokenService.Hash(r.RefreshToken ?? ""));
        var user = userId is null ? null : await users.FindByIdAsync(userId.Value);
        if (user is null || !user.IsActive) return Unauthorized(Fail("Invalid refresh token"));
        return Ok(Ok("Token refreshed", await IssueAsync(user)));
    }

    /// <summary>Revokes the refresh token. The short-lived access token simply expires.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest r)
    {
        await tokens.ConsumeRefreshAsync(TokenService.Hash(r.RefreshToken ?? ""));
        return Ok(Ok<object?>("Logged out", null));
    }

    /// <summary>Creates a short-lived one-time token that another front end exchanges for a session.</summary>
    [Authorize]
    [HttpPost("handoff")]
    [HttpPost("~/api/v1/frontend-session")]
    public async Task<IActionResult> CreateHandoff()
    {
        var token = TokenService.NewOpaqueToken();
        await tokens.StoreHandoffAsync(User.UserId(), TokenService.Hash(token), tokenService.HandoffExpiry());
        return Ok(Ok("Handoff token created", new { token }));
    }

    [HttpPost("handoff/exchange")]
    public async Task<IActionResult> ExchangeHandoff(HandoffExchangeRequest r)
    {
        var userId = await tokens.ConsumeHandoffAsync(TokenService.Hash(r.Token ?? ""));
        var user = userId is null ? null : await users.FindByIdAsync(userId.Value);
        if (user is null || !user.IsActive) return Unauthorized(Fail("Invalid or expired handoff token"));
        return Ok(Ok("Login successful", await IssueAsync(user)));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await users.FindByIdAsync(User.UserId());
        if (user is null) return NotFound(Fail("User not found"));
        return Ok(Ok("OK", await InfoAsync(user)));
    }

    private async Task<UserInfo> InfoAsync(UserRecord u) =>
        new(u.Id, u.Username, u.FullName, u.Role, await users.GetPermissionsAsync(u.Id));

    private async Task<AuthData> IssueAsync(UserRecord user)
    {
        var info = await InfoAsync(user);
        var (access, expiresIn) = tokenService.CreateAccessToken(user, info.Permissions);
        var refresh = TokenService.NewOpaqueToken();
        await tokens.StoreRefreshAsync(user.Id, TokenService.Hash(refresh), tokenService.RefreshExpiry());
        return new AuthData(access, refresh, expiresIn, info);
    }
}
