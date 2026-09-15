# Quality evidence

Measured results for the TaskFlow API test suite. Every figure here was produced
by running the thing it describes. Where something could not be measured, it says
so rather than estimating.

**Measured:** 15 September 2026
**Machine:** Windows 11, 12 logical cores, Docker Desktop on WSL2 (7.58 GB available to the daemon)
**Toolchain:** .NET SDK 8.0.303, ASP.NET Core runtime 8.0.7
**Commit:** branch `quality-engineering`

---

## Baseline, before this work

Measured on the original `master` before any change:

| | |
|---|---|
| Test projects | **0** |
| Tests | **0** |
| `dotnet test` exit code | **0** — passed with zero tests, a silent CI trap |
| Code coverage | Not measurable |
| Build | Succeeded, Release, 3.54s, 16 warnings |
| `dotnet format --verify-no-changes` | **3 violations** |
| CI pipeline | None |
| API operations | 5 |
| Application start | **Failed** in Development on this machine |

---

## Automated checks now

| Suite | Count |
|---|---|
| Unit tests | **111** |
| Architecture tests | **7** |
| Integration and API tests | **62** (54 endpoint + 8 framework smoke) |
| Contract tests | **29** (26 comparer rules + 3 against the live contract) |
| End-to-end scenarios | **1** |
| **Total automated checks** | **184** |

Contract rule tests live in the unit project because they need no database; the
three that compare against the running application's contract live in the
integration project. The 184 total counts each test once.

### Unit tests by area

| Area | Count |
|---|---|
| `AuthService` and `Pbkdf2PasswordHasher` | 31 |
| Contract comparer rules | 26 |
| `JwtSettings` validation | 20 |
| Diagnostics and redaction | 18 |
| `GlobalExceptionMiddleware` | 9 |
| `MappingProfile` | 7 |

### Integration tests by area

| Area | Count |
|---|---|
| Projects endpoints (list, get, create, validation, authorization) | 22 |
| Auth endpoints (register, login, validation, enumeration) | 20 |
| Update, delete and role-based authorization | 20 |
| Framework smoke tests | 8 |
| Contract | 3 |
| End-to-end journey | 1 |

---

## Execution time

Measured over three consecutive runs each. "Wall clock" is the full `dotnet test`
invocation including host startup; "test execution" is what the runner reports.

| Suite | Wall clock | Test execution | Docker |
|---|---|---|---|
| Unit | 3.6 – 4.8 s | 0.46 – 0.86 s | no |
| Architecture | 3.1 – 4.3 s | 0.06 – 0.11 s | no |
| Integration | 38.8 – 51.5 s | 7 – 9 s | yes |
| Mutation analysis | 1 m 57 s | — | no |

The gap between wall clock and test execution in the integration suite is SQL
Server container startup, around 25 seconds. The tests themselves are a small
fraction of the run.

**Full solution run:** roughly 45–60 seconds with Docker already running and the
SQL Server image already pulled. The first run on a clean machine additionally
pulls a 1.69 GB image.

---

## Code coverage

Collected across all three suites with `coverlet.runsettings`, merged with
ReportGenerator.

| Metric | Value |
|---|---|
| **Line coverage** | **78.8%** (586 of 743) |
| **Branch coverage** | **79.5%** (35 of 44) |
| **Method coverage** | **64.3%** (132 of 205) |

### By assembly

| Assembly | Line coverage |
|---|---|
| `TaskFlowAPI` | **97.2%** |
| `TaskFlow.Core` | 75.6% |
| `TaskFlow.Infrastructure` | 49.1% |

### The distribution matters more than the total

Everything reachable through the API is fully covered:

| Class | Coverage |
|---|---|
| `AuthService` | **100%** |
| `Pbkdf2PasswordHasher` | **100%** |
| `ProjectsController` | **100%** |
| `AuthController` | **100%** |
| `GlobalExceptionMiddleware` | **100%** |
| `MappingProfile` | **100%** |
| `ProjectRepository` | **100%** |
| `UserRepository` | **100%** |
| `ApplicationDbContext` | **100%** |
| `RequestLoggingMiddleware` | **100%** |
| `Program` | 91.8% |

What pulls the total down is code with no caller:

| Class | Coverage | Why |
|---|---|---|
| `TaskRepository` | **2.7%** | Fully implemented; no task endpoints exist |
| Seven DTOs | **0%** | `CreateTaskDto`, `UpdateTaskDto`, `UserDto`, `UserProfileDto`, `ChangePasswordDto`, `CreateUserDto`, `PagedResultDto<T>` — referenced by nothing |
| `UnitOfWork` | 50% | Transaction methods are never called |
| `GenericRepository<T>` | 55% | Several methods have no caller |

**No coverage threshold is enforced.** It would be satisfiable by writing tests
for unused DTOs, which raises the number and proves nothing. The honest way to
raise this figure is to delete the dead code or expose the endpoints it was
written for.

---

## Mutation score

Stryker.NET 4.16.0 over `TaskFlow.Core`'s services and configuration, run against
the unit suite.

| | Baseline | After improvements |
|---|---|---|
| Killed | 32 | **36** |
| Survived | 5 | **1** |
| Scored mutants | 37 | 37 |
| **Score** | **86.5%** | **97.3%** |
| Timeouts | 6 (verified by hand) | **0** |

### The baseline figure required verification

The first run *reported* 97.3%, with six mutants classified as `Timeout`. Stryker
counts a timeout as a kill. Applying all six by hand and running the suite gave:

| Mutant | Stryker | Verified |
|---|---|---|
| `AuthService:102` negate duplicate-email check | Timeout | **Killed** — 5 tests failed |
| `AuthService:117` remove `AddAsync` | Timeout | **Killed** — suite failed |
| `AuthService:82` rejection message → `""` | Timeout | **Survived** — suite passed |
| `JwtSettings:31` `SecretKey` default | Timeout | **Survived** — suite passed |
| `JwtSettings:35` `Issuer` default | Timeout | **Survived** — suite passed |
| `JwtSettings:39` `Audience` default | Timeout | **Survived** — suite passed |

Four of six were undetected mutants counted as kills, so the honest baseline was
**32/37 = 86.5%**. Later runs on an unloaded machine produced no timeouts at all,
confirming the classification was an artefact of machine load.

### Improvements made from the findings

**1. The login rejection message was never asserted.** Emptying
`"Invalid email or password"` killed no test: the suite asserted the exception
type, and that both failure paths produced the *same* message, but never that the
message said anything. A blank rejection reason would have shipped. Three tests
now pin the message.

**2. `JwtSettings` validation was entirely untested.**
`ValidateDataAnnotations().ValidateOnStart()` had been added specifically so a
missing or too-short signing key stops the application at startup — and nothing
verified it. Added 20 tests covering required settings, the 32-character key
minimum and its exact boundary, the expiry range and its default, and an absent
section.

One of those tests was then strengthened again by mutation testing: asserting only
that *something* threw left the `SecretKey` default mutant alive, because a
17-character replacement failed `MinLength` in place of `Required`. The test now
asserts the operator is told the field is *missing*, not that it is the wrong
length — better diagnostics, and it kills the mutant.

### The remaining survivor is equivalent

`Pbkdf2PasswordHasher.cs:55`, `||` → `&&` in the malformed-hash guard. For a
valid-Base64-but-wrong-length hash the mutated version skips the early return,
then computes PBKDF2 against a zero-padded buffer and the comparison returns
`false` anyway. The observable result is identical; only a timing assertion could
distinguish them, which would be a worse test. **Not chased.**

---

## Contract protection: demonstrated

Three controlled breaking changes were introduced, each caught, each reverted and
verified byte-identical to the committed version.

**1. Removing `[Authorize(Roles = "Admin")]` from the administrator endpoint:**

```
1 breaking change(s):
  BREAKING  [role-requirement-removed]  GET /api/Projects/all: the 'Admin' role is no longer required
```

**2 and 3. Removing a documented 404, and making an optional field required:**

```
2 breaking change(s):
  BREAKING  [response-removed]  GET /api/Projects/{id}: response 404 is no longer documented
  BREAKING  [property-now-required]  CreateProjectDto.status: an optional property became required
```

A fourth attempt — renaming `ProjectDto.CreatedByName` — was caught by the
compiler before the contract check could run, which is a fine outcome but not
evidence about the contract check.

**Additive changes pass**, as designed: the 26 comparer unit tests assert each rule
in both directions, so the check cannot silently classify everything as safe.

---

## Architecture rules: demonstrated

Injecting an `ApplicationDbContext` field into `AuthController` produced:

```
Architecture rule violated: Controllers must not depend on ApplicationDbContext directly
Failed!  - Failed: 1, Passed: 6, Total: 7
```

Reverted afterwards. This matters because the project references already enforce
some of these rules, so without the demonstration it would be unclear whether the
tests could fail at all.

---

## Failure diagnostics: demonstrated

A simulated defect — dereferencing an unloaded navigation property in
`CreateProject` — was injected to see what a developer actually gets. The output,
in order:

```
Expected 201 Created but got 500 InternalServerError.
Request : POST http://localhost/api/projects
Response: {"message":"An internal server error occurred","errors":null}

===== HTTP exchanges =====
--> POST http://localhost/api/auth/login
    body: {"email":"alice@taskflow.test","password":"***REDACTED***"}
<-- 200 (313ms)
    body: {"token":"***REDACTED***","userId":1,...}
--> POST http://localhost/api/projects
    body: {"name":"Apollo","description":"Moon landing",...}
<-- 500 (423ms)

===== warnings and errors =====
[00:01:51.266 ERROR] An unhandled exception occurred
System.NullReferenceException: Object reference not set to an instance of an object.
   at TaskFlow.API.Controllers.ProjectsController.CreateProject(...) in ...\ProjectsController.cs:line 117
```

The exception, with file and line, is reachable without a debugger. Credentials
and tokens are redacted.

**This ordering was a fix, not the original design.** Rendering the log
chronologically buried the exception beneath roughly forty lines of EF Core
command logging. Diagnostics now lead with warnings and errors.

---

## Flakiness

| Check | Runs | Result |
|---|---|---|
| Integration suite, consecutive | 3 | 66/66 every run |
| End-to-end scenario, consecutive | 3 | 1/1 every run |
| Unit suite, consecutive | 3 | 111/111 every run |
| Architecture suite, consecutive | 3 | 7/7 every run |
| Containers leaked after runs | — | **0**, verified with `docker ps -a` |

**No flaky tests were observed.** Two failures did occur during development, both
deterministic and both real:

1. **All 66 integration tests failed** when Docker Desktop was not running. The
   failure names the cause directly (`DockerUnavailableException`). This is
   environmental, not flaky — it fails 100% of the time without Docker.
2. **A stale-build failure** after a script restored a mutated file with a
   timestamp-preserving copy. MSBuild saw the DLL as newer than the source and
   skipped the rebuild, so the mutated binary persisted. Also deterministic, and
   worth knowing about when verifying mutants by hand.

Integration run time varied between 38.8s and 51.5s across three runs — container
startup, not test instability.

---

## Which checks run where

| Check | Pull request | Main | Nightly |
|---|---|---|---|
| Restore, build | ✅ | ✅ | ✅ |
| Dependency vulnerability audit | ⚠️ reports | ⚠️ reports | — |
| `dotnet format --verify-no-changes` | ✅ | ✅ | — |
| Unit tests | ✅ | ✅ | — |
| Architecture rules | ✅ | ✅ | — |
| Integration and API tests | ✅ | ✅ | — |
| Contract check | ✅ | ✅ | — |
| End-to-end | ✅ | ✅ | — |
| Coverage collection and publication | 📊 reports | 📊 reports | — |
| Docker image build | ✅ | ✅ | — |
| Container smoke check | ✅ | ✅ | — |
| Mutation analysis | — | — | 📊 reports |

✅ blocks the build 📊 published, does not block ⚠️ warns, does not block

---

## Defects found and fixed

Eight. The first seven each had a test written before the fix; the eighth is a
configuration change, verified by observing startup with and without a key.

| # | Defect | Severity | How it was found |
|---|---|---|---|
| 1 | **Broken object-level authorization.** Any authenticated user could read any other user's project via `GET /api/Projects/{id}` | **Security** | Phase 0 assessment, reproduced against the running app |
| 2 | **JWT signed and validated with different keys.** `AddJwtBearer` bound configuration eagerly; `IOptions` bound lazily | **Security, latent** | Broke the suite when `IOptions` was introduced; found from captured logs |
| 3 | **`Verify` threw on a malformed stored hash**, turning a corrupt value into a 500 and disclosing that the account exists | **Security** | Predicted in Phase 0, confirmed test-first |
| 4 | **Mapping silently corrupted data.** Updating a project with only `Status` set wiped `StartDate` to `0001-01-01` | **Correctness** | `AssertConfigurationIsValid` plus a patch-semantics test |
| 5 | **`Project.CreatedAt` never assigned**, so every project stored `0001-01-01` and "newest first" ordering compared identical values | **Correctness** | Observed in a live response during Phase 0 |
| 6 | **`POST` and `GET` returned different representations** of the same project (`createdByName` null on create) | **Correctness** | Observed in live responses |
| 7 | **`JwtSettings:ExpiryInHours` was configured but ignored** by a hardcoded 24-hour expiry | **Correctness** | Reading the code during the Phase 0 assessment |
| 8 | **Two JWT signing keys committed** in `appsettings.json` and `appsettings.Development.json`, allowing anyone with repository access to forge a token for any user | **Security** | Phase 0 assessment; removed during final validation |

Two test defects were also found and fixed:

- An account-enumeration test used a 5-character password, so both requests failed
  model validation and the test never reached the authentication path it claimed
  to assert.
- A redaction test asserted truncation of a long secret, but redaction runs before
  truncation, so the placeholder replaces the secret and the body never reaches
  the limit. The ordering is correct; the test was wrong.

---

## Not measured

**Azure DevOps pipeline execution time.** The repository's remote is GitHub and no
Azure DevOps project is connected, so [azure-pipelines.yml](../azure-pipelines.yml)
has not been executed. It is structurally complete and its YAML parses, but no
timing or pass/fail evidence exists for it and none is claimed.

**GitHub Actions execution time.** The `quality-engineering` branch has been
pushed to `origin`, so [.github/workflows/ci.yml](../.github/workflows/ci.yml)
will have been triggered. The run itself could not be observed from the
environment this work was carried out in - outbound HTTPS to the GitHub API was
unavailable, so no run status, duration or result can be quoted here. The
workflow's timings should be read from the Actions tab rather than from this
document.

**Container smoke check in CI.** The smoke check step is defined in both
pipelines. The equivalent check was performed by hand and passed - the image was
built and the running container served the full request journey, recorded above -
but the CI step itself has not been observed executing.

---

## Known weaknesses

1. **Half the domain is unreachable.** `TaskRepository` is fully implemented with
   overdue and upcoming-task queries, and seven DTOs exist, but no endpoint exposes
   any of it. This is the single largest reason the coverage figure is not higher.
2. **No EF migrations.** Schema comes from `EnsureCreated`, so the tests do not
   exercise the real deployment path. If migrations are added, the fixture should
   switch to `MigrateAsync`.
3. **Token expiry is not tested end-to-end.** The JWT middleware validates lifetime
   against the system clock, which cannot be substituted through
   `TokenValidationParameters` here, so the fake clock is anchored to real time.
   Expiry is asserted at the unit level instead.
4. **PBKDF2 at 10,000 iterations** is below current OWASP guidance (600,000 for
   PBKDF2-SHA256). Unchanged deliberately — it is existing behaviour, and raising
   it is a decision with a performance cost that should be made explicitly.
5. **A known-vulnerable dependency remains**, analysed and accepted in
   [DEPENDENCY_DECISIONS.md](DEPENDENCY_DECISIONS.md).
6. **A signing key must now be configured before the application will start.**
   This is deliberate - see the README - but it is a setup step that did not exist
   before, and anyone cloning the repository will meet it.
7. **`UnitOfWork.Dispose()` disposes the DI-owned `DbContext`**, a double dispose.
   Harmless in practice and left alone.
