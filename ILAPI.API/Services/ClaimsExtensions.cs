using System.Security.Claims;

namespace ILAPI.API.Services;

public static class ClaimsExtensions
{
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue("sub") ?? throw new UnauthorizedAccessException());
}
