# Testing strategy

How TaskFlow API is tested, and why each part exists. Measured results are in
[QUALITY_EVIDENCE.md](QUALITY_EVIDENCE.md).

---

## The shape of it

Four projects under `tests/`. One holds the framework; three hold tests.

```
tests/
  TaskFlow.TestInfrastructure/   the reusable framework - contains no tests
  TaskFlow.UnitTests/            fast, isolated, no Docker
  TaskFlow.ArchitectureTests/    dependency rules, no Docker
  TaskFlow.IntegrationTests/     real HTTP, real SQL Server in a container
```

The split is deliberate. Unit and architecture tests run in seconds with no
Docker, so a developer without a container runtime can still work, and CI can
report a broken unit test in about a minute instead of queueing it behind a SQL
Server image pull. Keeping the framework in its own library, with no tests of its
own, means "is this framework code or a test?" is answered by which project the
file is in.

---

## Test levels

### Unit tests — `TaskFlow.UnitTests`

**What belongs here:** behaviour that can be decided without I/O. Password
hashing, token issuance and expiry, login and registration rules, the exception
middleware's mapping of exception types to status codes, the AutoMapper profile,
configuration validation, and the contract comparer's classification rules.

**What is mocked:** only the persistence boundary, `IUnitOfWork` and its
repositories, with `MockBehavior.Strict` so an unexpected call fails rather than
silently returning a default.

**What is not mocked:** `Pbkdf2PasswordHasher`. It is a pure function with no I/O,
and substituting it would replace the behaviour under test - "the stored value
verifies and is not the password" - with an assertion that a mock was called.
Mock the things that talk to the outside world; use the real thing otherwise.

**Time** comes from `FakeTimeProvider`, frozen at a known instant, so token expiry
is an exact assertion rather than a tolerance window.

### Architecture tests — `TaskFlow.ArchitectureTests`

**What belongs here:** dependency rules that the solution already follows.
Core depends on nothing, Infrastructure depends only on Core, neither inner layer
depends on the API, Entity Framework and ASP.NET Core stay out of Core,
controllers reach persistence only through `IUnitOfWork`.

Every rule was verified true before it was written. A rule describing what the
code already does costs nothing to keep green and fails only on genuine
regression. A rule describing an aspiration is permanently red, and a permanently
red test teaches people to ignore the suite.

The rules were also shown to be *capable* of failing - injecting an
`ApplicationDbContext` field into a controller produced the expected violation.
A rule that cannot fail protects nothing, and the project references already
enforce some of these, so the demonstration matters.

### Integration and API tests — `TaskFlow.IntegrationTests/Api`

**What belongs here:** anything where the answer depends on the framework, the
database, or the two together. Status codes, model validation, serialisation,
authentication, authorization, persisted state, unique constraints, cascade
deletes, and route matching.

These go over HTTP into the real application. No controller is constructed
directly and no service is resolved to be called. If a test could pass while the
endpoint was unroutable, it is not testing the API.

**The database is real SQL Server, not the EF in-memory provider.** The
application targets SQL Server in production; only a real server exercises unique
indexes, identity columns, foreign-key delete behaviour and provider-specific
query translation. The in-memory provider would let all of those pass while
broken.

### Contract tests — `TaskFlow.IntegrationTests/Contract`

**What belongs here:** protection of the published API surface. See
[Contract testing](#contract-testing) below.

### End-to-end — `TaskFlow.IntegrationTests/EndToEnd`

**What belongs here:** one complete user journey, carrying real state between
steps. Register, log in separately, list, create, read back by following the
returned `Location` header, update with a partial payload, verify the
authorization boundary against a second user, check the administrator view,
delete, confirm removal, and confirm the database agrees.

Deliberately one test, not many. Its value is that everything is wired together;
that is proven once. Turning every scenario into a journey would produce slow
tests that fail for many possible reasons, which is the opposite of diagnosable.

---

## The framework

`TaskFlow.TestInfrastructure` exists so that individual tests contain arrange,
act and assert, and nothing else.

| Component | Responsibility |
|---|---|
| `TaskFlowApiFactory` | Boots the real API in-process via `WebApplicationFactory` |
| `SqlServerContainerFixture` | Starts, prepares and disposes the SQL Server container |
| `IntegrationTestFixture` | Holds the container and host for the whole assembly |
| `IntegrationTestBase` | Resets the database per test; supplies clients and helpers |
| `UserBuilder`, `ProjectBuilder`, `TaskItemBuilder` | Test data with fixed timestamps |
| `TestUsers`, `TestRoles` | Named identities, so "another user's project" is unambiguous |
| `ApiAssert` | Assertions that report request, response and logs on failure |
| `HttpExchangeRecorder`, `RecordingHttpHandler` | Captures traffic, redacts secrets |
| `InMemoryLogSink` | Captures application logs for failure output |
| `OpenApiContractExtractor`, `ContractComparer` | Contract extraction and classification |

### Two things worth knowing

**Only two things are redirected** when the application is booted: the connection
string and the JWT settings, both through configuration. The application's own DI
registrations stay on the tested path rather than being replaced by a parallel
set built for tests.

**`ILoggerFactory` is replaced, not `Log.Logger`.** The obvious approach -
reassigning Serilog's static logger after the host is built - was tried and did
not work reliably: the application kept writing to the console and dropping
rolling log files into the test binary directory. Replacing `ILoggerFactory` in
`ConfigureTestServices` is deterministic, because those registrations run after
the application's own.

---

## Test data

Built, not fixtured from files. Builders default every field to a valid value and
expose only what a test needs to vary, so a test says what matters about its data
and stays silent about the rest.

Timestamps default to fixed dates rather than `DateTime.UtcNow`, so ordering
assertions are reproducible on any machine at any time of day.

Passwords are hashed with the application's own `IPasswordHasher`. Re-implementing
the hash in test code would let the test format drift from production; extracting
the interface was what made seeding through the real login endpoint possible.

---

## Database isolation

One container for the whole integration assembly. One database inside it. Every
row deleted between tests by [Respawn](https://github.com/jbogard/Respawn), with
identity seeds reset so generated keys are reproducible.

**Why not a container per test class:** starting SQL Server costs roughly 25
seconds. With per-class containers, startup would dominate the run.

**Why not a database per test:** it would allow parallelism, but it means creating
a schema per test and complicates both the connection-string plumbing and failure
diagnosis. Worth revisiting if the suite grows enough for the serial run time to
hurt. At the current size it would buy little and cost clarity.

### Why integration tests run serially

They share a database, and isolation comes from deleting every row between tests.
That is only correct if no other test is mid-flight: in parallel, one test's reset
would erase another's arrangement, producing exactly the intermittent failure that
destroys trust in a suite.

All integration tests are therefore in one xUnit collection, which serialises them
with respect to each other, and the collection sets `DisableParallelization` so it
cannot run alongside another collection.

Unit and architecture tests share no state and run fully parallel.

### Schema creation

From the EF model via `EnsureCreated`, because the repository contains no
migrations - the same call the application makes at startup in Development. The
trade-off is that migration scripts are not exercised; there are none to exercise.
**If migrations are added, this should change to `Database.MigrateAsync()`** so the
tests validate the real deployment path.

---

## Authentication in tests

Tests get tokens by calling the real `POST /api/auth/login` endpoint. Nothing
forges a token and no test authentication handler is substituted. If login breaks,
these tests fail - which is correct, because the application would be broken.

Users are seeded directly into the database first. That is what makes an
administrator client possible at all, since the register endpoint hardcodes the
`User` role and offers no route to elevated privilege.

**No production credentials are used anywhere.** The signing key in
`TaskFlowApiFactory` is a test-only fixture supplied through in-memory
configuration, never a value the application is deployed with. Test user passwords
are fixtures in `TestUsers`. The SQL Server password is generated by
Testcontainers for a container that lives only for the run.

---

## Contract testing

The application must not break its published API by accident. A renamed property
or a dropped response code compiles cleanly and ships silently.

### How the contract is obtained

Generated in-process through Swashbuckle's `ISwaggerProvider`, resolved from the
application's own container. No production change was needed: the Swagger endpoint
is Development-only, but `AddSwaggerGen` is registered unconditionally, so
generation is available even where the endpoint is not exposed.

### What is compared

Not the whole document. A full-file snapshot flags every addition, description
edit and reordering as a difference; the baseline then needs constant updating,
reviewers learn to approve the diff without reading it, and the check stops
protecting anything.

What is kept is what a client can break against: which operations exist, the
status codes they document, their parameters and request bodies, the shape of each
schema, and the authorization each endpoint requires. Summaries, descriptions,
examples and tags are excluded, because changing them cannot break a caller.

**Authorization is read from endpoint routing metadata, not from the OpenAPI
document.** The application declares a single document-wide security scheme, so
every operation in the document appears to require authentication - including the
anonymous login and register routes. Reading `IAuthorizeData` and
`IAllowAnonymous` reports what the framework will actually enforce, which makes
"this endpoint quietly lost its `[Authorize]`" a detectable regression.

### What counts as breaking

| Breaking - fails the build | Additive - passes |
|---|---|
| Operation removed | Operation added |
| Documented response code removed | Response code added |
| Schema or schema property removed | Schema or optional property added |
| Property type or format changed | Required property relaxed to optional |
| Property or parameter newly required | Optional parameter added |
| Request body type changed | |
| Authentication or role requirement changed **either way** | |

Authorization is breaking in both directions. Newly requiring authentication
breaks existing callers; dropping it does not, but it publishes data that was
protected. Neither should ship unnoticed.

One judgement call: adding a required property to a schema is treated as breaking.
For a request DTO it plainly is; for a response DTO it is harmless. The contract
does not record which direction a schema travels in, and the same type is often
used for both, so the rule errs toward reporting. A false alarm costs a
conversation; a missed break costs a client outage.

### Approving an intentional contract change

1. Make the change and run the suite. The contract test fails and names every
   breaking change with the rule that classified it.
2. **Read the list.** This is the review step, and the reason the check reports
   only changes that can actually break a caller.
3. If every change is intended, regenerate the baseline:

   ```bash
   TASKFLOW_APPROVE_CONTRACT=1 dotnet test tests/TaskFlow.IntegrationTests \
       --filter FullyQualifiedName~ApiContractTests
   ```

4. Commit the updated `tests/TaskFlow.IntegrationTests/Contract/api-contract.approved.json`
   **in the same commit as the change that caused it**, so the diff shows the code
   change and the contract change together.
5. The baseline diff is reviewed like any other source change. A pull request that
   modifies it is making a public API change and should say so.

Approving is deliberately a separate, explicit command. It cannot happen as a side
effect of running the tests.

---

## Mutation testing

Coverage says a line ran. It does not say a test would notice if the line were
wrong. Mutation testing asks the harder question by changing the code and checking
whether anything fails.

**Scope:** `TaskFlow.Core`'s services and configuration - authentication, token
issuance, password hashing. DTOs and entities are excluded because mutating
auto-properties produces survivors that mean nothing and bury the real ones.

**Only the unit suite runs the mutants.** The integration suite is a better test of
behaviour, but it starts a SQL Server container; running it once per mutant would
turn minutes into hours for no additional signal.

**The score does not gate the build.** `thresholds.break` is 0 on purpose. A
mutation score used as a gate gets optimised, and tests written to kill mutants
rather than to describe behaviour are worse than the ones they replace. The report
is a diagnostic to read, not a target to hit.

**It runs nightly, not per pull request** — see
[.azuredevops/mutation-pipeline.yml](../.azuredevops/mutation-pipeline.yml). It takes
around two minutes against a unit suite that finishes in under a second. In the PR
path that would multiply feedback time to answer a question that does not change
between commits: "are these tests capable of detecting a wrong answer?" is a
property of the suite, not of a particular change. A drop is worth investigating
within a day; it is not worth blocking a pull request.

### Reading the report honestly

**Stryker counts a timeout as a kill.** An early run reported 97.3% with six
mutants classified as `Timeout`. Applying all six by hand showed only two were
genuinely detected; the other four passed the suite untouched. The honest score at
that point was 86.5%. Later runs on an unloaded machine produced no timeouts at
all, so the classification was an artefact of machine load.

**If a run reports timeouts, verify them before quoting the score.** Apply the
mutant by hand, run the suite, and see what happens. Note that restoring a file
with a tool that preserves its modification time will leave the mutated binary in
place, because MSBuild sees the DLL as newer than the source and skips the
rebuild.

---

## Investigating a flaky test

A retry is not a fix. It converts a real defect into an intermittent one that will
be found later, in a worse place, by someone with less context.

1. **Do not add a retry.** If a test is genuinely unreliable, disable it with a
   linked issue rather than hiding it behind a retry that makes the suite green and
   the signal worthless.
2. **Reproduce it.** Run the suite repeatedly: `for i in 1 2 3 4 5; do dotnet test ...; done`.
   Note whether it fails in isolation or only in a full run - the latter points at
   shared state.
3. **Read the diagnostics.** Integration failures print the HTTP exchanges,
   warnings and errors, and the application log. Most causes are visible there.
4. **Check the usual suspects, in order:**
   - **Time.** Anything reading `DateTime.UtcNow` instead of the injected
     `TimeProvider` will eventually straddle a boundary. The clock in tests is
     frozen; production code that bypasses it is the bug.
   - **Ordering.** A test asserting sequence without an explicit `OrderBy` depends
     on whatever the database returns. Pin the order in the query or assert as a set.
   - **Shared state.** A test that passes alone and fails in a run is usually
     relying on data another test left behind, or leaking data of its own.
   - **Container startup.** SQL Server readiness is polled with an explicit
     connection probe and a three-minute budget. A timeout here reports the
     container logs; that is an infrastructure failure, not a test failure.
5. **Fix the cause, and keep the test.** A test that found a real race is a good
   test.

---

## Quality gates and why

| Gate | Blocks the build | Why |
|---|---|---|
| Build | Yes | Nothing downstream is meaningful otherwise |
| `dotnet format --verify-no-changes` | Yes | Cheap, objective, instant; runs before tests so a formatting slip does not wait behind them |
| Unit tests | Yes | Fast and deterministic; a failure is always a real regression |
| Architecture rules | Yes | Every rule was true when written, so a failure means someone regressed it |
| Integration and API tests | Yes | The behaviour clients depend on |
| Contract check | Yes | Only breaking changes fail, so a failure always means a caller could break |
| End-to-end | Yes | One journey; if it fails, something is wired wrong |
| Container smoke check | Yes | Building an image is not evidence it runs |
| Coverage | **Reported, not enforced** | See below |
| Mutation score | **Reported, not enforced** | A score used as a gate gets optimised |
| Vulnerable-package audit | **Reported, not enforced** | See [DEPENDENCY_DECISIONS.md](DEPENDENCY_DECISIONS.md) |

### Why coverage is not a gate

The measured figure is limited by code that has no caller: `TaskRepository` is
fully implemented but no task endpoints exist, and seven DTOs are referenced by
nothing. Every line reachable through the API is already covered.

A threshold would therefore be satisfied by writing tests for unused DTOs, which
raises the number and proves nothing. The honest way to raise this figure is to
delete the dead code or expose the endpoints it was written for. Coverage is
published on every run and its *distribution* is what to read - a business-logic
class dropping from 100% is worth investigating; the total moving a point is not.

### What is excluded from coverage, and why

Exactly one thing: `tests/TaskFlow.TestInfrastructure`. It is a class library
rather than a test project, so the collector counts it as production code by
default; including it measured how well the tests cover their own helpers.

`CompilerGeneratedAttribute` is deliberately **not** excluded, though it is
commonly recommended. It removes auto-property accessors, which hold no logic, but
it also removes the closure classes emitted for lambdas - and this codebase keeps
real query logic in lambdas inside the repositories. With it enabled, coverable
lines fell from 743 to 347 and the reported figure jumped to 95.9%: a better
number measuring less code.

---

## Resilience testing: assessed, not implemented

**Conclusion: not justified for this application as it stands.**

The brief asked whether a controlled failure-injection test - Toxiproxy or
similar - would add value. It would not, because there is nothing to assert. The
application contains no resilience behaviour:

- no Polly or `Microsoft.Extensions.Http.Resilience`;
- no `EnableRetryOnFailure()` on the SQL Server provider, so EF Core's connection
  resiliency is off;
- no hand-rolled retry, backoff, timeout or circuit-breaker;
- no outbound HTTP dependencies at all;
- `IUnitOfWork`'s transaction methods exist but nothing calls them.

A Toxiproxy test would therefore verify a behaviour the application does not have.
Implementing one would mean adding retry logic first, purely so there is something
to test - which inverts the relationship between behaviour and its tests, and is
exactly what the brief warned against.

**What would change this answer:** a decision to make the API tolerant of
transient database failures. If `EnableRetryOnFailure()` were configured, or a
resilience pipeline added around an outbound call, then failure injection becomes
the only honest way to verify it - the retry either happens or it does not, and no
unit test can tell you which. At that point this should be revisited.

---

## Running the suites

```bash
# everything, no Docker needed
dotnet test tests/TaskFlow.UnitTests
dotnet test tests/TaskFlow.ArchitectureTests

# needs Docker running
dotnet test tests/TaskFlow.IntegrationTests

# a single level
dotnet test tests/TaskFlow.IntegrationTests --filter FullyQualifiedName~Contract
dotnet test tests/TaskFlow.IntegrationTests --filter FullyQualifiedName~EndToEnd

# everything
dotnet test TaskFlowAPI.sln

# with coverage
dotnet test TaskFlowAPI.sln --settings coverlet.runsettings \
    --collect:"XPlat Code Coverage" --results-directory artifacts/coverage
reportgenerator -reports:"artifacts/coverage/**/coverage.cobertura.xml" \
    -targetdir:artifacts/coverage/report -reporttypes:"TextSummary;Html"

# mutation testing
dotnet tool install -g dotnet-stryker --version 4.16.0
dotnet-stryker --config-file stryker-config.json
```

**Docker must be running** for the integration suite. If it is not, every test in
that project fails with `DockerUnavailableException`, which names the cause
directly.

---

## How CI runs them

Two equivalent definitions: [azure-pipelines.yml](../azure-pipelines.yml) and
[.github/workflows/ci.yml](../.github/workflows/ci.yml). The repository is hosted
on GitHub, so the Actions workflow is the one that executes.

Three stages, ordered by cost so the cheapest failure reports first:

1. **Validate** — restore, build, dependency audit, formatting, unit tests,
   architecture rules. No Docker.
2. **Integration** — API, contract and end-to-end tests against a real SQL Server
   container, then coverage collection and publication.
3. **Container** — build the Docker image and require the running container to
   serve HTTP.

Test results are published even when tests fail, which is exactly when they are
needed. Test containers are swept after every integration run, and that cleanup
never fails the build - it is cleanup, not a check.

Mutation testing runs on its own nightly schedule, outside the pull-request path.

### Pull requests versus main

Both currently run the same checks. The suite finishes in a few minutes, so
there is no case yet for a reduced pull-request pipeline: splitting them would
mean some breakage is only discovered after merge, in exchange for saving very
little. If the integration suite grows to the point where it dominates feedback
time, the split to make is to keep unit, architecture and contract checks on
every push and move the full integration and container stages to merge queue or
main - not to weaken what runs before merge.
