using ChaosEncounters.UI;
using Kingmaker.EntitySystem.Entities;

namespace ChaosEncounters.Combat.Mechanics.Boss;

internal sealed class WallOfFleshMechanic :
    IEncounterMechanic {
    private const string MechanicId = "WallOfFlesh";
    private const string HudTitle = "Wall of Flesh";
    private const string HudDescription =
        "The Boss uses its followers as a living shield and remains invulnerable until they are all dead.";
    private const string BossMarker = "Boss";

    private EncounterSession ActiveSession;
    private BaseUnitEntity Boss;
    private int RemainingMinions;
    private bool ProtectionActive;

    public string Id => MechanicId;
    public string DisplayName => HudTitle;
    public string Description => HudDescription;

    public bool CanActivate(EncounterSession session) {
        return session != null &&
               session.Type == EncounterType.Boss &&
               session.Leader != null;
    }

    public void Activate(EncounterSession session) {
        if (session == null) {
            throw new InvalidOperationException(
                "Wall of Flesh requires an encounter session.");
        }
        if (ActiveSession != null) {
            throw new InvalidOperationException(
                "Wall of Flesh is already active.");
        }
        if (session.Type != EncounterType.Boss) {
            throw new InvalidOperationException(
                "Wall of Flesh requires a Boss encounter.");
        }

        BaseUnitEntity leader = session.Leader;
        if (leader == null) {
            throw new InvalidOperationException(
                "Wall of Flesh requires an exact Boss leader.");
        }

        bool leaderFound = false;
        int remainingMinions = 0;
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity candidate =
                session.InitialEnemies[index];
            if (ReferenceEquals(candidate, leader)) {
                leaderFound = true;
            } else if (candidate != null &&
                       !candidate.LifeState.IsDead) {
                remainingMinions++;
            }
        }
        if (!leaderFound) {
            throw new InvalidOperationException(
                "The Wall of Flesh Boss leader is not part of the initial enemy snapshot.");
        }

        ActiveSession = session;
        Boss = leader;
        RemainingMinions = remainingMinions;
        ProtectionActive = RemainingMinions > 0;

        if (ProtectionActive) {
            DamageControl.SetIncomingDamageReduction(
                Boss,
                100);
        }
        UnitMarker.SetMarker(
            Boss,
            BossMarker,
            ChaosColors.Grey);
        if (ProtectionActive) {
            EncounterHud.Show(
                HudTitle,
                HudDescription);
        }

        Main.LogInfo(
            $"Wall of Flesh activated: " +
            $"BossName={Boss.CharacterName} " +
            $"BossBlueprint={Boss.Blueprint?.name ?? "None"} " +
            $"RemainingMinions={RemainingMinions} " +
            $"ProtectionActive={ProtectionActive}");
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
        if (ActiveSession == null || unit == null) {
            return;
        }

        if (ReferenceEquals(unit, Boss)) {
            bool protectionWasActive = ProtectionActive;
            ProtectionActive = false;
            RemainingMinions = 0;
            DamageControl.ClearPolicy(Boss);
            UnitMarker.ClearMarker(Boss);
            EncounterHud.Hide();
            if (protectionWasActive) {
                Main.LogWarning(
                    "Wall of Flesh Boss died while protection was still active; mechanic state was cleared.");
            }
            return;
        }

        if (!ProtectionActive) {
            return;
        }

        bool isInitialSubordinate = false;
        for (int index = 0;
             index < ActiveSession.InitialEnemies.Count;
             index++) {
            BaseUnitEntity candidate =
                ActiveSession.InitialEnemies[index];
            if (ReferenceEquals(candidate, unit) &&
                !ReferenceEquals(candidate, Boss)) {
                isInitialSubordinate = true;
                break;
            }
        }
        if (!isInitialSubordinate) {
            return;
        }

        if (RemainingMinions > 0) {
            RemainingMinions--;
        }
        if (RemainingMinions > 0) {
            return;
        }

        ProtectionActive = false;
        RemainingMinions = 0;
        DamageControl.ClearPolicy(Boss);
        EncounterHud.Hide();
        Main.LogInfo(
            $"Wall of Flesh broken: BossName={Boss.CharacterName}");
    }

    public void Deactivate(
        EncounterMechanicEndReason reason) {
        ActiveSession = null;
        Boss = null;
        RemainingMinions = 0;
        ProtectionActive = false;
    }
}
