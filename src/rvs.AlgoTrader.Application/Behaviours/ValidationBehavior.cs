using FluentValidation;
using MediatR;

namespace rvs.AlgoTrader.Application.Behaviours;

/// <summary>
/// MediatR pipeline behavior that automatically runs all registered FluentValidation
/// validators for every command/query before the handler executes.
///
/// #130: Ensures no unvalidated input reaches business logic or the database.
///
/// Registration: AddBehavior&lt;IPipelineBehavior&lt;,&gt;, ValidationBehavior&lt;,&gt;&gt;()
///               in the MediatR configuration in Program.cs.
///
/// If no validator is registered for a request type, the pipeline passes through
/// without overhead (IEnumerable is empty).
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (!validators.Any())
            return await next();

        var context  = new ValidationContext<TRequest>(request);
        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}
