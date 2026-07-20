using ChaosEncounters.Combat.Mechanics;
using Kingmaker;
using Kingmaker.Controllers.TurnBased;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.GameModes;
using Kingmaker.PubSubSystem;
using Kingmaker.PubSubSystem.Core;

namespace ChaosEncounters.Combat;

internal sealed class EncounterRuntime :
    IPartyCombatHandler,
    IRoundStartHandler,
    IRoundEndHandler,
    ITurnStartHandler,
    ITurnEndHandler,
    IUnitDieHandler,
    IAnyUnitCombatHandler {
    private static readonly EncounterRuntime Instance = new();
    private static EncounterSession CurrentSession;
    private static List<BaseUnitEntity> PendingEnemyJoins;
    private static bool SessionActivated;
    private static bool DuplicateStartWarningLogged;
    private static bool RuntimeFaulted;
    private static bool RuntimeFaultLogged;

    internal static void Initialize() {
        if (!EventBus.IsGloballySubscribed(Instance)) {
            EventBus.Subscribe(Instance);
        }
    }

    public void HandleRoundStart(bool isTurnBased) {
        if (!isTurnBased ||
            RuntimeFaulted ||
            CurrentSession == null) {
            return;
        }

        try {
            Game game = Game.Instance;
            if (game?.TurnController?.TbActive != true ||
                game.CurrentMode == GameModeType.SpaceCombat ||
                game.CurrentMode == GameModeType.StarSystem) {
                return;
            }

            int combatRound = game.TurnController.CombatRound;
            if (!SessionActivated) {
                SessionActivated = true;
                EncounterMechanicController.Activate(
                    CurrentSession);
                ForwardPendingEnemyJoins();
            }

            EncounterMechanicController.HandleRoundStart(
                combatRound);
        } catch (Exception exception) {
            FaultRuntime(nameof(HandleRoundStart), exception);
        }
    }

    public void HandleRoundEnd(bool isTurnBased, bool isFirst) {
        if (!isTurnBased ||
            isFirst ||
            RuntimeFaulted ||
            CurrentSession == null ||
            !SessionActivated) {
            return;
        }

        try {
            Game game = Game.Instance;
            if (game?.TurnController?.TbActive != true ||
                game.CurrentMode == GameModeType.SpaceCombat ||
                game.CurrentMode == GameModeType.StarSystem) {
                return;
            }

            int combatRound = game.TurnController.CombatRound;
            EncounterMechanicController.HandleRoundEnd(
                combatRound);
        } catch (Exception exception) {
            FaultRuntime(nameof(HandleRoundEnd), exception);
        }
    }

    public void HandleUnitStartTurn(bool isTurnBased) {
        if (!isTurnBased ||
            RuntimeFaulted ||
            CurrentSession == null ||
            !SessionActivated ||
            EventInvokerExtensions.MechanicEntity is not BaseUnitEntity unit ||
            !unit.IsInCombat) {
            return;
        }

        try {
            int combatRound =
                Game.Instance.TurnController.CombatRound;
            EncounterMechanicController.HandleUnitTurnStart(
                unit,
                combatRound);
        } catch (Exception exception) {
            FaultRuntime(
                nameof(HandleUnitStartTurn),
                exception);
        }
    }

    public void HandleUnitEndTurn(bool isTurnBased) {
        if (!isTurnBased ||
            RuntimeFaulted ||
            CurrentSession == null ||
            !SessionActivated ||
            EventInvokerExtensions.MechanicEntity is not BaseUnitEntity unit ||
            !unit.IsInCombat) {
            return;
        }

        try {
            int combatRound =
                Game.Instance.TurnController.CombatRound;
            EncounterMechanicController.HandleUnitTurnEnd(
                unit,
                combatRound);
        } catch (Exception exception) {
            FaultRuntime(
                nameof(HandleUnitEndTurn),
                exception);
        }
    }

    public void OnUnitDie() {
        if (RuntimeFaulted ||
            CurrentSession == null ||
            !SessionActivated) {
            return;
        }

        try {
            if (EventInvokerExtensions.AbstractUnitEntity is not BaseUnitEntity unit ||
                !unit.IsInCombat ||
                !unit.IsPlayerEnemy ||
                !unit.LifeState.IsDead) {
                return;
            }

            int combatRound =
                Game.Instance.TurnController.CombatRound;
            EncounterMechanicController.HandleEnemyDeath(
                unit,
                combatRound);
        } catch (Exception exception) {
            FaultRuntime(nameof(OnUnitDie), exception);
        }
    }

    public void HandleUnitJoinCombat(BaseUnitEntity unit) {
        if (RuntimeFaulted ||
            CurrentSession == null ||
            (SessionActivated &&
             !EncounterMechanicController
                 .IsExecutionListActive) ||
            unit == null ||
            unit.IsDisposed ||
            unit.LifeState == null ||
            unit.LifeState.IsDead ||
            !unit.IsInCombat ||
            !unit.IsPlayerEnemy) {
            return;
        }

        try {
            if (SessionActivated) {
                EncounterMechanicController
                    .HandleEnemyJoinCombat(unit);
                return;
            }

            List<BaseUnitEntity> pendingEnemyJoins =
                PendingEnemyJoins;
            if (pendingEnemyJoins == null) {
                pendingEnemyJoins =
                    new List<BaseUnitEntity>();
                PendingEnemyJoins = pendingEnemyJoins;
            }
            for (int index = 0;
                 index < pendingEnemyJoins.Count;
                 index++) {
                if (ReferenceEquals(
                        pendingEnemyJoins[index],
                        unit)) {
                    return;
                }
            }
            pendingEnemyJoins.Add(unit);
        } catch (Exception exception) {
            FaultRuntime(
                nameof(HandleUnitJoinCombat),
                exception);
        }
    }

    public void HandleUnitLeaveCombat(BaseUnitEntity unit) {
    }

    public void HandlePartyCombatStateChanged(bool inCombat) {
        if (!inCombat) {
            HandleCombatEnd();
            return;
        }

        if (RuntimeFaulted) {
            return;
        }

        try {
            if (CurrentSession != null) {
                if (!DuplicateStartWarningLogged) {
                    Main.LogWarning("Combat-start callback was received while an encounter session already exists; the initial immutable session was preserved.");
                    DuplicateStartWarningLogged = true;
                }
                return;
            }

            Game game = Game.Instance;
            if (game.CurrentMode == GameModeType.SpaceCombat ||
                game.CurrentMode == GameModeType.StarSystem) {
                return;
            }

            SessionActivated = false;
            PendingEnemyJoins = null;
            DuplicateStartWarningLogged = false;
            RuntimeFaulted = false;
            RuntimeFaultLogged = false;

            var initialEnemies = new List<BaseUnitEntity>();
            foreach (BaseUnitEntity unit in
                game.State.AllBaseAwakeUnitsForSure) {
                if (unit != null &&
                    unit.IsInCombat &&
                    unit.IsPlayerEnemy) {
                    initialEnemies.Add(unit);
                }
            }

            EncounterClassifier.Classify(
                initialEnemies,
                out EncounterType encounterType,
                out BaseUnitEntity leader,
                out _,
                out _,
                out string invalidCompositionReason);
            CurrentSession = new EncounterSession(
                initialEnemies,
                encounterType,
                leader);

            Main.LogInfo(
                $"Encounter classified: EncounterType={CurrentSession.Type} " +
                $"InitialEnemyCount={CurrentSession.InitialEnemies.Count} " +
                $"LeaderName={leader?.CharacterName ?? "None"} " +
                $"LeaderBlueprint={leader?.Blueprint?.name ?? "None"}");
            if (invalidCompositionReason != null) {
                Main.LogWarning(
                    $"Encounter classification composition warning: " +
                    invalidCompositionReason);
            }
        } catch (Exception exception) {
            FaultRuntime(nameof(HandlePartyCombatStateChanged), exception);
        }
    }

    private static void HandleCombatEnd() {
        try {
            if (CurrentSession != null) {
                Main.LogInfo("Encounter session ended.");
            }
        } catch (Exception exception) {
            FaultRuntime(
                $"{nameof(HandlePartyCombatStateChanged)}(false)",
                exception);
        } finally {
            ClearRuntimeState();
        }
    }

    private static void ForwardPendingEnemyJoins() {
        List<BaseUnitEntity> pendingEnemyJoins =
            PendingEnemyJoins;
        PendingEnemyJoins = null;
        if (pendingEnemyJoins == null ||
            !EncounterMechanicController
                .IsExecutionListActive) {
            return;
        }

        for (int index = 0;
             index < pendingEnemyJoins.Count;
             index++) {
            EncounterMechanicController
                .HandleEnemyJoinCombat(
                    pendingEnemyJoins[index]);
        }
    }

    private static void FaultRuntime(
        string callbackName,
        Exception exception) {
        EncounterMechanicController.Deactivate(
            EncounterMechanicEndReason.RuntimeFault);
        CurrentSession = null;
        PendingEnemyJoins = null;
        SessionActivated = false;
        RuntimeFaulted = true;

        if (!RuntimeFaultLogged) {
            RuntimeFaultLogged = true;
            Main.LogError(
                $"Encounter runtime faulted in {callbackName}: {exception}");
        }
    }

    private static void ClearRuntimeState() {
        EncounterMechanicController.Deactivate(
            EncounterMechanicEndReason.CombatEnded);
        CurrentSession = null;
        PendingEnemyJoins = null;
        SessionActivated = false;
        DuplicateStartWarningLogged = false;
        RuntimeFaulted = false;
        RuntimeFaultLogged = false;
    }
}
