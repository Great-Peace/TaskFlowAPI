# TaskFlow API

A task and project management REST API built on .NET 8, with an automated quality
suite covering unit, architecture, integration, contract, end-to-end and mutation
testing.

- **[docs/TESTING_STRATEGY.md](docs/TESTING_STRATEGY.md)** — what is tested at each level and why
- **[docs/QUALITY_EVIDENCE.md](docs/QUALITY_EVIDENCE.md)** — measured results
- **[docs/DEPENDENCY_DECISIONS.md](docs/DEPENDENCY_DECISIONS.md)** — accepted dependency risks

---

## Getting started

### Prerequisites

- .NET SDK 8.0
- SQL Server — LocalDB is fine for running the API
- Docker — required only for the integration tests

### One-time setup: the JWT signing key

**The application will not start without one.** No signing key is committed: a key
in source control lets anyone with repository access forge a valid token for any
user. The key is supplied through
[user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets),
which are stored in your user profile, outside the repository.

```bash
dotnet user-secrets set "JwtSettings:SecretKey" "<at least 32 characters>" --project TaskFlowAPI
```

Any value of 32 characters or more works locally. In a deployed environment,
supply it through the environment or a secret store:

```bash
JwtSettings__SecretKey="<key>"
```

If it is missing, startup fails immediately and says so, rather than running with
a broken or absent key:

```
OptionsValidationException: DataAnnotation validation failed for 'JwtSettings'
members: 'SecretKey' with the error: 'The SecretKey field is required.'
```

### Run it

```bash
dotnet run --project TaskFlowAPI
```

Swagger UI is served at the root in Development: <http://localhost:5101>

---

## API

| Method | Route | Access |
|---|---|---|
| POST | `/api/Auth/register` | anonymous |
| POST | `/api/Auth/login` | anonymous |
| GET | `/api/Projects` | authenticated — the caller's own projects |
| POST | `/api/Projects` | authenticated |
| GET | `/api/Projects/{id}` | authenticated — owner only |
| PUT | `/api/Projects/{id}` | authenticated — owner only |
| DELETE | `/api/Projects/{id}` | authenticated — owner only |
| GET | `/api/Projects/all` | **Admin** role |

Routes that address a project by id answer `404` when the caller is not the owner,
so that "not yours" and "does not exist" are indistinguishable. Returning `403`
would confirm the project exists and let a caller enumerate other users' data by
probing ids.

---

## Tests

```bash
# fast, no Docker
dotnet test tests/TaskFlow.UnitTests
dotnet test tests/TaskFlow.ArchitectureTests

# needs Docker running - starts a disposable SQL Server container
dotnet test tests/TaskFlow.IntegrationTests

# everything
dotnet test TaskFlowAPI.sln
```

See [docs/TESTING_STRATEGY.md](docs/TESTING_STRATEGY.md) for coverage, mutation
testing and the contract-approval workflow.

---

## Project layout

```
TaskFlow.Core/             entities, DTOs, interfaces, AuthService - depends on nothing
TaskFlow.Infrastructure/   EF Core, repositories, unit of work - depends on Core
TaskFlowAPI/               controllers, middleware, mapping, startup

tests/
  TaskFlow.TestInfrastructure/   reusable test framework (no tests)
  TaskFlow.UnitTests/            isolated behaviour
  TaskFlow.ArchitectureTests/    dependency rules
  TaskFlow.IntegrationTests/     HTTP against a real SQL Server
```

The dependency direction is enforced by automated architecture tests, not just by
convention.
