using System.Text.Json;
using FluentValidation;
using rvs.AlgoTrader.Application.DTOs.Common;

namespace rvs.AlgoTrader.API.Middleware;

public class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path}",
                context.Request.Method, context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/json";
        // #130: ValidationException → 422 Unprocessable Entity with field-level error messages.
        var (statusCode, message) = ex switch
        {
            ValidationException ve  => (422, string.Join("; ", ve.Errors.Select(e => e.ErrorMessage))),
            InvalidOperationException => (400, ex.Message),
            UnauthorizedAccessException => (401, "Unauthorized"),
            KeyNotFoundException => (404, ex.Message),
            _ => (500, "An unexpected error occurred")
        };
        context.Response.StatusCode = statusCode;
        var response = ApiResponse<object>.Fail(message);
        await context.Response.WriteAsync(JsonSerializer.Serialize(response,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
