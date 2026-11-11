### Conventional Commits Cheat Sheet

A conventional commit message consists of a **type**, an optional **scope**, and a **description**:
`type(scope): brief description`

#### Key Prefixes & When to Use Them:

* **`feat`** (Feature)
* **When to use:** When adding a **new feature** or functionality to the application (e.g., adding a new API endpoint, a new database table, or a new command handler).
* *Example:* `feat(application): add CreateBooking command and handler`


* **`fix`** (Bug Fix)
* **When to use:** When fixing a **bug** or resolving an issue in the codebase.
* *Example:* `fix(infrastructure): correct database connection string parsing`


* **`docs`** (Documentation)
* **When to use:** When adding or updating documentation, such as the `README.md`, architectural notes, or code comments.
* *Example:* `docs: update README with local setup and architecture decisions`


* **`test`** (Tests)
* **When to use:** When adding missing tests, correcting existing tests, or refactoring test suites (unit/integration tests).
* *Example:* `test(integration): add CreateBookingCommand integration tests`


* **`refactor`** (Code Refactoring)
* **When to use:** When changing code structure (e.g., renaming variables, moving files, cleaning up architecture) that **neither fixes a bug nor adds a feature**.
* *Example:* `refactor(domain): extract validation logic into domain entity`


* **`style`** (Formatting / Styles)
* **When to use:** For code formatting changes (missing semicolons, white space, linting fixes) that do not affect the meaning or logic of the code.
* *Example:* `style: format C# files according to editorconfig rules`


* **`build`** (Build System / Dependencies)
* **When to use:** When making changes to the build system, external dependencies, NuGet packages, project files (`.csproj`), or configurations.
* *Example:* `build(webapi): add Microsoft.EntityFrameworkCore.Design package`


* **`chore`** (Maintenance)
* **When to use:** For general maintenance tasks, updating `.gitignore`, setting up CI/CD pipelines, or routine chores that don't modify source code or tests.
* *Example:* `chore: update .gitignore to exclude build artifacts`