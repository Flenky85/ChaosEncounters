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

## Development loop

Implement one small piece, compile it, test it in game, fix it, commit it, and only then add the next piece.

ChatGPT web is used for design and planning. Codex modifies the local repository. Visual Studio 2022 compiles and debugs the mod.
