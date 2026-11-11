using FluentValidation;
using MediatR;

namespace MyBookingApi.Application.Common.Behaviours;

/// <summary>
/// Instead of calling the validator manually in every handler, use a MediatR pipeline behavior
/// </summary>
/// <typeparam name="TRequest"></typeparam>
/// <typeparam name="TResponse"></typeparam>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Check if any validators are registered for the current request type
        if (_validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);

            // Execute all applicable validators concurrently to optimize performance
            var validationResults = await Task.WhenAll(
                _validators.Select(v => v.ValidateAsync(context, cancellationToken))
            );

            // Aggregate all validation error failures, filtering out null entries
            var failures = validationResults
                .SelectMany(r => r.Errors)
                .Where(f => f != null)
                .ToList();

            // If validation errors exist, abort pipeline execution and throw a validation exception
            if (failures.Count != 0)
            {
                throw new ValidationException(failures);
            }
        }

        // Proceed to the next behavior or target command/query handler in the pipeline
        return await next();
    }
}