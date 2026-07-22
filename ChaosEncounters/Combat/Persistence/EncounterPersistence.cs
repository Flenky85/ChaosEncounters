using HarmonyLib;
using ChaosEncounters.Combat.Mechanics;
using Kingmaker;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Persistence;
using Kingmaker.GameModes;
using Kingmaker.PubSubSystem.Core;
using Newtonsoft.Json;

namespace ChaosEncounters.Combat.Persistence;

internal sealed class EncounterPersistence :
    IAreaHandler,
    IAreaLoadingStagesHandler {
    private const string SaveKey = "ChaosEncounters.SaveState";
    private const string HarmonyId = "ChaosEncounters.Persistence";

    private static readonly EncounterPersistence Instance = new();
    private static readonly JsonSerializerSettings SerializerSettings =
        new() {
            TypeNameHandling = TypeNameHandling.None,
            Formatting = Formatting.None
        };

    private static bool SaveHookInstalled;
    private static bool Initialized;

    internal static void Initialize() {
        if (Initialized) {
            return;
        }

        if (!SaveHookInstalled) {
            new Harmony(HarmonyId)
                .CreateClassProcessor(
                    typeof(SaveManagerPreSavePatch))
                .Patch();
            SaveHookInstalled = true;
        }

        if (!EventBus.IsGloballySubscribed(Instance)) {
            EventBus.Subscribe(Instance);
        }

        Initialized = true;
        Main.LogInfo("Encounter persistence coordinator initialized.");
    }

    internal static void TryWriteSaveRecord() {
        Dictionary<string, object> list;
        try {
            list = Game.Instance?.State?.InGameSettings?.List;
        } catch (Exception exception) {
            Main.LogError(
                $"Encounter save state could not access InGameSettings: {exception}");
            return;
        }

        if (list == null) {
            Main.LogWarning(
                "Encounter save state was skipped because InGameSettings.List is unavailable.");
            return;
        }

        try {
            if (!EncounterRuntime.TryCreateSaveRecord(
                    out EncounterSaveRecord record,
                    out string failureReason)) {
                if (failureReason != null) {
                    Main.LogError(
                        $"Encounter save state export failed: {failureReason}");
                    RemoveSaveKey(list);
                    return;
                }
                RemoveSaveKey(list);
                Main.LogInfo(
                    "Encounter save key removed because no persistable encounter exists.");
                return;
            }

            string json = JsonConvert.SerializeObject(
                record,
                SerializerSettings);
            list[SaveKey] = json;
            Main.LogInfo(
                $"Encounter save record written: Lifecycle={record.Lifecycle} " +
                $"MechanicId={record.MechanicId ?? "None"}");
        } catch (Exception exception) {
            Main.LogError(
                $"Encounter save record serialization failed: {exception}");
            RemoveSaveKey(list);
        }
    }

    public void OnAreaBeginUnloading() {
        try {
            EncounterRuntime.ResetForAreaUnload();
            Main.LogInfo(
                "Old encounter runtime cleared during area unload.");
        } catch (Exception exception) {
            Main.LogError(
                $"Encounter area-unload cleanup failed: {exception}");
        }
    }

    public void OnAreaDidLoad() {
    }

    public void OnAreaScenesLoaded() {
    }

    public void OnAreaLoadingComplete() {
        try {
            ProcessLoadedState();
        } catch (Exception exception) {
            Main.LogError(
                $"Encounter loaded-state processing failed: {exception}");
            TrySuppressLoadedCombat(
                "unexpected loaded-state processing failure");
        }
    }

    private static void ProcessLoadedState() {
        Game game = Game.Instance;
        Dictionary<string, object> list =
            game?.State?.InGameSettings?.List;
        bool loadedTerrestrialCombat =
            IsLoadedTerrestrialCombat(game);

        if (list == null ||
            !list.TryGetValue(SaveKey, out object rawValue)) {
            if (loadedTerrestrialCombat) {
                EncounterRuntime.SuppressLoadedCombat();
                Main.LogWarning(
                    "Loaded combat has no Chaos Encounters save state; " +
                    "the combat was suppressed because the save predates combat-state persistence.");
            }
            return;
        }

        if (rawValue is not string json ||
            string.IsNullOrWhiteSpace(json)) {
            SuppressInvalidLoadedState(
                "the save entry is not a nonempty JSON string");
            return;
        }

        EncounterSaveRecord record;
        try {
            record = JsonConvert.DeserializeObject<EncounterSaveRecord>(
                json,
                SerializerSettings);
        } catch (Exception exception) {
            SuppressInvalidLoadedState(
                $"JSON parsing failed: {exception.Message}");
            return;
        }

        if (!TryValidateRecord(record, out string failureReason)) {
            SuppressInvalidLoadedState(failureReason);
            return;
        }
        if (!loadedTerrestrialCombat) {
            SuppressInvalidLoadedState(
                "the record does not correspond to a loaded terrestrial combat");
            return;
        }

        Main.LogInfo(
            $"Encounter loaded record found and parsed: Lifecycle={record.Lifecycle} " +
            $"MechanicId={record.MechanicId ?? "None"}");

        switch (record.Lifecycle) {
            case EncounterSaveLifecycle.PendingActivation:
                if (!TryResolvePendingActivation(
                        record.PendingActivation,
                        out List<BaseUnitEntity> initialEnemies,
                        out BaseUnitEntity leader,
                        out List<BaseUnitEntity> pendingEnemyJoins,
                        out failureReason)) {
                    SuppressInvalidLoadedState(failureReason);
                    return;
                }
                EncounterRuntime.RestorePendingActivation(
                    record,
                    initialEnemies,
                    leader,
                    pendingEnemyJoins);
                Main.LogInfo(
                    "Pending encounter activation restored; normal first-round selection remains pending.");
                break;
            case EncounterSaveLifecycle.Active:
                if (!EncounterRuntime
                    .TryGetLoadedProvisionalSession(
                        out EncounterSession session,
                        out failureReason) ||
                    !EncounterRestoreContext.TryCreate(
                        session,
                        out EncounterRestoreContext context,
                        out failureReason)) {
                    SuppressInvalidLoadedState(failureReason);
                    return;
                }

                EncounterMechanicRestoreStatus status =
                    EncounterRuntime.RestoreActiveMechanic(
                        record,
                        context,
                        out string restoreFailureReason);
                switch (status) {
                    case EncounterMechanicRestoreStatus.Restored:
                        Main.LogInfo(
                            $"Saved active mechanic restored: MechanicId={record.MechanicId}");
                        break;
                    case EncounterMechanicRestoreStatus.Unsupported:
                        Main.LogWarning(
                            $"Saved active mechanic was unsupported and suppressed: MechanicId={record.MechanicId}. {restoreFailureReason}");
                        break;
                    case EncounterMechanicRestoreStatus.DisabledInSettings:
                        Main.LogWarning(
                            $"Saved active mechanic was disabled in settings and suppressed: MechanicId={record.MechanicId}. {restoreFailureReason}");
                        break;
                    default:
                        Main.LogWarning(
                            $"Saved active mechanic was invalid and suppressed: MechanicId={record.MechanicId}. {restoreFailureReason}");
                        break;
                }
                break;
            default:
                EncounterRuntime.RestoreNonActiveLifecycle(
                    record.Lifecycle);
                Main.LogInfo(
                    $"Non-active encounter lifecycle restored: Lifecycle={record.Lifecycle}");
                break;
        }
    }

    private static bool TryValidateRecord(
        EncounterSaveRecord record,
        out string failureReason) {
        if (record == null) {
            failureReason = "the JSON root is null";
            return false;
        }
        if (record.SchemaVersion !=
            EncounterSaveRecord.CurrentSchemaVersion) {
            failureReason =
                $"unsupported schema version {record.SchemaVersion}";
            return false;
        }
        if (!IsSupportedLifecycle(record.Lifecycle)) {
            failureReason =
                $"unsupported lifecycle value {(int)record.Lifecycle}";
            return false;
        }
        if (!IsSupportedEncounterType(record.EncounterType)) {
            failureReason =
                $"invalid encounter type {(int)record.EncounterType}";
            return false;
        }
        switch (record.Lifecycle) {
            case EncounterSaveLifecycle.PendingActivation:
                if (record.MechanicId != null ||
                    record.PendingActivation == null ||
                    record.MechanicData != null) {
                    failureReason =
                        "the pending-activation record has an invalid lifecycle-specific shape";
                    return false;
                }
                if (!TryValidatePendingActivation(
                        record.PendingActivation,
                        out failureReason)) {
                    return false;
                }
                break;
            case EncounterSaveLifecycle.Active:
                if (!IsValidMechanicId(record.MechanicId) ||
                    record.PendingActivation != null ||
                    record.MechanicData == null) {
                    failureReason =
                        "the active record has an invalid lifecycle-specific shape";
                    return false;
                }
                break;
            case EncounterSaveLifecycle.NoCompatibleCandidate:
            case EncounterSaveLifecycle.DisabledForCombat:
            case EncounterSaveLifecycle.RuntimeFaulted:
                if (record.MechanicId != null ||
                    record.PendingActivation != null ||
                    record.MechanicData != null) {
                    failureReason =
                        "the non-active record has an invalid lifecycle-specific shape";
                    return false;
                }
                break;
        }

        failureReason = null;
        return true;
    }

    private static bool TryValidatePendingActivation(
        PendingActivationSaveData pending,
        out string failureReason) {
        if (pending.InitialEnemyIds == null ||
            pending.InitialEnemyIds.Count == 0 ||
            pending.InitialEnemyIds.Count >
                EncounterPersistenceValidation.MaximumEntityCount) {
            failureReason =
                "the initial-enemy ID list has an invalid size";
            return false;
        }
        if (pending.PendingEnemyJoinIds == null ||
            pending.PendingEnemyJoinIds.Count >
                EncounterPersistenceValidation.MaximumEntityCount) {
            failureReason =
                "the pending-enemy ID list has an invalid size";
            return false;
        }
        if (pending.LeaderId != null &&
            !EncounterPersistenceValidation
                .IsValidEntityId(pending.LeaderId)) {
            failureReason = "the leader ID is invalid";
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0;
             index < pending.InitialEnemyIds.Count;
             index++) {
            string id = pending.InitialEnemyIds[index];
            if (!EncounterPersistenceValidation
                    .IsValidEntityId(id)) {
                failureReason =
                    $"initial-enemy ID at index {index} is invalid";
                return false;
            }
            if (!ids.Add(id)) {
                failureReason =
                    $"initial-enemy ID at index {index} is duplicated";
                return false;
            }
        }
        if (pending.LeaderId != null &&
            !ids.Contains(pending.LeaderId)) {
            failureReason =
                "the leader ID is not part of the initial enemy snapshot";
            return false;
        }
        for (int index = 0;
             index < pending.PendingEnemyJoinIds.Count;
             index++) {
            string id = pending.PendingEnemyJoinIds[index];
            if (!EncounterPersistenceValidation
                    .IsValidEntityId(id)) {
                failureReason =
                    $"pending-enemy ID at index {index} is invalid";
                return false;
            }
            if (!ids.Add(id)) {
                failureReason =
                    $"pending-enemy ID at index {index} is duplicated or belongs to the initial snapshot";
                return false;
            }
        }

        failureReason = null;
        return true;
    }

    private static bool TryResolvePendingActivation(
        PendingActivationSaveData pending,
        out List<BaseUnitEntity> initialEnemies,
        out BaseUnitEntity leader,
        out List<BaseUnitEntity> pendingEnemyJoins,
        out string failureReason) {
        initialEnemies = new List<BaseUnitEntity>(
            pending.InitialEnemyIds.Count);
        leader = null;
        pendingEnemyJoins = new List<BaseUnitEntity>(
            pending.PendingEnemyJoinIds.Count);

        EntityService entityService = EntityService.Instance;
        if (entityService == null) {
            failureReason = "EntityService is unavailable";
            return false;
        }

        for (int index = 0;
             index < pending.InitialEnemyIds.Count;
             index++) {
            string id = pending.InitialEnemyIds[index];
            BaseUnitEntity unit =
                entityService.GetEntity<BaseUnitEntity>(id);
            if (!IsValidPendingEnemy(unit, id)) {
                failureReason =
                    $"initial enemy at index {index} could not be resolved as a valid loaded combat entity: Id={id}";
                return false;
            }
            initialEnemies.Add(unit);
            if (string.Equals(
                    pending.LeaderId,
                    id,
                    StringComparison.Ordinal)) {
                leader = unit;
            }
        }
        if (pending.LeaderId != null && leader == null) {
            failureReason =
                $"leader could not be resolved from the initial snapshot: Id={pending.LeaderId}";
            return false;
        }

        for (int index = 0;
             index < pending.PendingEnemyJoinIds.Count;
             index++) {
            string id = pending.PendingEnemyJoinIds[index];
            BaseUnitEntity unit =
                entityService.GetEntity<BaseUnitEntity>(id);
            if (!IsValidPendingEnemy(unit, id)) {
                failureReason =
                    $"pending enemy at index {index} could not be resolved as a valid loaded combat entity: Id={id}";
                return false;
            }
            pendingEnemyJoins.Add(unit);
        }

        failureReason = null;
        return true;
    }

    private static bool IsValidPendingEnemy(
        BaseUnitEntity unit,
        string expectedId) {
        return IsValidTerrestrialEnemy(unit, expectedId) &&
               unit.IsInCombat &&
               unit.LifeState != null &&
               !unit.LifeState.IsDead &&
               !unit.LifeState.IsFinallyDead;
    }

    private static bool IsValidTerrestrialEnemy(
        BaseUnitEntity unit,
        string expectedId) {
        return unit != null &&
               unit is not StarshipEntity &&
               !unit.IsDisposed &&
               unit.IsInGame &&
               unit.IsPlayerEnemy &&
               string.Equals(
                   unit.UniqueId,
                   expectedId,
                   StringComparison.Ordinal);
    }

    private static bool IsLoadedTerrestrialCombat(Game game) {
        return game?.Player?.IsInCombat == true &&
               game.CurrentMode != GameModeType.SpaceCombat &&
               game.CurrentMode != GameModeType.StarSystem;
    }

    private static bool IsSupportedLifecycle(
        EncounterSaveLifecycle lifecycle) {
        switch (lifecycle) {
            case EncounterSaveLifecycle.PendingActivation:
            case EncounterSaveLifecycle.Active:
            case EncounterSaveLifecycle.NoCompatibleCandidate:
            case EncounterSaveLifecycle.DisabledForCombat:
            case EncounterSaveLifecycle.RuntimeFaulted:
                return true;
            default:
                return false;
        }
    }

    private static bool IsSupportedEncounterType(
        EncounterType encounterType) {
        return encounterType == EncounterType.Common ||
               encounterType == EncounterType.Boss ||
               encounterType ==
                   (EncounterType.Common | EncounterType.Boss);
    }

    private static bool IsValidMechanicId(string id) {
        return !string.IsNullOrWhiteSpace(id) &&
               id.Length <=
                   EncounterPersistenceValidation
                       .MaximumEntityIdLength;
    }

    private static void SuppressInvalidLoadedState(
        string failureReason) {
        if (IsLoadedTerrestrialCombat(Game.Instance)) {
            EncounterRuntime.SuppressLoadedCombat();
            Main.LogWarning(
                $"Encounter loaded state was corrupt, incomplete, or unsupported and was suppressed: {failureReason}");
        } else {
            EncounterRuntime.ResetForLoadedState();
            Main.LogWarning(
                $"Encounter loaded state was corrupt, incomplete, unsupported, or stale and was discarded outside combat: {failureReason}");
        }
    }

    private static void TrySuppressLoadedCombat(string reason) {
        try {
            if (IsLoadedTerrestrialCombat(Game.Instance)) {
                EncounterRuntime.SuppressLoadedCombat();
                Main.LogWarning(
                    $"Encounter loaded combat was suppressed after {reason}.");
            } else {
                EncounterRuntime.ResetForLoadedState();
                Main.LogWarning(
                    $"Encounter loaded state was discarded outside combat after {reason}.");
            }
        } catch (Exception exception) {
            Main.LogError(
                $"Encounter loaded-combat suppression failed: {exception}");
        }
    }

    private static void RemoveSaveKey(
        Dictionary<string, object> list) {
        try {
            list.Remove(SaveKey);
        } catch (Exception exception) {
            Main.LogError(
                $"Encounter stale save key could not be removed: {exception}");
        }
    }
}

[HarmonyPatch(
    typeof(SaveManager),
    nameof(SaveManager.PreSave))]
internal static class SaveManagerPreSavePatch {
    [HarmonyPostfix]
    private static void Postfix() {
        try {
            EncounterPersistence.TryWriteSaveRecord();
        } catch (Exception exception) {
            Main.LogError(
                $"Encounter PreSave postfix failure was isolated: {exception}");
        }
    }
}
