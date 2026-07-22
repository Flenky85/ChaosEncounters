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
    private const int MaximumEntityCount = 4096;
    private const int MaximumEntityIdLength = 1024;

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

        if (!TryResolveRecord(
                record,
                out List<BaseUnitEntity> initialEnemies,
                out BaseUnitEntity leader,
                out List<BaseUnitEntity> pendingEnemyJoins,
                out failureReason)) {
            SuppressInvalidLoadedState(failureReason);
            return;
        }

        EncounterRuntime.RestoreSaveRecord(
            record,
            initialEnemies,
            leader,
            pendingEnemyJoins);

        switch (record.Lifecycle) {
            case EncounterSaveLifecycle.PendingActivation:
                Main.LogInfo(
                    "Pending encounter activation restored; normal first-round selection remains pending.");
                break;
            case EncounterSaveLifecycle.Active:
                if (EncounterMechanicController
                    .IsExecutionListMechanicId(
                        record.MechanicId)) {
                    Main.LogInfo(
                        "Active Execution List save record found; attempting exact ordered restoration.");
                    if (!EncounterRuntime.TryRestoreExecutionList(
                            record.ExecutionListRecipe,
                            out bool disabledInSettings,
                            out string restoreFailureReason)) {
                        if (disabledInSettings) {
                            Main.LogWarning(
                                $"Saved Execution List was suppressed because it is disabled in the current mod settings: {restoreFailureReason}");
                        } else {
                            Main.LogWarning(
                                $"Saved Execution List was suppressed because its recipe is missing or invalid: {restoreFailureReason}");
                        }
                    }
                } else {
                    Main.LogWarning(
                        $"Saved active mechanic was suppressed: MechanicId={record.MechanicId}. " +
                        "Mechanic-specific restoration is not implemented in this roadmap point.");
                }
                break;
            default:
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
        if (record.Lifecycle == EncounterSaveLifecycle.Active) {
            if (!IsValidId(record.MechanicId)) {
                failureReason =
                    "an active record has no valid mechanic ID";
                return false;
            }
        } else if (!string.IsNullOrEmpty(record.MechanicId)) {
            failureReason =
                "a non-active record unexpectedly contains a mechanic ID";
            return false;
        }
        if (record.ExecutionListRecipe != null &&
            (record.Lifecycle != EncounterSaveLifecycle.Active ||
             !EncounterMechanicController
                 .IsExecutionListMechanicId(
                     record.MechanicId))) {
            failureReason =
                "an Execution List recipe is attached to a different encounter state";
            return false;
        }
        if (record.LeaderId != null &&
            !IsValidId(record.LeaderId)) {
            failureReason = "the leader ID is invalid";
            return false;
        }
        if (record.InitialEnemyIds == null ||
            record.InitialEnemyIds.Count == 0 ||
            record.InitialEnemyIds.Count > MaximumEntityCount) {
            failureReason = "the initial-enemy ID list has an invalid size";
            return false;
        }
        if (record.PendingEnemyJoinIds == null ||
            record.PendingEnemyJoinIds.Count > MaximumEntityCount) {
            failureReason = "the pending-enemy ID list has an invalid size";
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0;
             index < record.InitialEnemyIds.Count;
             index++) {
            string id = record.InitialEnemyIds[index];
            if (!IsValidId(id)) {
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
        if (record.LeaderId != null &&
            !ids.Contains(record.LeaderId)) {
            failureReason =
                "the leader ID is not part of the initial enemy snapshot";
            return false;
        }
        for (int index = 0;
             index < record.PendingEnemyJoinIds.Count;
             index++) {
            string id = record.PendingEnemyJoinIds[index];
            if (!IsValidId(id)) {
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

    private static bool TryResolveRecord(
        EncounterSaveRecord record,
        out List<BaseUnitEntity> initialEnemies,
        out BaseUnitEntity leader,
        out List<BaseUnitEntity> pendingEnemyJoins,
        out string failureReason) {
        initialEnemies = new List<BaseUnitEntity>(
            record.InitialEnemyIds.Count);
        leader = null;
        pendingEnemyJoins = new List<BaseUnitEntity>(
            record.PendingEnemyJoinIds.Count);

        EntityService entityService = EntityService.Instance;
        if (entityService == null) {
            failureReason = "EntityService is unavailable";
            return false;
        }

        for (int index = 0;
             index < record.InitialEnemyIds.Count;
             index++) {
            string id = record.InitialEnemyIds[index];
            BaseUnitEntity unit =
                entityService.GetEntity<BaseUnitEntity>(id);
            if (!IsValidInitialEnemy(
                    unit,
                    id,
                    record.Lifecycle)) {
                failureReason =
                    $"initial enemy at index {index} could not be resolved as a valid loaded combat entity: Id={id}";
                return false;
            }
            initialEnemies.Add(unit);
            if (string.Equals(
                    record.LeaderId,
                    id,
                    StringComparison.Ordinal)) {
                leader = unit;
            }
        }
        if (record.LeaderId != null && leader == null) {
            failureReason =
                $"leader could not be resolved from the initial snapshot: Id={record.LeaderId}";
            return false;
        }

        for (int index = 0;
             index < record.PendingEnemyJoinIds.Count;
             index++) {
            string id = record.PendingEnemyJoinIds[index];
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

    private static bool IsValidInitialEnemy(
        BaseUnitEntity unit,
        string expectedId,
        EncounterSaveLifecycle lifecycle) {
        if (!IsValidTerrestrialEnemy(unit, expectedId)) {
            return false;
        }

        if (lifecycle != EncounterSaveLifecycle.PendingActivation) {
            return true;
        }

        return unit.IsInCombat &&
               unit.LifeState != null &&
               !unit.LifeState.IsDead;
    }

    private static bool IsValidPendingEnemy(
        BaseUnitEntity unit,
        string expectedId) {
        return IsValidTerrestrialEnemy(unit, expectedId) &&
               unit.IsInCombat &&
               unit.LifeState != null &&
               !unit.LifeState.IsDead;
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

    private static bool IsValidId(string id) {
        return !string.IsNullOrWhiteSpace(id) &&
               id.Length <= MaximumEntityIdLength;
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
