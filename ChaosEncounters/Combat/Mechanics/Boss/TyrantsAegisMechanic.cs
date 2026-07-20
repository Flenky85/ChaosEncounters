using System.Collections.Generic;
using ChaosEncounters.UI;
using Kingmaker.EntitySystem.Entities;

namespace ChaosEncounters.Combat.Mechanics.Boss;

internal sealed class TyrantsAegisMechanic :
    IEncounterMechanic,
    IEnemyJoinAwareMechanic {
    private const string MechanicId = "TyrantsAegis";
    private const string HudTitle = "Tyrant's Aegis";
    private const string HudDescription =
        "All other enemies are immune to damage while the Boss remains alive. Kill the Boss to break their protection.";
    private const string BossMarker = "Boss";
    private const string InvulnerableMarker = "Invul";

    private EncounterSession ActiveSession;
    private BaseUnitEntity Boss;
    private List<BaseUnitEntity> ProtectedEnemies;
    private bool Resolved;

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
        ProtectedEnemies = new List<BaseUnitEntity>();
        Resolved = false;

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

            if (!IsValidProtectedEnemy(enemy, leader) ||
                FindProtectedEnemyIndex(enemy) >= 0) {
                continue;
            }

            ProtectedEnemies.Add(enemy);
            DamageControl.SetIncomingDamageReduction(
                enemy,
                100);
            UnitMarker.SetMarker(
                enemy,
                InvulnerableMarker,
                ChaosColors.Grey);
        }

        EncounterHud.Show(
            HudTitle,
            HudDescription);
        Main.LogInfo(
            $"Tyrant's Aegis activated: " +
            $"BossName={leader.CharacterName} " +
            $"BossBlueprint={leader.Blueprint?.name ?? "None"} " +
            $"ProtectedEnemyCount={ProtectedEnemies.Count}");
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
        if (ActiveSession == null ||
            Resolved ||
            ProtectedEnemies == null ||
            !IsLivingBoss(Boss) ||
            !IsValidProtectedEnemy(unit, Boss) ||
            FindProtectedEnemyIndex(unit) >= 0) {
            return;
        }

        ProtectedEnemies.Add(unit);
        DamageControl.SetIncomingDamageReduction(
            unit,
            100);
        UnitMarker.SetMarker(
            unit,
            InvulnerableMarker,
            ChaosColors.Grey);
        Main.LogInfo(
            $"Tyrant's Aegis reinforcement protected: " +
            $"UnitName={unit.CharacterName} " +
            $"ProtectedEnemyCount={ProtectedEnemies.Count}");
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
            int protectedEnemyIndex =
                FindProtectedEnemyIndex(unit);
            if (protectedEnemyIndex < 0) {
                return;
            }

            ProtectedEnemies.RemoveAt(
                protectedEnemyIndex);
            DamageControl.ClearPolicy(unit);
            UnitMarker.ClearMarker(unit);
            return;
        }

        Resolved = true;
        int releasedEnemyCount =
            ProtectedEnemies.Count;
        for (int index = 0;
             index < ProtectedEnemies.Count;
             index++) {
            BaseUnitEntity enemy =
                ProtectedEnemies[index];

            DamageControl.ClearPolicy(enemy);
            UnitMarker.ClearMarker(enemy);
        }
        ProtectedEnemies.Clear();

        UnitMarker.ClearMarker(Boss);
        EncounterHud.Hide();
        Main.LogInfo(
            $"Tyrant's Aegis broken: " +
            $"BossName={Boss.CharacterName} " +
            $"ReleasedEnemyCount={releasedEnemyCount}");
    }

    public void Deactivate(
        EncounterMechanicEndReason reason) {
        ProtectedEnemies?.Clear();
        ActiveSession = null;
        Boss = null;
        ProtectedEnemies = null;
        Resolved = false;
    }

    private static bool IsLivingBoss(
        BaseUnitEntity boss) {
        return boss != null &&
               !boss.IsDisposed &&
               boss.LifeState != null &&
               !boss.LifeState.IsDead;
    }

    private static bool IsValidProtectedEnemy(
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

    private int FindProtectedEnemyIndex(
        BaseUnitEntity unit) {
        List<BaseUnitEntity> protectedEnemies =
            ProtectedEnemies;
        if (protectedEnemies == null) {
            return -1;
        }

        for (int index = 0;
             index < protectedEnemies.Count;
             index++) {
            if (ReferenceEquals(
                    protectedEnemies[index],
                    unit)) {
                return index;
            }
        }

        return -1;
    }
}
