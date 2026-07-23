# Instrumentation map

## Verified EventBus callbacks

| Interface | Exact callback | Snapshot/use |
|---|---|---|
| `Kingmaker.PubSubSystem.Core.IAreaHandler` | `void OnAreaBeginUnloading()` | `AREA_LOADING_START` |
| `Kingmaker.PubSubSystem.Core.IAreaHandler` | `void OnAreaDidLoad()` | lifecycle record |
| `Kingmaker.PubSubSystem.Core.IAreaActivationHandler` | `void OnAreaActivated()` | lifecycle record |
| `Kingmaker.PubSubSystem.Core.IAreaLoadingStagesHandler` | `void OnAreaScenesLoaded()` | lifecycle record |
| `Kingmaker.PubSubSystem.Core.IAreaLoadingStagesHandler` | `void OnAreaLoadingComplete()` | `AREA_LOAD_COMPLETE` when combat state exists |
| `Kingmaker.PubSubSystem.Core.IPartyCombatHandler` | `void HandlePartyCombatStateChanged(bool inCombat)` | `PARTY_COMBAT_TRUE/FALSE` and complete snapshot |
| `Kingmaker.PubSubSystem.IAnyUnitCombatHandler` | `void HandleUnitJoinCombat(BaseUnitEntity unit)` | unfiltered entry/exit unit records; full snapshot for enemy/potentially hostile unit |
| `Kingmaker.PubSubSystem.IAnyUnitCombatHandler` | `void HandleUnitLeaveCombat(BaseUnitEntity unit)` | unfiltered entry/exit unit records |
| `Kingmaker.Controllers.TurnBased.ITurnBasedModeHandler` | `void HandleTurnBasedModeSwitched(bool isTurnBased)` | `TURN_BASED_MODE_BEGIN/END` |
| `Kingmaker.Controllers.TurnBased.IRoundStartHandler` | `void HandleRoundStart(bool isTurnBased)` | `ROUND_START` |
| `Kingmaker.Controllers.TurnBased.IPreparationTurnBeginHandler` | `void HandleBeginPreparationTurn(bool canDeploy)` | `PREPARATION_BEGIN` |
| `Kingmaker.Controllers.TurnBased.IPreparationTurnEndHandler` | `void HandleEndPreparationTurn()` | `PREPARATION_END` |
| `Kingmaker.Controllers.TurnBased.ITurnStartHandler` | `void HandleUnitStartTurn(bool isTurnBased)` | final actor and `FIRST_REAL_UNIT_TURN` snapshot |

The turn-start interface is entity-scoped; the acting entity is read from `EventInvokerExtensions.MechanicEntity`. Preparation turns are explicitly distinguished from the first normal turn.

## Verified Harmony targets

All targets are in the installed original `Code.dll`. There are no transpilers.

| Declaring type and exact method | Probe | Why EventBus is insufficient | Expected frequency |
|---|---|---|---|
| `PartUnitCombatState.JoinCombat(bool surprised = false)` | prefix/postfix | Global join callback occurs after `m_InCombat` mutation and `Wake()`; prefix/postfix exposes before/after state | once per unit false-to-true combat transition |
| `InitiativeHelper.Roll(IEnumerable<MechanicEntity> newCombatants, bool relax)` | prefix/postfix | Initiative is assigned inside the method before later callbacks | initial population plus late combatants |
| `TurnController.EnterTb()` | prefix/postfix | `ITurnBasedModeHandler(true)` fires before native initiative population but cannot show the entire method's before/after state | once per TB entry |
| `TurnController.AddUnitsToCombat()` | prefix/postfix | directly brackets scheduling and initial initiative roll | once in `EnterTb()` |
| `TurnController.BeginPreparationTurn(bool canDeploy)` | prefix/postfix | callback is raised after `TurnOrder.BeginPreparationTurn()` and other mutations | initial or restored preparation |
| `TurnController.ForceEndPreparationTurn()` | prefix/postfix | callback is raised after `TurnOrder.EndPreparationTurn()` | once when deployment is finished |
| `TurnController.NextTurnTB()` | prefix/postfix | directly brackets selection/start of the first normal unit turn | once per turn natively; probe returns after first real turn |
| `TurnController.OnStart()` | prefix/postfix | loaded `TurnDataPart` restoration and preparation restart precede general load completion | once when controller starts |
| `TurnController.ApplyPostLoadFixes()` | prefix/postfix | private first-tick loaded-state mutations have no dedicated EventBus completion callback | once on controller's first tick |
| `UnitCombatJoinController.OnEnable()` | prefix/postfix | recomputes serialized unit combat into aggregate `Player.IsInCombat` during load | once when controller enables |

The private targets were verified by current decompilation and are named explicitly for Harmony. No runtime target discovery or reflection scan is used.

## Snapshot content

Each full snapshot logs:

- collection summaries and cross-collection enemy counts;
- stable identity (`UniqueId`, plus reference identity hash);
- descriptive character/blueprint/faction/runtime data;
- position and nearest-party distance;
- in-game, combat, hostility/faction, disposal, starship, life, death, kill-marker, and health state;
- presence in awake, all-base, turn-controller, and initiative sources;
- preparation and current-turn status;
- observational Chaos Encounters eligibility;
- `NewSincePreviousSnapshot`, `MissingSincePreviousSnapshot`, and field-level `ChangedStateSincePreviousSnapshot`.

## Failure isolation

Every top-level callback and native-boundary entry is guarded. Probe exceptions are written to the separate research log when possible and never propagate. The probe does not call `EncounterRuntime.FaultRuntime` or any production fault/deactivation path.
