using ChaosEncounters.Combat.Mechanics;
using ChaosEncounters.Combat.Persistence;
using ChaosEncounters.UI;
using Kingmaker;
using Kingmaker.Controllers.TurnBased;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.GameModes;
using Kingmaker.PubSubSystem;
using Kingmaker.PubSubSystem.Core;

namespace ChaosEncounters.Combat;

internal sealed class EncounterRuntime :
    IPartyCombatHandler,
    IPreparationTurnBeginHandler,
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
    private static bool ActivationDeferredForLoadedState = true;
    private static EncounterSaveLifecycle? Lifecycle;

    internal static void Initialize() {
        if (!EventBus.IsGloballySubscribed(Instance)) {
            EventBus.Subscribe(Instance);
        }
    }

    public void HandleRoundStart(bool isTurnBased) {
        if (!isTurnBased ||
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

            EncounterMechanicController.HandleRoundStart(
                game.TurnController.CombatRound);
        } catch (Exception exception) {
            FaultRuntime(nameof(HandleRoundStart), exception);
        }
    }

    public void HandleBeginPreparationTurn(bool canDeploy) {
        if (RuntimeFaulted ||
            CurrentSession == null ||
            SessionActivated) {
            return;
        }

        try {
            TryActivatePendingEncounter();
        } catch (Exception exception) {
            FaultRuntime(
                nameof(HandleBeginPreparationTurn),
                exception);
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
            CurrentSession == null) {
            return;
        }

        try {
            if (!SessionActivated) {
                TryActivatePendingEncounter();
            }
            if (!SessionActivated ||
                EventInvokerExtensions.MechanicEntity is not BaseUnitEntity unit ||
                !unit.IsInCombat) {
                return;
            }

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
                 .HasEnemyJoinAwareMechanic) ||
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
                    .HandleEnemyJoined(unit);
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

        if (CurrentSession == null &&
            SessionActivated &&
            Lifecycle.HasValue &&
            Lifecycle !=
                EncounterSaveLifecycle.PendingActivation) {
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
                if (IsValidInitialEnemy(unit)) {
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
            Lifecycle = EncounterSaveLifecycle.PendingActivation;

            Main.LogInfo(
                $"Encounter detected (provisional): " +
                $"EligibleEncounterTypes={CurrentSession.Type} " +
                $"EnemyCount={CurrentSession.InitialEnemies.Count} " +
                $"LeaderName={leader?.CharacterName ?? "None"} " +
                $"LeaderBlueprint={leader?.Blueprint?.name ?? "None"}");
            if (invalidCompositionReason != null) {
                Main.LogWarning(
                    $"Provisional encounter classification composition warning: " +
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
            ClearRuntimeState(
                EncounterMechanicEndReason.CombatEnded);
        }
    }

    internal static void MarkDisabledForCurrentCombat() {
        if (CurrentSession == null) {
            return;
        }

        SessionActivated = true;
        Lifecycle = EncounterSaveLifecycle.DisabledForCombat;
    }

    internal static bool TryCreateSaveRecord(
        out EncounterSaveRecord record,
        out string failureReason) {
        record = null;
        failureReason = null;
        EncounterSession session = CurrentSession;
        if (session == null) {
            return false;
        }
        if (!Lifecycle.HasValue) {
            failureReason =
                "an encounter session exists without a lifecycle";
            return false;
        }

        EncounterSaveLifecycle lifecycle = Lifecycle.Value;
        if ((lifecycle ==
             EncounterSaveLifecycle.PendingActivation) ==
            SessionActivated) {
            failureReason =
                "the encounter activation flag does not match its lifecycle";
            return false;
        }

        string mechanicId =
            EncounterMechanicController.ActiveMechanicId;
        if (lifecycle == EncounterSaveLifecycle.Active) {
            if (string.IsNullOrWhiteSpace(mechanicId)) {
                failureReason =
                    "the active encounter has no mechanic ID";
                return false;
            }
        } else if (mechanicId != null) {
            failureReason =
                "a non-active encounter still owns a mechanic";
            return false;
        }

        record = new EncounterSaveRecord {
            SchemaVersion =
                EncounterSaveRecord.CurrentSchemaVersion,
            Lifecycle = lifecycle
        };

        switch (lifecycle) {
            case EncounterSaveLifecycle.PendingActivation:
                if (!TryCreatePendingActivationSaveData(
                        session,
                        out PendingActivationSaveData pending,
                        out failureReason)) {
                    record = null;
                    return false;
                }
                record.PendingActivation = pending;
                return true;
            case EncounterSaveLifecycle.Active:
                var mechanicData =
                    new EncounterMechanicSaveData();
                if (!EncounterMechanicController
                    .TryCaptureActiveMechanicData(
                        mechanicData,
                        out failureReason)) {
                    record = null;
                    return false;
                }
                record.MechanicId = mechanicId;
                record.MechanicData = mechanicData;
                return true;
            case EncounterSaveLifecycle.NoCompatibleCandidate:
            case EncounterSaveLifecycle.DisabledForCombat:
            case EncounterSaveLifecycle.RuntimeFaulted:
                return true;
            default:
                record = null;
                failureReason =
                    $"unsupported encounter lifecycle {(int)lifecycle}";
                return false;
        }
    }

    internal static void RestorePendingActivation(
        EncounterSaveRecord record,
        PendingActivationSaveData pending,
        List<BaseUnitEntity> initialEnemies,
        BaseUnitEntity leader,
        List<BaseUnitEntity> pendingEnemyJoins) {
        if (record == null ||
            record.Lifecycle !=
                EncounterSaveLifecycle.PendingActivation) {
            throw new ArgumentException(
                "A pending-activation record is required.",
                nameof(record));
        }
        if (pending == null) {
            throw new ArgumentNullException(nameof(pending));
        }
        if (initialEnemies == null) {
            throw new ArgumentNullException(nameof(initialEnemies));
        }
        if (pendingEnemyJoins == null) {
            throw new ArgumentNullException(
                nameof(pendingEnemyJoins));
        }

        EncounterMechanicController.Deactivate(
            EncounterMechanicEndReason.LoadedStateReplaced);
        CurrentSession = new EncounterSession(
            initialEnemies,
            pending.EncounterType,
            leader);
        PendingEnemyJoins = pendingEnemyJoins.Count == 0
            ? null
            : pendingEnemyJoins;
        SessionActivated = false;
        DuplicateStartWarningLogged = false;
        RuntimeFaulted = false;
        RuntimeFaultLogged = false;
        Lifecycle = EncounterSaveLifecycle.PendingActivation;
    }

    internal static bool TryGetLoadedProvisionalSession(
        out EncounterSession session,
        out string failureReason) {
        session = CurrentSession;
        if (session == null) {
            failureReason =
                "The loaded provisional encounter session is unavailable.";
            return false;
        }

        failureReason = null;
        return true;
    }

    internal static EncounterMechanicRestoreStatus
        RestoreActiveMechanic(
        EncounterSaveRecord record,
        EncounterSession session,
        EncounterRestoreContext context,
        out string failureReason) {
        if (record == null ||
            record.Lifecycle != EncounterSaveLifecycle.Active ||
            session == null ||
            context == null ||
            !ReferenceEquals(CurrentSession, session)) {
            failureReason =
                "The active record does not match the loaded provisional encounter session.";
            SuppressLoadedCombat();
            return EncounterMechanicRestoreStatus.Invalid;
        }

        EncounterMechanicRestoreStatus status =
            EncounterMechanicController
                .TryRestoreActiveMechanic(
                    record.MechanicId,
                    session,
                    context,
                    record.MechanicData,
                    out failureReason);
        PendingEnemyJoins = null;
        SessionActivated = true;
        DuplicateStartWarningLogged = false;
        RuntimeFaulted = false;
        RuntimeFaultLogged = false;
        Lifecycle = status ==
            EncounterMechanicRestoreStatus.Restored
            ? EncounterSaveLifecycle.Active
            : EncounterSaveLifecycle.DisabledForCombat;

        return status;
    }

    internal static void RestoreNonActiveLifecycle(
        EncounterSaveLifecycle lifecycle) {
        if (lifecycle !=
                EncounterSaveLifecycle.NoCompatibleCandidate &&
            lifecycle !=
                EncounterSaveLifecycle.DisabledForCombat &&
            lifecycle !=
                EncounterSaveLifecycle.RuntimeFaulted) {
            throw new ArgumentOutOfRangeException(
                nameof(lifecycle),
                lifecycle,
                "A terminal encounter lifecycle is required.");
        }

        EncounterSession provisionalSession =
            IsValidLoadedSession(CurrentSession)
                ? CurrentSession
                : null;
        EncounterMechanicController.Deactivate(
            EncounterMechanicEndReason.LoadedStateReplaced);
        CurrentSession = provisionalSession;
        PendingEnemyJoins = null;
        SessionActivated = true;
        DuplicateStartWarningLogged = false;
        RuntimeFaulted = lifecycle ==
            EncounterSaveLifecycle.RuntimeFaulted;
        RuntimeFaultLogged = false;
        Lifecycle = lifecycle;
    }

    internal static void SuppressLoadedCombat() {
        EncounterSession provisionalSession =
            IsValidLoadedSession(CurrentSession)
                ? CurrentSession
                : null;
        EncounterMechanicController.Deactivate(
            EncounterMechanicEndReason.LoadedStateReplaced);
        CurrentSession = provisionalSession;
        PendingEnemyJoins = null;
        SessionActivated = true;
        DuplicateStartWarningLogged = false;
        RuntimeFaulted = false;
        RuntimeFaultLogged = false;
        Lifecycle = EncounterSaveLifecycle.DisabledForCombat;
    }

    internal static void ResetForAreaUnload() {
        ActivationDeferredForLoadedState = true;
        try {
            ClearRuntimeState(
                EncounterMechanicEndReason.AreaUnloading);
        } finally {
            DamageControl.ClearAllPolicies();
            UnitMarker.ResetForAreaUnload();
            EncounterHud.ResetForAreaUnload();
        }
    }

    internal static void CompleteLoadedStateProcessing() {
        ActivationDeferredForLoadedState = false;
        if (RuntimeFaulted ||
            CurrentSession == null ||
            SessionActivated ||
            Lifecycle !=
                EncounterSaveLifecycle.PendingActivation ||
            Game.Instance?.TurnController
                ?.IsPreparationTurn != true) {
            return;
        }

        try {
            TryActivatePendingEncounter();
        } catch (Exception exception) {
            FaultRuntime(
                nameof(CompleteLoadedStateProcessing),
                exception);
        }
    }

    internal static void ResetForLoadedState() {
        ClearRuntimeState(
            EncounterMechanicEndReason.LoadedStateReplaced);
    }

    private static bool TryCreatePendingActivationSaveData(
        EncounterSession session,
        out PendingActivationSaveData pending,
        out string failureReason) {
        pending = null;
        failureReason = null;
        if (session.InitialEnemies.Count == 0 ||
            session.InitialEnemies.Count >
                EncounterPersistenceValidation.MaximumEntityCount) {
            failureReason =
                "the pending encounter initial snapshot has an invalid size";
            return false;
        }

        var initialEnemyIds = new List<string>(
            session.InitialEnemies.Count);
        var allIds = new HashSet<string>(
            StringComparer.Ordinal);
        bool leaderMatchesSession = session.Leader == null;
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity unit = session.InitialEnemies[index];
            string id = unit?.UniqueId;
            if (!EncounterPersistenceValidation
                    .IsValidEntityId(id)) {
                failureReason =
                    $"initial enemy at index {index} has an invalid persistent ID";
                return false;
            }
            if (!allIds.Add(id)) {
                failureReason =
                    $"initial enemy at index {index} has a duplicate persistent ID";
                return false;
            }
            initialEnemyIds.Add(id);
            if (ReferenceEquals(unit, session.Leader)) {
                leaderMatchesSession = true;
            }
        }

        string leaderId = session.Leader?.UniqueId;
        if (session.Leader != null) {
            if (!EncounterPersistenceValidation
                    .IsValidEntityId(leaderId)) {
                failureReason =
                    "the encounter leader has an invalid persistent ID";
                return false;
            }
            if (!leaderMatchesSession ||
                !allIds.Contains(leaderId)) {
                failureReason =
                    "the encounter leader does not match the initial snapshot";
                return false;
            }
        }

        List<BaseUnitEntity> pendingEnemyJoins =
            PendingEnemyJoins;
        int pendingCount = pendingEnemyJoins?.Count ?? 0;
        if (pendingCount >
            EncounterPersistenceValidation.MaximumEntityCount) {
            failureReason =
                "the pending enemy snapshot exceeds the persistence limit";
            return false;
        }

        var pendingEnemyJoinIds = new List<string>(
            pendingCount);
        for (int index = 0;
             index < pendingCount;
             index++) {
            string id = pendingEnemyJoins[index]?.UniqueId;
            if (!EncounterPersistenceValidation
                    .IsValidEntityId(id)) {
                failureReason =
                    $"pending enemy at index {index} has an invalid persistent ID";
                return false;
            }
            if (!allIds.Add(id)) {
                failureReason =
                    $"pending enemy at index {index} duplicates another persisted entity ID";
                return false;
            }
            pendingEnemyJoinIds.Add(id);
        }

        pending = new PendingActivationSaveData {
            EncounterType = session.Type,
            LeaderId = leaderId,
            InitialEnemyIds = initialEnemyIds,
            PendingEnemyJoinIds = pendingEnemyJoinIds
        };
        return true;
    }

    private static bool IsValidLoadedSession(
        EncounterSession session) {
        if (session == null ||
            session.InitialEnemies.Count == 0) {
            return false;
        }

        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity unit = session.InitialEnemies[index];
            if (unit == null ||
                unit is StarshipEntity ||
                unit.IsDisposed ||
                !unit.IsInGame ||
                !unit.IsInCombat ||
                !unit.IsPlayerEnemy ||
                unit.LifeState == null ||
                !EncounterPersistenceValidation
                    .IsValidEntityId(unit.UniqueId)) {
                return false;
            }
        }

        return true;
    }

    private static bool TryActivatePendingEncounter() {
        if (ActivationDeferredForLoadedState ||
            RuntimeFaulted ||
            CurrentSession == null ||
            SessionActivated ||
            Lifecycle !=
                EncounterSaveLifecycle.PendingActivation) {
            return false;
        }

        Game game = Game.Instance;
        if (game?.Player?.IsInCombat != true ||
            game?.TurnController?.TbActive != true ||
            game.CurrentMode == GameModeType.SpaceCombat ||
            game.CurrentMode == GameModeType.StarSystem) {
            return false;
        }
        IEnumerable<BaseUnitEntity> awakeUnits =
            game.State?.AllBaseAwakeUnitsForSure;
        if (awakeUnits == null) {
            throw new InvalidOperationException(
                "The awake-unit collection is unavailable at encounter activation.");
        }

        var initialEnemies = new List<BaseUnitEntity>();
        foreach (BaseUnitEntity unit in awakeUnits) {
            if (IsValidInitialEnemy(unit)) {
                initialEnemies.Add(unit);
            }
        }

        List<BaseUnitEntity> pendingEnemyJoins =
            PendingEnemyJoins;
        PendingEnemyJoins = null;
        if (pendingEnemyJoins != null) {
            for (int pendingIndex = 0;
                 pendingIndex < pendingEnemyJoins.Count;
                 pendingIndex++) {
                BaseUnitEntity pendingEnemy =
                    pendingEnemyJoins[pendingIndex];
                if (!IsValidInitialEnemy(pendingEnemy)) {
                    continue;
                }

                bool alreadyIncluded = false;
                for (int enemyIndex = 0;
                     enemyIndex < initialEnemies.Count;
                     enemyIndex++) {
                    if (ReferenceEquals(
                            initialEnemies[enemyIndex],
                            pendingEnemy)) {
                        alreadyIncluded = true;
                        break;
                    }
                }
                if (!alreadyIncluded) {
                    initialEnemies.Add(pendingEnemy);
                }
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
            $"Encounter classified: EligibleEncounterTypes={CurrentSession.Type} " +
            $"InitialEnemyCount={CurrentSession.InitialEnemies.Count} " +
            $"LeaderName={leader?.CharacterName ?? "None"} " +
            $"LeaderBlueprint={leader?.Blueprint?.name ?? "None"}");
        if (invalidCompositionReason != null) {
            Main.LogWarning(
                $"Encounter classification composition warning: " +
                invalidCompositionReason);
        }

        SessionActivated = true;
        EncounterMechanicController.Activate(CurrentSession);
        Lifecycle = EncounterMechanicController
            .HasActiveMechanic
            ? EncounterSaveLifecycle.Active
            : EncounterSaveLifecycle.NoCompatibleCandidate;
        return true;
    }

    private static bool IsValidInitialEnemy(
        BaseUnitEntity unit) {
        return unit != null &&
               unit is not StarshipEntity &&
               !unit.IsDisposed &&
               unit.IsInGame &&
               unit.IsInCombat &&
               unit.IsPlayerEnemy &&
               unit.LifeState != null &&
               !unit.LifeState.IsDead &&
               !unit.LifeState.IsFinallyDead;
    }

    private static void FaultRuntime(
        string callbackName,
        Exception exception) {
        EncounterMechanicController.Deactivate(
            EncounterMechanicEndReason.RuntimeFault);
        SessionActivated = true;
        RuntimeFaulted = true;
        Lifecycle = EncounterSaveLifecycle.RuntimeFaulted;

        if (!RuntimeFaultLogged) {
            RuntimeFaultLogged = true;
            Main.LogError(
                $"Encounter runtime faulted in {callbackName}: {exception}");
        }
    }

    private static void ClearRuntimeState(
        EncounterMechanicEndReason reason) {
        EncounterMechanicController.Deactivate(
            reason);
        CurrentSession = null;
        PendingEnemyJoins = null;
        SessionActivated = false;
        DuplicateStartWarningLogged = false;
        RuntimeFaulted = false;
        RuntimeFaultLogged = false;
        Lifecycle = null;
    }
}
