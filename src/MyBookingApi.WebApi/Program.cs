using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MyBookingApi.Application;
using MyBookingApi.Infrastructure;
using MyBookingApi.Infrastructure.Security;
using MyBookingApi.WebApi.HostedServices;
using MyBookingApi.WebApi.Middlewares;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// OWASP API4:2023 – Unrestricted Resource Consumption: cap the request body size at the server level so oversized payloads cannot exhaust memory.
// JSON API requests are small; 1 MB is generous for legitimate traffic and rejects abuse early.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1 * 1024 * 1024;
});

// Add services to the container.
// Register the custom exception handler and problem details infrastructure services in DI container
builder.Services.AddExceptionHandler<CustomExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Background maintenance (OWASP API4:2023 - Unrestricted Resource Consumption): a hosted service
// periodically prunes refresh tokens that expired or were revoked long ago, so the token store cannot
// grow without bound. Its schedule and retention are configuration-driven and validated at startup,
// and both fall back to safe defaults when the section is absent.
builder.Services.AddOptions<RefreshTokenCleanupOptions>()
    .Bind(builder.Configuration.GetSection(RefreshTokenCleanupOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHostedService<RefreshTokenCleanupService>();

// Fail-fast configuration validation (OWAST API8:2023 – Security Misconfiguration). Bind the "Jwt" section to a strongly-typed options
// object and validate it when the host starts: a missing issuer/audience or a signing key shorter
// than 256 bits (the HS256 minimum) aborts startup instead of allowing forgeable tokens.
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        options => Encoding.UTF8.GetByteCount(options.Key) >= 32,
        "Jwt:Key must be at least 256 bits (32 bytes).")
    .ValidateOnStart();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Configure the JWT bearer handler from the validated options. Only HMAC-SHA256 signed tokens are
// accepted and a signing key is mandatory, which blocks unsigned ("alg: none") and algorithm-
// substitution attacks (API2 - Broken Authentication).
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((jwtBearerOptions, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;

        jwtBearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // Structured audit logging (Section 4): record authentication failures and authorization
        // rejections without ever logging the token or the Authorization header.
        jwtBearerOptions.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("MyBookingApi.Authentication");

                // Only the exception type is recorded; the token itself is never written to the log.
                logger.LogWarning(
                    "JWT authentication failed. Reason={Reason}",
                    context.Exception.GetType().Name);

                return Task.CompletedTask;
            },
            OnForbidden = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("MyBookingApi.Authorization");

                logger.LogWarning(
                    "Authorization rejected (403). Method={Method} Path={Path}",
                    context.HttpContext.Request.Method,
                    context.HttpContext.Request.Path);

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Rate limiting (OWASP API4 - Unrestricted Resource Consumption). A generous global limiter protects every
// endpoint as a safety net, while the credential-issuing registration endpoint uses a much stricter
// named policy applied with [EnableRateLimiting].
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Structured audit logging (Section 4): record throttled requests (429) so abuse attempts are
    // observable, and keep the RFC 9110 status code expected by clients.
    options.OnRejected = (context, cancellationToken) =>
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("MyBookingApi.RateLimiting");

        logger.LogWarning(
            "Rate limit exceeded (429). Method={Method} Path={Path} ClientIp={ClientIp}",
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path,
            context.HttpContext.Connection.RemoteIpAddress);

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        return ValueTask.CompletedTask;
    };

    // Safety net for every request that does not carry an endpoint-specific policy.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    // Strict policy for the credential-issuing registration endpoint.
    options.AddPolicy("registration", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    // Strict policy for the login endpoint. Throttling credential submission per client slows down
    // password brute-force and account-enumeration attempts (OWASP API2:2023 - Broken Authentication).
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    // Strict policy for the token-refresh endpoint. Rotation is legitimate but infrequent, so a modest
    // per-client allowance protects the token store against credential-stuffing and replay attempts
    // while leaving ample headroom for normal session maintenance.
    options.AddPolicy("refresh", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    // Strict policy for the logout endpoint. Sign-out is legitimate but infrequent, so the same modest
    // per-client allowance as refresh protects the token store against abuse while leaving ample
    // headroom for normal session teardown.
    options.AddPolicy("logout", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

// OWASP API4: bound the total time a request may run so a slow or hung request cannot occupy a worker
// indefinitely (a resource-exhaustion vector). The default policy applies to every endpoint.
builder.Services.AddRequestTimeouts(options =>
{
    options.DefaultPolicy = new RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
});

// CORS (OWASP API8 - Security Misconfiguration): cross-origin access is denied by default and only the
// origins explicitly listed in configuration are allowed, so a wildcard ("*") policy can never be
// introduced accidentally.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddControllers();

// Register native .NET 10 OpenAPI services
builder.Services.AddOpenApi();

var app = builder.Build();

// Security headers applied to every response (including error responses) early in the pipeline.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    await next();
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Map native OpenAPI endpoint (generates /openapi/v1.json)
    app.MapOpenApi();

    // Map Scalar API reference UI (available at /scalar/v1)
    app.MapScalarApiReference(options =>
    {
        options.Title = "MyBookingApi Documentation";
        options.Theme = ScalarTheme.Default;
    });
}
else
{
    // HSTS and a strict Content-Security-Policy only make sense over HTTPS in non-development environments.

    // 'UseHsts' enforces HTTP Strict Transport Security, instructing the browser to communicate with the server exclusively via secure HTTPS connections in the future, preventing protocol downgrade and Man-in-the-Middle attacks.
    app.UseHsts(); 
    

    app.Use(async (context, next) =>
    {
        // Content-Security-Policy (CSP) header strictly dictates to the browser which resources 
        // and interactions are allowed, protecting the application against injection attacks.
        context.Response.Headers["Content-Security-Policy"] = 
            "default-src 'none'; " + // 'default-src 'none'' blocks the loading of all resources by default (scripts, images, stylesheets, objects, etc.). Since this is a pure Web API returning only JSON data, the browser has no need to load any external content.
            "frame-ancestors 'none'"; // 'frame-ancestors 'none'' prevents any external website from embedding your API inside iframe, frame, or object elements, acting as a crucial defense against Clickjacking attacks.
            
        await next();
    });
}

// Enable the exception handler middleware early in the HTTP request pipeline
// (Must be placed before routing, authentication, and controllers)
app.UseExceptionHandler();

app.UseHttpsRedirection();

// Routing must run before the rate limiter and CORS so that endpoint-specific policies are resolved.
app.UseRouting();

// Enforce the request timeout policy configured above (API4).
app.UseRequestTimeouts();

app.UseCors("Default");

// Apply the rate limiting middleware before authentication/authorization so abusive traffic is
// throttled as early as possible, including on anonymous endpoints such as registration.
app.UseRateLimiter();

// Authentication must run before authorization so that the caller's identity is resolved first.
app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the top-level Program entry point to the integration test project (WebApplicationFactory<Program>).
public partial class Program { }
