using MyBookingApi.Application;
using MyBookingApi.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();