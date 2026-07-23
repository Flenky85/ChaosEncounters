# Research status

## Status

Research instrumentation is implemented and statically/build validated on `research-lab`. Manual runtime validation is pending. The roster bug is not yet understood.

## Repository inspection

Initial state:

- Branch: `research-lab`
- Working tree: clean
- Initial HEAD: `0803458 Initialize round-end healing prototype`

The prompt-named `EncounterRuntime.cs`, `EncounterSession.cs`, `Combat/Persistence/`, and `EncounterMechanicController.cs` do not exist on this branch. The actual repository-local combat-start observer is `ChaosEncounters/Combat/CombatStartProbe.cs`; it enumerates `Game.Instance.State.AllBaseAwakeUnitsForSure` during `IPartyCombatHandler.HandlePartyCombatStateChanged(true)`.

Directly relevant prior research read:

- `.research/reports/research-status.md`
- `.research/decompiled/combat-start/analysis.md`
- `.research/export/combat-start-review/`
- `.research/save-specific-combat-state/`
- `.research/link-mechanic-roster/`

## Current original-assembly evidence

Focused types were freshly decompiled from the installed original assemblies under:

```text
<RogueTraderInstallDir>\WH40KRT_Data\Managed
```

The focused outputs are stored in `evidence/decompiled-current/`. No game assemblies are included.

Confirmed native ordering:

1. `PartUnitCombatState.JoinCombat(bool)` sets `m_InCombat = true`.
2. It raises entity-scoped `IUnitCombatHandler.HandleUnitJoinCombat()`.
3. It wakes the owner.
4. It raises global `IAnyUnitCombatHandler.HandleUnitJoinCombat(BaseUnitEntity)`.
5. `UnitCombatJoinController` later recomputes `Player.IsInCombat` and raises `IPartyCombatHandler`.
6. `TurnController.EnterTb()` sets TB active, raises `ITurnBasedModeHandler`, calls `InitiativeHelper.Update()`, calls `AddUnitsToCombat()`, starts round 1, then starts preparation or the first normal turn.
7. `InitiativeHelper.Roll(IEnumerable<MechanicEntity>, bool)` assigns initiative roll/value/order to new combatants.
8. On loaded state, `UnitCombatJoinController.OnEnable()` recomputes aggregate party combat; `TurnController.OnStart()` restores the current turn and may restore preparation; first-tick `ApplyPostLoadFixes()` performs additional native fixes.

## Collections compared

- `Game.Instance.State.AllBaseAwakeUnitsForSure`
- `Game.Instance.State.AllBaseUnits.All`
- `Game.Instance.TurnController.AllUnits`, restricted to `BaseUnitEntity`
- Initiative membership represented directly by `!unit.Initiative.Empty`

`TurnController.AllUnits` is not a separate stored roster. Native code verifies that it enumerates awake base units not controlled by the director/not extra/not removed from initiative, then squads, meteors, and initiative placeholders. The probe logs its base-unit projection because it answers a materially different eligibility question.

No verified private combatant collection was accessed. No reflection was introduced.

## Eligibility label

`eligibleForChaosEncounters` is an observational label matching the current branch's enemy-enumeration basis:

```text
present in AllBaseAwakeUnitsForSure
IsInGame
IsInCombat
IsPlayerEnemy
not StarshipEntity
```

It does not activate a mechanic and is not a proposed production policy.

## Build and deployment validation

Canonical command:

```powershell
dotnet build .\ChaosEncounters.slnx --configuration Debug --no-restore --verbosity normal
```

Result after explicit user authorization for the normal project deploy target:

- Build: succeeded
- Warnings: 0
- Errors: 0
- Debug DLL, PDB, and `Info.json`: deployed to the installed Unity Mod Manager ChaosEncounters directory
- Deployment verification: build-output and deployed-file SHA-256 hashes were compared

The build-generated `bin/` and `obj/` files remain ignored and are not part of the research commit.

## Performance and allocation report

Execution triggers:

- Once per verified area/load callback.
- Once per party combat transition.
- Once per unit join/leave until capture completion.
- Once per TB/round/preparation/first-turn callback.
- Prefix/postfix around cold combat-start and loaded-state methods.
- `InitiativeHelper.Roll` may run for later joining combatants, but logging returns immediately after capture completion.

Expected frequency:

- Full snapshots are deliberately frequent only from the first combat-related boundary through the first real unit turn.
- After `CAPTURE_COMPLETE`, verbose callbacks and Harmony boundaries return without logging; party-combat false performs cleanup for the next combat.

Collections enumerated per full snapshot:

- awake base units;
- all base units;
- `TurnController.AllUnits`;
- party and pets for nearest-distance diagnostics.

Expected allocations:

- Hash sets, dictionaries, state records, strings, and full log records per snapshot.
- This is deliberate research-only cold-path allocation.

Lower-cost alternatives considered:

- Production logging or a single party-combat snapshot was rejected because it cannot locate the 7-to-17 transition.
- Polling, `Update`, timers, coroutines, reflection, and assembly scans were rejected.
- Private collection reflection was rejected in favor of direct methods and direct public collections.

## Known limitations

- Runtime callback order is not claimed until observed in the requested encounter.
- `AllBaseUnits.All` and awake collections are expected to contain unique entities; the probe reports reference-set summaries and tracks stable identity, but does not claim a native duplicate cause without runtime evidence.
- Initiative membership is defined by the verified native `Initiative.Empty` state; squad and placeholder entities are not emitted as per-unit `BaseUnitEntity` records.
- Area loading has completion-stage callbacks but no verified EventBus “begin loading” callback. `IAreaHandler.OnAreaBeginUnloading()` is logged as `AREA_LOADING_START`; the exact semantics are “old area begins unloading.”
- The header cannot statically distinguish new-game load from save-game load at mod startup, so it records `UnknownAtModStartup`; the subsequent area and controller restoration boundaries provide the runtime evidence.
- Optional property access is guarded. A failed optional read is represented as `Unavailable` where the field is textual, or accompanied by an availability flag for life/health.

## Runtime validation still required

Run both procedures in `README.md`, preserve both log files, and compare:

- the first `NewSincePreviousSnapshot` for each missing enemy;
- changes in `PresentInAwake`, `PresentInAllBase`, `PresentInTurnController`, and `PresentInInitiative`;
- join callback state versus `UNIT_JOIN_PREFIX`/`UNIT_JOIN_POSTFIX`;
- normal `ENTER_TB`/initiative/preparation ordering versus loaded-state restoration ordering;
- the exact first snapshot where living enemy counts change from 7 to 17.

Only those logs can answer the research question.
