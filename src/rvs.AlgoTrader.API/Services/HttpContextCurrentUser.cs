using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Services;

/// <summary>
/// Reads the authenticated user identity from the current HTTP context JWT claims.
/// Registered as Scoped. Falls back to "System" for background/scheduler invocations.
/// </summary>
public class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Actor
    {
        get
        {
            var user = accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true) return "System";
            return user.Identity.Name
                ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? "User";
        }
    }
}
