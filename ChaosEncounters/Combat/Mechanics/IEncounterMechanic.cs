using Kingmaker.EntitySystem.Entities;

namespace ChaosEncounters.Combat.Mechanics;

internal interface IEncounterMechanic {
    string Id { get; }

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

internal enum EncounterMechanicEndReason {
    CombatEnded,
    RuntimeFault,
    ManualEmergencyDisable
}
