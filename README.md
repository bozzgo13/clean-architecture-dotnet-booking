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

## 2. Security & Architectural Standards Compliance

The system strictly adheres to the **OWASP API Security Top 10 (2023)** standards and **Microsoft ASP.NET Core 10 Technological Best Practices**, ensuring enterprise-grade protection against critical vulnerabilities (such as BOLA, BFLA, mass assignment, and security misconfigurations) while leveraging native, high-performance .NET patterns.

### Core Security & Authentication Architecture
* **Cryptographic Password Hashing (PBKDF2):** Passwords are never stored in plain text. Hashing is abstracted behind `IPasswordHasher` in the Application layer and implemented via **ASP.NET Core Identity's** `PasswordHasher`. Utilizing per-hash random salts, high iteration counts, and version markers provides robust protection against rainbow table and brute-force attacks (OWASP API4:2023).
* **Stateless JWT Authentication & Claims Extraction:** Protected endpoints enforce `[Authorize]` using JWT Bearer tokens with fail-fast options validation (enforcing minimum 256-bit keys per OWASP API8:2023). Custom `ClaimsPrincipalExtensions` cleanly extract user identity and role claims (`GetUserId()`, `IsAdmin()`), keeping controllers thin.
* **Object-Level Authorization & BOLA/BFLA Prevention:** Endpoints strictly enforce authorization boundaries (e.g., admin-only routes restricted via `[Authorize(Roles = "Admin")]`), and resource ownership is derived exclusively from the authenticated principal rather than client input.
* **Mass Assignment Protection:** Incoming payloads use explicit DTOs (`CreateBookingRequest`) that intentionally omit sensitive or privileged fields (such as `UserId` or `Role`), preventing overposting vulnerabilities (OWASP API3:2023).
* **Secure Error Handling & Information Disclosure Prevention:** A global exception handler (`IExceptionHandler`) maps domain/application exceptions to standardized RFC 7807 `ProblemDetails` responses with unique `traceId` correlation, ensuring unexpected errors never leak internal stack traces or database details (OWASP API8:2023).
* **Database-Level Uniqueness & Concurrency Protection:** User emails are normalized in the Domain layer and enforced via a unique database index (`IsUnique()`), with `DbUpdateException` handling to gracefully translate concurrent duplicate registration race conditions into `400 Bad Request` responses.

### Rich Domain Model & Domain-Driven Design (DDD)
* **Decision:** Embraced Domain-Driven Design (DDD) patterns by moving away from anemic domain models. Business logic, state transitions, and entity invariants are encapsulated directly within domain entities (e.g., `Booking`, `Resource`, `User`) with private setters and protective methods (e.g., `Cancel()`, `NormalizeEmail()`).
* **Why:** Prevents invalid entity states, keeps business rules centralized where they belong, and ensures that the domain layer remains pure and independent of infrastructure concerns.

### Vertical Slice Organization
* **Decision:** Organized application features around self-contained **Vertical Slices** (e.g., `CreateBooking`, `GetBookings`, `Register`) rather than grouping files strictly by technical role.
* **Why:** Keeps commands/queries, handlers, DTOs, and validators cohesive and close together. Adding or modifying a feature requires touching a single feature folder, reducing coupling across the codebase.

### Code-First Approach
* **Decision:** We adopted an Entity Framework Core **Code-First** approach to model database entities directly in C#.
* **Why:** Enables version-controlled database schema iterations using EF Core migrations while ensuring that the domain code remains the single source of truth.

### CQRS via MediatR
* **Decision:** Command Query Responsibility Segregation (CQRS) implemented through **MediatR**.
* **Why:** Decouples write operations (`CreateBookingCommand` etc) from read queries (`GetBookingsQuery`), making handlers focused, small, and highly testable.

### Fail-Fast Validation Strategy (FluentValidation)
* **Decision:** Implemented a **Fail-Fast** validation approach using **FluentValidation** rules on incoming commands and queries before any business logic or database operations are executed.
* **Why:** Validates data contracts, formatting (e.g., email syntax, password complexity), and required fields immediately at the application boundary. Invalid requests fail early with clear `400 Bad Request` errors, saving database round-trips and protecting the domain from corrupt inputs.

### Centralized Exception Handling (`ProblemDetails`)
* **Decision:** Implemented a global exception handler (`IExceptionHandler`) in the WebApi layer that intercepts domain and application exceptions (such as `ValidationException` or `UnauthorizedAccessException`) and maps them to standardized RFC 7807 `ProblemDetails` HTTP responses.
* **Why:** Eliminates repetitive `try-catch` boilerplate inside controllers and guarantees consistent, predictable, and secure error payloads across all endpoints without leaking internal stack traces.

### Direct EF Core Context Usage (`IApplicationDbContext`) in Handlers
* **Decision:** Handlers interact directly with an abstraction of the EF Core context (`IApplicationDbContext`) rather than wrapping EF Core behind explicit repository classes.
* **Why:** EF Core's `DbSet<T>` and `DbContext` already implement the Repository and Unit of Work patterns natively, avoiding redundant repository abstractions while preserving testability.

---

## 3. Challenges Encountered & Solutions

### A. Package Versioning Constraints & Security Vulnerability Mitigation (.NET 10 Compatibility)
* **Problem 1 (Version Incompatibility):** When attempting to install packages like `Microsoft.EntityFrameworkCore.SqlServer`, NuGet threw a version mismatch error because the latest package versions targeted newer runtimes (e.g., .NET 11) rather than the project's target framework (`net10.0`).
* **Problem 2 (Known Vulnerabilities):** Transitive or direct dependencies (such as `System.Security.Cryptography.Pkcs` or `System.Security.Cryptography.Xml`) occasionally trigger security warnings (e.g., moderate/high severity vulnerabilities) in older minor/patch releases.
* **Solution:** 
  * Explicitly pin dependencies to **.NET 10-compatible versions** (e.g., matching the `10.x.x` major version line) to prevent automatic roll-forward or installation of incompatible framework versions.
  * Proactively update NuGet packages to their latest stable patch versions within the same major version to eliminate known security advisories and dependency warnings, ensuring a clean, secure build pipeline.

### B. Securing Connection Strings (Avoiding Hardcoded Secrets)
* **Problem:** Committing actual database connection strings with credentials into public or shared Git repositories presents security risks.
* **Solution:** Used `appsettings.Development.json` for local development settings (with `TrustServerCertificate=True`) and excluded local secret files from source control via `.gitignore`. Production deployment reads connection strings securely from Environment Variables or Azure Key Vault.

### C. Replacing Swagger with Scalar for API Documentation
* **Problem:** Swagger UI / Swashbuckle is no longer included or maintained by default in newer .NET Web API templates.
* **Solution:** Integrated **Scalar** (`Scalar.AspNetCore`) for modern, interactive API documentation. Configured `launchSettings.json` so that launching the Web API in the `Development` environment automatically opens the Scalar API documentation page in the browser (`/scalar/v1`).

### D. Refresh Token Concurrency Control (Preventing Double-Spend / Race Conditions)
* **Problem:** When handling concurrent HTTP requests using the same active refresh token (e.g., due to client retries or race conditions), standard database reads and updates can allow multiple requests to pass validation simultaneously and issue successor tokens, defeating the single-use guarantee of token rotation.
* **Solution:** Implemented **optimistic concurrency control** on refresh token redemption. By enforcing a concurrency check (e.g., tracking row versions or state changes in a single atomic transaction), the database guarantees that only the first redemption wins, while subsequent concurrent attempts to use the same token are safely rejected.

### E. Strict Breach Detection vs. Network Resiliency in Token Rotation
* **Problem:** Replaying an already-revoked refresh token indicates a potential token theft attempt (token reuse attack). However, aggressive invalidation policies can penalize legitimate clients experiencing network retries.
* **Solution:** Adopted a **strict breach response** policy. Any replay of an already-revoked refresh token immediately triggers a security lockdown, mass-revoking all active sessions for that user. This prioritizes zero-tolerance security over grace-window leniency, aligning with strict OAuth 2.0 Security BCP recommendations.

### F. Complete Session Lifecycle Management (Logout & Background Cleanup)
* **Problem:** Allowing refresh tokens to accumulate indefinitely without an explicit revocation path or row pruning leads to database bloat and lingering active sessions after a user leaves.
* **Solution:** 
  * Added a dedicated, rate-limited `POST /api/v1/auth/logout` endpoint for explicit, secure user session termination.
  * Implemented a scheduled background service (`IHostedService`) to periodically purge expired and revoked refresh tokens, maintaining database hygiene and bounded table growth.

### G. Bootstrapping the First Administrator Securely (The Chicken-and-Egg Problem)
* **Problem:** In a freshly deployed environment with an empty database, no administrator account exists. Relying on public registration endpoints creates severe security vulnerabilities (privilege escalation), while hardcoding credentials in configuration files or database migrations introduces high security risks and non-deterministic password hashing issues.
* **Solution:** Implemented a secure, opt-in **Application-layer bootstrap command** paired with a **startup initializer (`IHostedService`)**:
* **Isolated Command:** Created a dedicated `BootstrapAdminUserCommand` separate from public user registration to strictly prevent unauthorized role self-assignment.
* **Cryptographically Secure Credentials:** When enabled (`AdminBootstrap__Enabled=true`), a high-entropy one-time password is generated dynamically in memory via a CSPRNG and printed to standard output on first run, ensuring secrets never reside in source control or configuration files.
* **Idempotency & Concurrency Safety:** The handler safely checks for existing administrators and gracefully handles multi-replica database races using unique email constraints, making it completely safe to execute on every startup.

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
