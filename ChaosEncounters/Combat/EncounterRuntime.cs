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
    private static EncounterSaveLifecycle? Lifecycle;

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
                if (Lifecycle ==
                    EncounterSaveLifecycle.PendingActivation) {
                    Lifecycle = EncounterMechanicController
                        .HasActiveMechanic
                        ? EncounterSaveLifecycle.Active
                        : EncounterSaveLifecycle
                            .NoCompatibleCandidate;
                }
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
            Lifecycle ==
                EncounterSaveLifecycle.DisabledForCombat) {
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
            Lifecycle = EncounterSaveLifecycle.PendingActivation;

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
            if (string.IsNullOrWhiteSpace(id)) {
                failureReason =
                    $"initial enemy at index {index} has no persistent ID";
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
        if (initialEnemyIds.Count == 0) {
            failureReason =
                "the encounter session has no initial enemies";
            return false;
        }

        string leaderId = session.Leader?.UniqueId;
        if (session.Leader != null) {
            if (string.IsNullOrWhiteSpace(leaderId)) {
                failureReason =
                    "the encounter leader has no persistent ID";
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
        var pendingEnemyJoinIds = new List<string>(
            pendingCount);
        for (int index = 0;
             index < pendingCount;
             index++) {
            BaseUnitEntity unit = pendingEnemyJoins[index];
            string id = unit?.UniqueId;
            if (string.IsNullOrWhiteSpace(id)) {
                failureReason =
                    $"pending enemy at index {index} has no persistent ID";
                return false;
            }
            if (!allIds.Add(id)) {
                failureReason =
                    $"pending enemy at index {index} duplicates another persisted entity ID";
                return false;
            }
            pendingEnemyJoinIds.Add(id);
        }

        ExecutionListSaveRecipe executionListRecipe = null;
        if (lifecycle == EncounterSaveLifecycle.Active &&
            EncounterMechanicController
                .IsExecutionListMechanicId(mechanicId) &&
            !EncounterMechanicController
                .TryCreateExecutionListSaveRecipe(
                    out executionListRecipe,
                    out failureReason)) {
            return false;
        }

        record = new EncounterSaveRecord {
            SchemaVersion =
                EncounterSaveRecord.CurrentSchemaVersion,
            Lifecycle = lifecycle,
            EncounterType = session.Type,
            MechanicId = lifecycle ==
                EncounterSaveLifecycle.Active
                ? mechanicId
                : null,
            LeaderId = leaderId,
            InitialEnemyIds = initialEnemyIds,
            PendingEnemyJoinIds = pendingEnemyJoinIds,
            ExecutionListRecipe = executionListRecipe
        };
        return true;
    }

    internal static void RestoreSaveRecord(
        EncounterSaveRecord record,
        List<BaseUnitEntity> initialEnemies,
        BaseUnitEntity leader,
        List<BaseUnitEntity> pendingEnemyJoins) {
        if (record == null) {
            throw new ArgumentNullException(nameof(record));
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
            record.EncounterType,
            leader);
        PendingEnemyJoins = pendingEnemyJoins.Count == 0
            ? null
            : pendingEnemyJoins;
        DuplicateStartWarningLogged = false;
        RuntimeFaultLogged = false;

        if (record.Lifecycle ==
            EncounterSaveLifecycle.PendingActivation) {
            SessionActivated = false;
            RuntimeFaulted = false;
            Lifecycle = EncounterSaveLifecycle.PendingActivation;
            return;
        }

        SessionActivated = true;
        RuntimeFaulted = record.Lifecycle ==
            EncounterSaveLifecycle.RuntimeFaulted;
        Lifecycle = record.Lifecycle ==
            EncounterSaveLifecycle.Active
            ? EncounterSaveLifecycle.DisabledForCombat
            : record.Lifecycle;
    }

    internal static bool TryRestoreExecutionList(
        ExecutionListSaveRecipe recipe,
        out bool disabledInSettings,
        out string failureReason) {
        disabledInSettings = false;
        failureReason = null;
        EncounterSession session = CurrentSession;
        if (session == null) {
            failureReason =
                "The restored encounter session is unavailable.";
            Lifecycle = EncounterSaveLifecycle.DisabledForCombat;
            return false;
        }

        bool restored = EncounterMechanicController
            .TryRestoreExecutionList(
                session,
                recipe,
                out disabledInSettings,
                out failureReason);
        SessionActivated = true;
        RuntimeFaulted = false;
        Lifecycle = restored
            ? EncounterSaveLifecycle.Active
            : EncounterSaveLifecycle.DisabledForCombat;
        return restored;
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
        try {
            ClearRuntimeState(
                EncounterMechanicEndReason.AreaUnloading);
        } finally {
            DamageControl.ClearAllPolicies();
            UnitMarker.ResetForAreaUnload();
            EncounterHud.ResetForAreaUnload();
        }
    }

    internal static void ResetForLoadedState() {
        ClearRuntimeState(
            EncounterMechanicEndReason.LoadedStateReplaced);
    }

    private static bool IsValidLoadedSession(
        EncounterSession session) {
        if (session == null ||
            session.InitialEnemies.Count == 0) {
            return false;
        }

        bool leaderFound = session.Leader == null;
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity unit = session.InitialEnemies[index];
            if (unit == null ||
                unit is StarshipEntity ||
                unit.IsDisposed ||
                !unit.IsInGame ||
                !unit.IsPlayerEnemy ||
                string.IsNullOrWhiteSpace(unit.UniqueId)) {
                return false;
            }
            if (ReferenceEquals(unit, session.Leader)) {
                leaderFound = true;
            }
        }

        return leaderFound;
    }

    private static void ForwardPendingEnemyJoins() {
        List<BaseUnitEntity> pendingEnemyJoins =
            PendingEnemyJoins;
        PendingEnemyJoins = null;
        if (pendingEnemyJoins == null ||
            !EncounterMechanicController
                .HasEnemyJoinAwareMechanic) {
            return;
        }

        for (int index = 0;
             index < pendingEnemyJoins.Count;
             index++) {
            EncounterMechanicController
                .HandleEnemyJoined(
                    pendingEnemyJoins[index]);
        }
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
