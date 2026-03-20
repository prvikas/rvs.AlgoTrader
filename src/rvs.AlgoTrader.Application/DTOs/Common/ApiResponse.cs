namespace rvs.AlgoTrader.Application.DTOs.Common;

public record ApiResponse<T>(bool Success, T? Data, string? Error, string CorrelationId)
{
    /// <summary>Create a success response. Controllers that lack a correlationId can omit it.</summary>
    public static ApiResponse<T> Ok(T data, string correlationId = "") => new(true, data, null, correlationId);

    /// <summary>Create an error response. Controllers that lack a correlationId can omit it.</summary>
    public static ApiResponse<T> Fail(string error, string correlationId = "") => new(false, default, error, correlationId);
}

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string correlationId = "") => new(true, data, null, correlationId);
    public static ApiResponse<T> Fail<T>(string error, string correlationId = "") => new(false, default, error, correlationId);
}
