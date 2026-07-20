using ChaosEncounters.UI;
using Kingmaker.EntitySystem.Entities;

namespace ChaosEncounters.Combat.Mechanics.Boss;

internal sealed class TyrantsAegisMechanic :
    IEncounterMechanic {
    private const string MechanicId = "TyrantsAegis";
    private const string HudTitle = "Tyrant's Aegis";
    private const string HudDescription =
        "All other enemies are immune to damage while the Boss remains alive. Kill the Boss to break their protection.";
    private const string BossMarker = "Boss";
    private const string InvulnerableMarker = "Invul";

    private EncounterSession ActiveSession;
    private BaseUnitEntity Boss;
    private bool Resolved;

    public string Id => MechanicId;

    public bool CanActivate(EncounterSession session) {
        return session != null &&
               session.Type == EncounterType.Boss &&
               session.Leader != null;
    }

    public void Activate(EncounterSession session) {
        if (session == null) {
            throw new InvalidOperationException(
                "Tyrant's Aegis requires an encounter session.");
        }
        if (ActiveSession != null) {
            throw new InvalidOperationException(
                "Tyrant's Aegis is already active.");
        }
        if (session.Type != EncounterType.Boss) {
            throw new InvalidOperationException(
                "Tyrant's Aegis requires a Boss encounter.");
        }

        BaseUnitEntity leader = session.Leader;
        if (leader == null) {
            throw new InvalidOperationException(
                "Tyrant's Aegis requires an exact Boss leader.");
        }

        bool leaderFound = false;
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            if (ReferenceEquals(
                    session.InitialEnemies[index],
                    leader)) {
                leaderFound = true;
                break;
            }
        }
        if (!leaderFound) {
            throw new InvalidOperationException(
                "The Tyrant's Aegis Boss leader is not part of the initial enemy snapshot.");
        }

        ActiveSession = session;
        Boss = leader;
        Resolved = false;

        int protectedEnemyCount = 0;
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity enemy = session.InitialEnemies[index];
            if (ReferenceEquals(enemy, leader)) {
                UnitMarker.SetMarker(
                    enemy,
                    BossMarker,
                    ChaosColors.Red);
                continue;
            }

            DamageControl.SetIncomingDamageReduction(
                enemy,
                100);
            UnitMarker.SetMarker(
                enemy,
                InvulnerableMarker,
                ChaosColors.Grey);
            protectedEnemyCount++;
        }

        EncounterHud.Show(
            HudTitle,
            HudDescription);
        Main.LogInfo(
            $"Tyrant's Aegis activated: " +
            $"BossName={leader.CharacterName} " +
            $"BossBlueprint={leader.Blueprint?.name ?? "None"} " +
            $"ProtectedEnemyCount={protectedEnemyCount}");
    }

    public void HandleRoundStart(int combatRound) {
    }

    public void HandleRoundEnd(int combatRound) {
    }

    public void HandleUnitTurnStart(
        BaseUnitEntity unit,
        int combatRound) {
    }

    public void HandleUnitTurnEnd(
        BaseUnitEntity unit,
        int combatRound) {
    }

    public void HandleEnemyDeath(
        BaseUnitEntity unit,
        int combatRound) {
        if (ActiveSession == null ||
            Resolved ||
            unit == null) {
            return;
        }

        if (!ReferenceEquals(unit, Boss)) {
            DamageControl.ClearPolicy(unit);
            UnitMarker.ClearMarker(unit);
            return;
        }

        Resolved = true;
        int releasedEnemyCount = 0;
        for (int index = 0;
             index < ActiveSession.InitialEnemies.Count;
             index++) {
            BaseUnitEntity enemy =
                ActiveSession.InitialEnemies[index];
            if (ReferenceEquals(enemy, Boss)) {
                UnitMarker.ClearMarker(enemy);
                continue;
            }

            DamageControl.ClearPolicy(enemy);
            UnitMarker.ClearMarker(enemy);
            releasedEnemyCount++;
        }

        EncounterHud.Hide();
        Main.LogInfo(
            $"Tyrant's Aegis broken: " +
            $"BossName={Boss.CharacterName} " +
            $"ReleasedEnemyCount={releasedEnemyCount}");
    }

    public void Deactivate(
        EncounterMechanicEndReason reason) {
        ActiveSession = null;
        Boss = null;
        Resolved = false;
    }
}
