using Kingmaker.EntitySystem.Entities;

namespace ChaosEncounters.Combat.Mechanics;

internal sealed class LifecycleProbeMechanic :
    IEncounterMechanic {
    private EncounterSession ActiveSession;
    private int RoundStartCount;
    private int RoundEndCount;
    private int UnitTurnStartCount;
    private int UnitTurnEndCount;
    private int EnemyDeathCount;

    internal LifecycleProbeMechanic(string id) {
        Id = id;
    }

    public string Id { get; }

    public void Activate(EncounterSession session) {
        if (session == null) {
            throw new ArgumentNullException(nameof(session));
        }
        if (ActiveSession != null) {
            throw new InvalidOperationException(
                $"Lifecycle probe {Id} is already active.");
        }

        ActiveSession = session;
        ResetCounters();
    }

    public void HandleRoundStart(int combatRound) {
        RoundStartCount++;
    }

    public void HandleRoundEnd(int combatRound) {
        RoundEndCount++;
    }

    public void HandleUnitTurnStart(
        BaseUnitEntity unit,
        int combatRound) {
        UnitTurnStartCount++;
    }

    public void HandleUnitTurnEnd(
        BaseUnitEntity unit,
        int combatRound) {
        UnitTurnEndCount++;
    }

    public void HandleEnemyDeath(
        BaseUnitEntity unit,
        int combatRound) {
        EnemyDeathCount++;
    }

    public void Deactivate(
        EncounterMechanicEndReason reason) {
        EncounterSession session = ActiveSession;
        try {
            if (session != null) {
                Main.LogInfo(
                    $"Encounter lifecycle probe summary: " +
                    $"Id={Id} " +
                    $"EncounterType={session.Type} " +
                    $"EndReason={reason} " +
                    $"RoundStarts={RoundStartCount} " +
                    $"RoundEnds={RoundEndCount} " +
                    $"UnitTurnStarts={UnitTurnStartCount} " +
                    $"UnitTurnEnds={UnitTurnEndCount} " +
                    $"EnemyDeaths={EnemyDeathCount}");
            }
        } finally {
            ActiveSession = null;
            ResetCounters();
        }
    }

    private void ResetCounters() {
        RoundStartCount = 0;
        RoundEndCount = 0;
        UnitTurnStartCount = 0;
        UnitTurnEndCount = 0;
        EnemyDeathCount = 0;
    }
}
