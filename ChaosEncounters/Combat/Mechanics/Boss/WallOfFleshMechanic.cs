using System.Collections.Generic;
using ChaosEncounters.UI;
using Kingmaker.EntitySystem.Entities;

namespace ChaosEncounters.Combat.Mechanics.Boss;

internal sealed class WallOfFleshMechanic :
    IEncounterMechanic,
    IEnemyJoinAwareMechanic {
    private const string MechanicId = "WallOfFlesh";
    private const string HudTitle = "Wall of Flesh";
    private const string HudDescription =
        "The Boss uses its followers as a living shield and remains invulnerable until they are all dead.";
    private const string BossMarker = "Boss";

    private EncounterSession ActiveSession;
    private BaseUnitEntity Boss;
    private List<BaseUnitEntity> LivingMinions;
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
        var livingMinions = new List<BaseUnitEntity>();
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity candidate =
                session.InitialEnemies[index];
            if (ReferenceEquals(candidate, leader)) {
                leaderFound = true;
            } else if (IsValidSubordinate(
                           candidate,
                           leader) &&
                       FindUnitIndex(
                           livingMinions,
                           candidate) < 0) {
                livingMinions.Add(candidate);
            }
        }
        if (!leaderFound) {
            throw new InvalidOperationException(
                "The Wall of Flesh Boss leader is not part of the initial enemy snapshot.");
        }

        ActiveSession = session;
        Boss = leader;
        LivingMinions = livingMinions;
        ProtectionActive = LivingMinions.Count > 0;

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
            $"RemainingMinions={LivingMinions.Count} " +
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

    public void HandleEnemyJoined(
        BaseUnitEntity unit) {
        List<BaseUnitEntity> livingMinions =
            LivingMinions;
        BaseUnitEntity boss = Boss;
        if (ActiveSession == null ||
            livingMinions == null ||
            !IsLivingBoss(boss) ||
            !IsValidSubordinate(unit, boss) ||
            FindUnitIndex(livingMinions, unit) >= 0) {
            return;
        }

        livingMinions.Add(unit);
        bool wallRestored = !ProtectionActive;
        if (wallRestored) {
            ProtectionActive = true;
            DamageControl.SetIncomingDamageReduction(
                boss,
                100);
            UnitMarker.SetMarker(
                boss,
                BossMarker,
                ChaosColors.Grey);
            EncounterHud.Show(
                HudTitle,
                HudDescription);
        }

        Main.LogInfo(
            $"Wall of Flesh reinforcement registered: " +
            $"UnitName={unit.CharacterName} " +
            $"LivingMinions={livingMinions.Count} " +
            $"WallRestored={wallRestored}");
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
            LivingMinions?.Clear();
            LivingMinions = null;
            DamageControl.ClearIncomingDamageReduction(Boss);
            UnitMarker.ClearMarker(Boss);
            EncounterHud.Hide();
            if (protectionWasActive) {
                Main.LogWarning(
                    "Wall of Flesh Boss died while protection was still active; mechanic state was cleared.");
            }
            return;
        }

        List<BaseUnitEntity> livingMinions =
            LivingMinions;
        if (livingMinions == null) {
            return;
        }

        int minionIndex = FindUnitIndex(
            livingMinions,
            unit);
        if (minionIndex < 0) {
            return;
        }

        livingMinions.RemoveAt(minionIndex);
        if (livingMinions.Count > 0) {
            return;
        }

        ProtectionActive = false;
        DamageControl.ClearIncomingDamageReduction(Boss);
        EncounterHud.Hide();
        Main.LogInfo(
            $"Wall of Flesh broken: BossName={Boss.CharacterName}");
    }

    public void Deactivate(
        EncounterMechanicEndReason reason) {
        LivingMinions?.Clear();
        ActiveSession = null;
        Boss = null;
        LivingMinions = null;
        ProtectionActive = false;
    }

    private static bool IsLivingBoss(
        BaseUnitEntity boss) {
        return boss != null &&
               !boss.IsDisposed &&
               boss.LifeState != null &&
               !boss.LifeState.IsDead;
    }

    private static bool IsValidSubordinate(
        BaseUnitEntity unit,
        BaseUnitEntity boss) {
        return unit != null &&
               !ReferenceEquals(unit, boss) &&
               !unit.IsDisposed &&
               unit.LifeState != null &&
               !unit.LifeState.IsDead &&
               unit.IsInCombat &&
               unit.IsPlayerEnemy;
    }

    private static int FindUnitIndex(
        List<BaseUnitEntity> units,
        BaseUnitEntity unit) {
        for (int index = 0;
             index < units.Count;
             index++) {
            if (ReferenceEquals(units[index], unit)) {
                return index;
            }
        }

        return -1;
    }
}
