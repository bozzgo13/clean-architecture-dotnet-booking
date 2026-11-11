using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MyBookingApi.WebApi.Middlewares;

/// <summary>
/// Global exception handler implementing the built-in .NET IExceptionHandler pattern 
/// to gracefully catch, log, and translate unhandled exceptions into standardized HTTP responses.
/// </summary>
public class CustomExceptionHandler : IExceptionHandler
{
    private readonly ILogger<CustomExceptionHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomExceptionHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger instance for recording exception telemetry.</param>
    public CustomExceptionHandler(ILogger<CustomExceptionHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Attempts to asynchronously handle the encountered exception during the request pipeline.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="exception">The exception that was thrown during request processing.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be canceled.</param>
    /// <returns>True if the exception was handled successfully; otherwise, false.</returns>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Log the exception details with appropriate severity level
        _logger.LogError(
            exception,
            "An unhandled exception occurred during request processing: {Message}",
            exception.Message);

        // Map the exception type to corresponding ProblemDetails payload and HTTP status code
        var problemDetails = exception switch
        {
            ValidationException validationException => CreateValidationProblemDetails(validationException),
            _ => CreateInternalServerProblemDetails(exception)
        };

        // Set response metadata
        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";

        // Write the structured problem details response as JSON
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        // Return true to signal that the exception has been fully handled and short-circuit the pipeline
        return true;
    }

    /// <summary>
    /// Creates a standardized ValidationProblemDetails payload for FluentValidation errors.
    /// </summary>
    private static ValidationProblemDetails CreateValidationProblemDetails(ValidationException exception)
    {
        // Group validation errors by property name to match standard ASP.NET Core validation error formats
        var errors = exception.Errors
            .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
            .ToDictionary(failureGroup => failureGroup.Key, failureGroup => failureGroup.ToArray());

        return new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation Failed",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            Detail = "One or more validation errors occurred during request processing."
        };
    }

    /// <summary>
    /// Creates a generic ProblemDetails payload for unexpected internal server errors.
    /// </summary>
    private static ProblemDetails CreateInternalServerProblemDetails(Exception exception)
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Server Error",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            Detail = "An unexpected error occurred while processing your request. Please try again later."
        };
    }
}