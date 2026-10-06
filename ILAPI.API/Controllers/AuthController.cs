using ILAPI.API.Services;
using ILAPI.Core;
using ILAPI.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ILAPI.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IUserRepository users, ITokenRepository tokens, TokenService tokenService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Email) || !r.Email.Contains('@') || string.IsNullOrWhiteSpace(r.FullName))
            return BadRequest(new { error = "Valid email and full name are required" });
        if (r.Password is null || r.Password.Length < 8)
            return BadRequest(new { error = "Password must be at least 8 characters" });
        var user = await users.CreateAsync(r.Email.Trim(), BCrypt.Net.BCrypt.HashPassword(r.Password), r.FullName.Trim(), r.Phone);
        if (user is null) return Conflict(new { error = "Email already registered" });
        return Ok(await IssueAsync(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest r)
    {
        var user = await users.FindByEmailAsync(r.Email?.Trim() ?? "");
        // same response for unknown user and bad password
        if (user is null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(r.Password ?? "", user.PasswordHash))
            return Unauthorized(new { error = "Invalid email or password" });
        return Ok(await IssueAsync(user));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest r)
    {
        var userId = await tokens.ConsumeRefreshAsync(TokenService.Hash(r.RefreshToken ?? ""));
        var user = userId is null ? null : await users.FindByIdAsync(userId.Value);
        if (user is null || !user.IsActive) return Unauthorized(new { error = "Invalid refresh token" });
        return Ok(await IssueAsync(user));
    }

    /// <summary>Creates a short-lived one-time token that another front end exchanges for a session.</summary>
    [Authorize]
    [HttpPost("handoff")]
    public async Task<ActionResult> CreateHandoff()
    {
        var token = TokenService.NewOpaqueToken();
        await tokens.StoreHandoffAsync(User.UserId(), TokenService.Hash(token), tokenService.HandoffExpiry());
        return Ok(new { token });
    }

    [HttpPost("handoff/exchange")]
    public async Task<ActionResult<AuthResponse>> ExchangeHandoff(HandoffExchangeRequest r)
    {
        var userId = await tokens.ConsumeHandoffAsync(TokenService.Hash(r.Token ?? ""));
        var user = userId is null ? null : await users.FindByIdAsync(userId.Value);
        if (user is null || !user.IsActive) return Unauthorized(new { error = "Invalid or expired handoff token" });
        return Ok(await IssueAsync(user));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserInfo>> Me()
    {
        var user = await users.FindByIdAsync(User.UserId());
        return user is null ? NotFound() : Ok(new UserInfo(user.Id, user.Email, user.FullName, user.Role));
    }

    private async Task<AuthResponse> IssueAsync(UserRecord user)
    {
        var (access, expires) = tokenService.CreateAccessToken(user);
        var refresh = TokenService.NewOpaqueToken();
        await tokens.StoreRefreshAsync(user.Id, TokenService.Hash(refresh), tokenService.RefreshExpiry());
        return new AuthResponse(access, refresh, expires, new UserInfo(user.Id, user.Email, user.FullName, user.Role));
    }
}
