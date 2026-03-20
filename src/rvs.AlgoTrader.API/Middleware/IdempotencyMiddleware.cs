using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Middleware;

/// <summary>
/// Checks Idempotency-Key header on POST requests to prevent duplicate order submissions.
/// IIdempotencyService is injected per-request via InvokeAsync (not constructor) because
/// middleware is a singleton and cannot take scoped services in its constructor.
/// </summary>
public class IdempotencyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IIdempotencyService idempotency)
    {
        if (context.Request.Method == "POST" &&
            context.Request.Path.StartsWithSegments("/api/orders"))
        {
            var key = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
            var idempotencyResult = string.IsNullOrEmpty(key) ? null
                : await idempotency.CheckAsync(key, context.RequestAborted);
            if (idempotencyResult?.IsDuplicate == true)
            {
                context.Response.StatusCode = 200;
                await context.Response.WriteAsync("{\"status\":\"duplicate\",\"message\":\"Already processed\"}");
                return;
            }
        }
        await next(context);
    }
}
