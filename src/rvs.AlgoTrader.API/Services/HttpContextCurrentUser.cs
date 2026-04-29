using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Services;

/// <summary>
/// Reads the authenticated user identity from the current HTTP context JWT claims.
/// Registered as Scoped. Falls back to system values when no HTTP context is present
/// (background jobs, startup orchestrator).
/// JWT shape: sub = user UUID | name = username | role = role
/// </summary>
public class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    // Well-known system user UUID — matches the seed row in migration 050.
    private const string SystemUserId = "00000000-0000-0000-0000-000000000001";

    public string Actor
    {
        get
        {
            var user = accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true) return "System";
            return user.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                ?? user.Identity.Name
                ?? "User";
        }
    }

    public string UserId
    {
        get
        {
            var user = accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true) return SystemUserId;
            // sub claim carries the user UUID written by AuthController.GenerateJwtToken
            return user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? SystemUserId;
        }
    }
}
