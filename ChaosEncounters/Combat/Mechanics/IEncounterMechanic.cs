using Kingmaker.EntitySystem.Entities;
using ChaosEncounters.Combat.Persistence;

namespace ChaosEncounters.Combat.Mechanics;

internal interface IEncounterMechanic {
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }

    bool CanActivate(EncounterSession session);

    void Activate(EncounterSession session);

    void HandleRoundStart(int combatRound);
    void HandleRoundEnd(int combatRound);

    void HandleUnitTurnStart(
        BaseUnitEntity unit,
        int combatRound);

    void HandleUnitTurnEnd(
        BaseUnitEntity unit,
        int combatRound);

    void HandleEnemyDeath(
        BaseUnitEntity unit,
        int combatRound);

    void Deactivate(EncounterMechanicEndReason reason);
}

internal interface IEnemyJoinAwareMechanic {
    void HandleEnemyJoined(BaseUnitEntity unit);
}

internal interface IUnitCombatLifecycleAwareMechanic {
    void HandleUnitJoinedCombat(BaseUnitEntity unit);
    void HandleUnitLeftCombat(BaseUnitEntity unit);
}

internal interface IPersistableEncounterMechanic {
    bool TryCaptureSaveData(
        EncounterMechanicSaveData saveData,
        out string failureReason);

    // Restoration rebuilds an already selected mechanic and must not
    // re-evaluate activation eligibility, classification, or CanActivate.
    bool TryRestoreFromSave(
        EncounterRestoreContext context,
        EncounterMechanicSaveData saveData,
        out string failureReason);
}

internal enum EncounterMechanicRestoreStatus {
    Restored,
    Unsupported,
    DisabledInSettings,
    Invalid
}

internal enum EncounterMechanicEndReason {
    CombatEnded,
    RuntimeFault,
    ManualEmergencyDisable,
    AreaUnloading,
    LoadedStateReplaced
}
