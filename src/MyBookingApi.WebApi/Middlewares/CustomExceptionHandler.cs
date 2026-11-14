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
        // The trace identifier lets an operator correlate the client-visible error with the
        // detailed server-side log entry without exposing any internal detail in the response.
        var traceId = httpContext.TraceIdentifier;

        // Map the exception type to corresponding ProblemDetails payload and HTTP status code.
        var problemDetails = exception switch
        {
            ValidationException validationException => CreateValidationProblemDetails(validationException),
            UnauthorizedAccessException => CreateUnauthorizedProblemDetails(),
            _ => CreateInternalServerProblemDetails()
        };

        LogException(exception, problemDetails.Status, traceId);

        // Expose the correlation trace id to the caller (RFC 7807 / Problem Details for HTTP APIs) so support staff can
        // tie a reported error back to the secure server-side log, without leaking any internals.
        problemDetails.Extensions["traceId"] = traceId;

        // Set response status
        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        // Write the structured problem details response as RFC 7807 JSON. The content type is passed
        // explicitly because WriteAsJsonAsync would otherwise default to "application/json".
        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            options: null,
            contentType: "application/problem+json",
            cancellationToken);

        // Return true to signal that the exception has been fully handled and short-circuit the pipeline
        return true;
    }

    /// <summary>
    /// Records the exception with a severity that reflects whether the failure is an expected
    /// client-side error (4xx) or an unexpected server-side fault (5xx).
    /// </summary>
    /// <param name="exception">The exception that was thrown during request processing.</param>
    /// <param name="statusCode">The HTTP status code the exception was translated into.</param>
    /// <param name="traceId">The correlation identifier attached to the response.</param>
    private void LogException(Exception exception, int? statusCode, string traceId)
    {
        if (statusCode is >= StatusCodes.Status400BadRequest and < StatusCodes.Status500InternalServerError)
        {
            // Client errors (failed validation, missing credentials) are expected and are logged at
            // Warning without the full stack trace so they do not pollute error telemetry.
            _logger.LogWarning(
                "Request {TraceId} rejected with status {StatusCode}: {ExceptionType}",
                traceId,
                statusCode,
                exception.GetType().Name);

            return;
        }

        // Unexpected server faults keep the full exception detail for secure server-side diagnosis.
        _logger.LogError(
            exception,
            "Unhandled exception while processing request {TraceId}",
            traceId);
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
    /// Creates a standardized ProblemDetails payload for authentication failures
    /// (e.g. a missing or invalid user identifier claim).
    /// </summary>
    private static ProblemDetails CreateUnauthorizedProblemDetails()
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Unauthorized",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
            Detail = "The request could not be authenticated. Authentication credentials are missing or invalid."
        };
    }

    /// <summary>
    /// Creates a generic ProblemDetails payload for unexpected internal server errors.
    /// </summary>
    private static ProblemDetails CreateInternalServerProblemDetails()
    {
        // Intentionally generic to prevent information disclosure (OWASP API8:2023 - Security Misconfiguration 
        // and RFC 7807 ProblemDetails guidelines). No exception message, stack trace, SQL, or file paths 
        // are surfaced to the client; diagnostics are captured securely server-side.
        return new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Server Error",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            Detail = "An unexpected error occurred while processing your request. Please try again later."
        };
    }
}
