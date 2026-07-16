# AGENTS.md

## Project

Chaos Encounters is a mod for Warhammer 40,000: Rogue Trader, developed with C#, Visual Studio 2022, Git, and Codex.

## Working rules

* Inspect the repository before modifying anything.
* Treat the repository, compiler output, game logs, and in-game tests as the source of truth.
* Make small, surgical changes.
* Work on one clearly defined piece at a time.
* Do not redesign or refactor unrelated code.
* Do not add abstractions or complexity before they are needed.
* Never invent Rogue Trader classes, methods, namespaces, blueprints, GUIDs, components, or Harmony signatures.
* Clearly separate confirmed facts from assumptions.
* When game internals are unknown, identify exactly what must be inspected in ILSpy, dnSpy, ToyBox, logs, or other mods.
* Prefer observation and logging before implementing complex behavior.
* Keep mechanics modular, configurable, testable, and removable.
* Explain which files will change and how the result should be tested.
* Warn about edge cases, compatibility risks, and simpler alternatives.
* Avoid large code changes unless explicitly requested.

## Blockers and incomplete instructions

* If the requested work is blocked, ambiguous, or missing information required for a safe implementation, stop.
* Clearly explain the blocker, what was discovered, and what information or decision is needed from the user.
* Do not invent requirements or choose an architectural direction on the user's behalf.
* Do not create speculative workarounds, fallback systems, retry loops, polling loops, or additional complexity to bypass an unexpected problem.
* Do not repeatedly modify the code in an attempt to make an unclear solution work.
* Do not fix unrelated problems discovered during the task without explicit user approval.
* When a command, compilation, or test fails unexpectedly, report the exact failure and wait for direction before attempting a substantially different approach.
* Small corrections that are unquestionably required to complete the agreed change are allowed, but anything that changes scope or design requires user approval.

## Dependency rules

* Chaos Encounters must not have compile-time or runtime dependencies on other user-installed mods.
* Other mods may be inspected only as research references.
* Never reference, import, invoke, copy, or ship assemblies or helper APIs belonging to another mod.
* Any game type, method, event, or Harmony target discovered through another mod must be independently verified in Rogue Trader's original assemblies before use.
* Implementation may depend only on Rogue Trader's original assemblies, Unity Mod Manager, Harmony, the .NET framework, and libraries explicitly included by this project.
* Features must be tested with Chaos Encounters enabled and all unrelated mods disabled whenever practical.

## Performance discipline

* Minimal runtime overhead is a project requirement.
* Do not perform runtime work that does not support an active feature.
* Prefer direct Rogue Trader, Unity Mod Manager, Harmony, Unity, or .NET APIs over reflection, dynamic lookup, string-based member access, or assembly scanning.
* Use reflection only when no verified direct API exists and the user has approved the tradeoff.
* Do not scan assemblies when there are no active targets to discover.
* Prefer EventBus handlers and other direct events over polling, `Update()`, repeated ticks, timers, coroutines, or periodic global scans.
* Identify whether code runs on a cold path or a hot path before implementing it.
* Treat the following as hot paths unless proven otherwise:

  * Unity frame updates
  * controller ticks
  * AI evaluation
  * combat turns
  * attacks and damage
  * movement
  * ability execution
  * per-unit callbacks
* Avoid LINQ, closures, boxing, temporary collections, reflection, repeated blueprint lookups, and avoidable string creation in hot paths.
* LINQ and temporary snapshots are allowed in infrequent cold paths only when they provide a clear correctness or readability benefit.
* When LINQ or a temporary collection is used, state how frequently it executes and why the allocation is acceptable.
* Do not create speculative caches, object pools, background loops, polling systems, or performance abstractions.
* Cache data only when the source is stable, invalidation is understood, and repeated lookup has a demonstrated or structurally obvious cost.
* Prefer a single verified enumeration over repeated enumeration of the same game collection.
* Do not repeatedly enumerate all loaded units when a narrower verified collection or event exists.
* Research logging may be verbose, but production behavior must avoid unnecessary recurring log output and string allocation.
* Every proposed implementation must report:

  * execution trigger
  * expected frequency
  * collections enumerated
  * expected allocations
  * lower-cost alternatives considered
* Remove confirmed zero-value runtime work, even when its individual cost is small.
* Do not sacrifice correctness or compatibility for speculative micro-optimizations.
* Compiler success is not sufficient evidence of performance safety; runtime frequency and allocation behavior must also be considered.


## Development loop

Implement one small piece, compile it, test it in game, fix it, commit it, and only then add the next piece.

ChatGPT web is used for design and planning. Codex modifies the local repository. Visual Studio 2022 compiles and debugs the mod.
