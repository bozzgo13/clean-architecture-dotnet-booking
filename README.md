# MyBookingApi - Architectural & Development Overview

This document outlines the core architectural decisions, design patterns, problem-solving milestones, and setup instructions for the **MyBookingApi** resource booking system built with .NET 10.

---

## 1. Clean Architecture Design Principles & Implementation

The core requirement for this project was to implement a **Clean Architecture Design**. Clean Architecture organizes software into concentric layers to separate core business logic from external delivery mechanisms and infrastructure concerns.

We addressed these requirements through the following foundational principles:

* **Strict Separation of Concerns (Separation of Concerns):**
  The project is split into four distinct layers:
  * `MyBookingApi.Domain`: Core domain entities, value objects, and pure business logic with zero external dependencies.
  * `MyBookingApi.Application`: Use cases, CQRS commands/queries, MediatR handlers, and application interface definitions.
  * `MyBookingApi.Infrastructure`: Database implementation (EF Core), external service integrations, and persistence configurations.
  * `MyBookingApi.WebApi`: API controllers/endpoints, middleware, and entry-point configurations.

* **Independence of Frameworks & Database (Framework & UI Independence):**
  The business logic in the `Domain` and `Application` layers does not depend on SQL Server, EF Core, or Web API frameworks. Database engines or UI frameworks can be swapped without touching domain logic.

* **High Testability (Testability):**
  Because business logic is fully isolated, unit tests for commands, handlers, and validation can run quickly without initializing a real database or starting an HTTP server.

* **Dependency Inversion Principle (DIP):**
  High-level modules (`Application`) do not depend on low-level modules (`Infrastructure`). Both depend on abstractions (`IApplicationDbContext`). Dependencies flow inwards toward the `Domain`.

---

## 2. Key Architectural Decisions & Patterns

### Rich Domain Model
* **Decision:** Business logic and entity invariants are encapsulated directly within domain entities (e.g., `Booking`, `Resource`) rather than placing them in anemic data DTOs or service layers.
* **Why:** Prevents invalid entity states and maintains domain integrity across all use cases.

### Code-First Approach
* **Decision:** We adopted an Entity Framework Core **Code-First** approach to model database entities directly in C#.
* **Why:** Enables version-controlled database schema iterations using EF Core migrations while ensuring that the domain code remains the single source of truth.

### CQRS via MediatR
* **Decision:** Command Query Responsibility Segregation (CQRS) implemented through **MediatR**.
* **Why:** Decouples write operations (`CreateBookingCommand`) from read queries, making handlers focused, small, and highly testable.

### Direct EF Core Context Usage (`IApplicationDbContext`) in Handlers
* **Decision:** Handlers interact directly with an abstraction of the EF Core context (`IApplicationDbContext`) rather than wrapping EF Core behind explicit repository classes.
* **Why:** EF Core's `DbSet<T>` and `DbContext` already implement the Repository and Unit of Work patterns natively, avoiding redundant repository abstractions while preserving testability.

---

## 3. Challenges Encountered & Solutions

### A. Package Versioning Constraints (.NET 10 Compatibility)
* **Problem:** When attempting to install `Microsoft.EntityFrameworkCore.SqlServer`, NuGet threw a version mismatch error:
  > `Package Microsoft.EntityFrameworkCore.SqlServer 11.0.0 is not compatible with net10.0 (.NETCoreApp,Version=v10.0). Package Microsoft.EntityFrameworkCore.SqlServer 11.0.0 supports: net11.0 (.NETCoreApp,Version=v11.0)`
* **Solution:** Because the project targets **.NET 10**, dependencies across EF Core and third-party packages must be explicitly pinned to `.NET 10`-compatible versions (e.g., `10.x.x`) instead of automatically pulling latest major releases.

### B. Securing Connection Strings (Avoiding Hardcoded Secrets)
* **Problem:** Committing actual database connection strings with credentials into public or shared Git repositories presents security risks.
* **Solution:** Used `appsettings.Development.json` for local development settings (with `TrustServerCertificate=True`) and excluded local secret files from source control via `.gitignore`. Production deployment reads connection strings securely from Environment Variables or Azure Key Vault.

### C. Replacing Swagger with Scalar for API Documentation
* **Problem:** Swagger UI / Swashbuckle is no longer included or maintained by default in newer .NET Web API templates.
* **Solution:** Integrated **Scalar** (`Scalar.AspNetCore`) for modern, interactive API documentation. Configured `launchSettings.json` so that launching the Web API in the `Development` environment automatically opens the Scalar API documentation page in the browser (`/scalar/v1`).

---

## 4. Local Database Setup & Migration Instructions

Follow these steps to set up and initialize the local SQL Server database using EF Core Migrations.

### Step 1: Install EF Core Global Tool
If you do not have the EF Core command-line tool installed, install it globally:
```bash
dotnet tool install --global dotnet-ef
```
*(If already installed, update it using `dotnet tool update --global dotnet-ef`).*

### Step 2: Create Database Migration
Generate an initial database migration from the root directory of the solution:
```bash
dotnet ef migrations add InitialCreate --project src/MyBookingApi.Infrastructure --startup-project src/MyBookingApi.WebApi
```

### Step 3: Apply Migration to Local Database
Apply the migration to create the SQL Server database and tables:
```bash
dotnet ef database update --project src/MyBookingApi.Infrastructure --startup-project src/MyBookingApi.WebApi
```

### Step 4: Run the Application
Run the Web API project:
```bash
dotnet run --project src/MyBookingApi.WebApi
```
When running in `Development` mode, the Scalar API documentation interface will automatically launch in your browser.