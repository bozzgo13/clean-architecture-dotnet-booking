using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MyBookingApi.Application;
using MyBookingApi.Infrastructure;
using MyBookingApi.WebApi.Middlewares;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Register the custom exception handler and problem details infrastructure services in DI container
builder.Services.AddExceptionHandler<CustomExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Configure JWT bearer authentication. Issuer/audience/signing key are supplied through configuration
// (see the "Jwt" section) so that production secrets are read from environment variables or a vault.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? string.Empty))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddControllers();

// Register native .NET 10 OpenAPI services
builder.Services.AddOpenApi();

var app = builder.Build();

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

// Enable the exception handler middleware early in the HTTP request pipeline
// (Must be placed before routing, authentication, and controllers)
app.UseExceptionHandler();

app.UseHttpsRedirection();

// Authentication must run before authorization so that the caller's identity is resolved first.
app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the top-level Program entry point to the integration test project (WebApplicationFactory<Program>).
public partial class Program { }