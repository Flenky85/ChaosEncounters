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

## Development loop

Implement one small piece, compile it, test it in game, fix it, commit it, and only then add the next piece.

ChatGPT web is used for design and planning. Codex modifies the local repository. Visual Studio 2022 compiles and debugs the mod.
