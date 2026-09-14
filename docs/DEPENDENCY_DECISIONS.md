# Dependency decisions

Records deliberate decisions about third-party packages, so that a known issue
left in place is distinguishable from one nobody noticed.

## AutoMapper 14.0.0 — GHSA-rvv3-g6hj-g44x (High)

**Status:** accepted, reported in CI, not upgraded.
**Decided:** 14 September 2026.
**Review:** whenever the API begins accepting nested request payloads, or when a
migration off AutoMapper is considered.

### The advisory

[GHSA-rvv3-g6hj-g44x](https://github.com/advisories/GHSA-rvv3-g6hj-g44x) —
denial of service through uncontrolled recursion. AutoMapper maps deeply nested
object graphs with recursive calls and no default depth limit, so a sufficiently
nested graph can exhaust the stack and terminate the process with a
`StackOverflowException`, which cannot be caught.

Affected: `< 15.1.1`, and `16.0.0`–`16.1.0`. Fixed in `15.1.1` and `16.1.1`.
This repository uses `14.0.0`, which is affected.

It surfaces on every restore as `NU1903`, and
`dotnet list package --vulnerable` reports it for every project that references
the API assembly.

### Why it is not exploitable here

The attack requires an attacker-controlled object graph deep enough to exhaust
the stack. In this application no such graph can reach AutoMapper:

- The inbound DTOs — `CreateProjectDto`, `UpdateProjectDto`, `LoginDto`,
  `RegisterDto` — are flat. Every member is a string, a number, a date or a
  nullable of one. None contains a collection or a nested object, so a request
  body cannot express nesting at all, however it is crafted.
- The mappings that do traverse object graphs run in the opposite direction:
  `Project → ProjectDto` and `TaskItem → TaskItemDto` project entities that were
  loaded from the database. Their depth is bounded by the schema, which is two
  levels: a project holds tasks, and a task holds no children.
- The self-referential shape the advisory describes — a type containing a
  property of its own type — does not exist in this domain.

The severity rating reflects the worst case across all AutoMapper users, not the
exposure of any particular application. Here the input that would be required
cannot be constructed through the public API.

### Why it is not being upgraded

Upgrading to a patched version means moving to AutoMapper 15 or later, which
carries two costs disproportionate to a risk that is not reachable:

1. **Licence change.** AutoMapper moved to a commercial licence at version 15.
   Adopting it means accepting licence terms and, above a revenue threshold,
   paying for it. That is a decision about the project's dependencies and
   obligations, not a security fix, and it should not be made as a side effect of
   silencing a warning.
2. **Breaking API changes.** Version 15 changed configuration and dependency
   injection registration. The change would touch `Program.cs` and
   `MappingProfile`, and the mapping profile is code that already proved capable
   of failing silently — a blanket null check that quietly overwrote
   non-nullable destinations, fixed in Phase 2. Rewriting it under time pressure
   to clear an unreachable advisory is a poor trade.

Removing AutoMapper altogether in favour of explicit mapping is a legitimate
option and would eliminate the dependency, the licence question and the advisory
at once. It is a larger change than this work covers, and it should be judged on
its own merits rather than as a security response.

### What CI does about it

The pipeline runs `dotnet list package --vulnerable --include-transitive` as a
visible step and prints the result. It **reports without failing**, for two
reasons:

- Failing on it would block every build until the licence decision above is
  made, which converts a known, analysed, unreachable issue into a work stoppage.
- A gate that everyone has to bypass is not a gate. Teams respond to a
  permanently red check by adding a suppression, and the suppression then hides
  the next advisory too — the one that *is* reachable.

The step is loud rather than silent: the advisory appears in the build log and
the job summary on every run, so it cannot quietly become invisible.

**This decision should be revisited if the API starts accepting nested request
payloads**, because that is the precondition that makes the advisory reachable.
Adding a DTO with a collection or nested object is the trigger to come back here.
