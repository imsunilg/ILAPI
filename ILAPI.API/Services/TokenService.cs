using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using ILAPI.Core;
using Microsoft.IdentityModel.Tokens;

namespace ILAPI.API.Services;

public class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "ILAPI";
    public string Audience { get; set; } = "ILPortal";
    public int AccessMinutes { get; set; } = 15;
    public int RefreshDays { get; set; } = 14;
    public int HandoffSeconds { get; set; } = 60;
}

public class TokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> options)
{
    private readonly JwtOptions _o = options.Value;

    public (string Token, DateTime ExpiresAt) CreateAccessToken(UserRecord u)
    {
        var expires = DateTime.UtcNow.AddMinutes(_o.AccessMinutes);
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Key)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(_o.Issuer, _o.Audience,
            [new("sub", u.Id.ToString()), new("email", u.Email), new("name", u.FullName), new("role", u.Role)],
            expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }

    public static string NewOpaqueToken() => Base64Url(RandomNumberGenerator.GetBytes(48));

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public DateTime RefreshExpiry() => DateTime.UtcNow.AddDays(_o.RefreshDays);
    public DateTime HandoffExpiry() => DateTime.UtcNow.AddSeconds(_o.HandoffSeconds);

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
