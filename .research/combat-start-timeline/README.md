# Combat-start timeline probe

## Research question

The normal start of one visible 17-enemy encounter exposes only 7 enemies to Chaos Encounters, while loading a save made during that combat exposes all 17. This package instruments combat startup without changing combat, encounter, initiative, deployment, hostility, health, persistence, or roster behavior.

The runtime log must establish when each unit becomes present in native collections, in game, in combat, hostile, initiative-bearing, and eligible for the current Chaos Encounters enemy enumeration. Static analysis alone does not explain the 7-to-17 transition.

## Read order

1. `reports/research-status.md`
2. `reports/instrumentation-map.md`
3. Focused current-assembly decompilations under `evidence/decompiled-current/`

## Output

Each game launch recreates:

```text
<Chaos Encounters mod directory>/Logs/ChaosEncounters-combat-start-timeline.log
```

Every record includes a monotonically increasing sequence, millisecond timestamp, Unity frame where available, probe point and position, game mode, party combat state, TB state, combat round, preparation state, and whether the first real unit turn has begun. Writes are synchronous, immediately flushed, and isolated from `General.log`.

## Manual test procedure

Normal initialization:

1. Launch the game fresh with the `research-lab` build.
2. Load a save immediately before the problematic encounter.
3. Start the encounter normally.
4. Do not save or load during the first run.
5. Allow deployment/preparation to begin.
6. Finish deployment.
7. Allow the first real unit turn to begin.
8. Exit or return to the menu.
9. Collect `ChaosEncounters-combat-start-timeline.log`.

Loaded-state comparison:

1. Start the same encounter.
2. Save after the combat has begun but before it ends.
3. Load that save.
4. Allow the first real unit turn after loading.
5. Collect the new timeline log.

The log is recreated at mod startup, so preserve the first run before launching the second.

## Safety boundary

The probe only subscribes to verified EventBus interfaces, observes verified collections, applies narrow prefix/postfix patches to verified original-game methods, and writes its separate log. It uses no timers, coroutines, `Update`, assembly scans, runtime reflection, transpilers, other-mod APIs, or production runtime fault handling.

Runtime validation is pending. Do not infer a fix or promote this instrumentation to a production branch until the two requested logs identify the exact roster transition.
