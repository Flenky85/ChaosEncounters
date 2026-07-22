using System.Collections.Generic;
using ChaosEncounters.Combat.Persistence;
using ChaosEncounters.UI;
using Kingmaker.EntitySystem.Entities;

namespace ChaosEncounters.Combat.Mechanics.Boss;

internal sealed class TyrantsAegisMechanic :
    IEncounterMechanic,
    IEnemyJoinAwareMechanic,
    IPersistableEncounterMechanic {
    private const string MechanicId = "TyrantsAegis";
    private const string HudTitle = "Tyrant's Aegis";
    private const string HudDescription =
        "All other enemies are immune to damage while the Boss remains alive. Kill the Boss to break their protection.";
    private const string BossMarker = "Boss";
    private const string InvulnerableMarker = "Invul";

    private bool Active;
    private BaseUnitEntity Boss;
    private List<BaseUnitEntity> ProtectedEnemies;
    private bool Resolved;

    public string Id => MechanicId;
    public string DisplayName => HudTitle;
    public string Description => HudDescription;

    public bool CanActivate(EncounterSession session) {
        return session != null &&
               session.SupportsEncounterType(
                   EncounterType.Boss) &&
               session.Leader != null;
    }

    public void Activate(EncounterSession session) {
        if (session == null) {
            throw new InvalidOperationException(
                "Tyrant's Aegis requires an encounter session.");
        }
        if (Active) {
            throw new InvalidOperationException(
                "Tyrant's Aegis is already active.");
        }
        if (!session.SupportsEncounterType(
                EncounterType.Boss)) {
            throw new InvalidOperationException(
                "Tyrant's Aegis requires Boss encounter eligibility.");
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

        Boss = leader;
        ProtectedEnemies = new List<BaseUnitEntity>();
        Resolved = false;
        Active = true;

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
        if (!Active ||
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
        if (!Active ||
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
        Active = false;
        Boss = null;
        ProtectedEnemies = null;
        Resolved = false;
    }

    bool IPersistableEncounterMechanic.TryCaptureSaveData(
        EncounterMechanicSaveData saveData,
        out string failureReason) {
        failureReason = null;
        if (saveData == null) {
            failureReason =
                "The Tyrant's Aegis save-data container is unavailable.";
            return false;
        }
        if (saveData.TyrantsAegis != null) {
            failureReason =
                "The Tyrant's Aegis save-data container is already populated.";
            return false;
        }
        if (!Active || Boss == null || ProtectedEnemies == null) {
            failureReason =
                "Tyrant's Aegis is not initialized for save capture.";
            return false;
        }

        string bossId = Boss.UniqueId;
        if (!EncounterPersistenceValidation
                .IsValidEntityId(bossId)) {
            failureReason =
                "The Tyrant's Aegis Boss has an invalid persistent ID.";
            return false;
        }

        if (Resolved) {
            if (IsLivingBoss(Boss)) {
                failureReason =
                    "The resolved Tyrant's Aegis Boss is still alive.";
                return false;
            }
            if (ProtectedEnemies.Count != 0) {
                failureReason =
                    "The resolved Tyrant's Aegis still owns protected enemies.";
                return false;
            }
        } else {
            if (!IsLivingBoss(Boss)) {
                failureReason =
                    "The unresolved Tyrant's Aegis Boss is not alive.";
                return false;
            }

            for (int index = 0;
                 index < ProtectedEnemies.Count;
                 index++) {
                BaseUnitEntity protectedEnemy =
                    ProtectedEnemies[index];
                if (!IsValidProtectedEnemy(
                        protectedEnemy,
                        Boss)) {
                    failureReason =
                        $"The Tyrant's Aegis protected enemy at index {index} is invalid.";
                    return false;
                }

                for (int duplicateIndex = 0;
                     duplicateIndex < index;
                     duplicateIndex++) {
                    if (ReferenceEquals(
                            ProtectedEnemies[duplicateIndex],
                            protectedEnemy)) {
                        failureReason =
                            $"The Tyrant's Aegis protected enemy at index {index} is duplicated.";
                        return false;
                    }
                }
            }
        }

        saveData.TyrantsAegis =
            new TyrantsAegisSaveRecipe {
                BossId = bossId
            };
        return true;
    }

    bool IPersistableEncounterMechanic.TryRestoreFromSave(
        EncounterRestoreContext context,
        EncounterMechanicSaveData saveData,
        out string failureReason) {
        failureReason = null;
        if (Active || Boss != null || ProtectedEnemies != null) {
            failureReason =
                "Tyrant's Aegis is already active.";
            return false;
        }
        if (context == null) {
            failureReason =
                "The Tyrant's Aegis restore context is unavailable.";
            return false;
        }
        if (saveData == null) {
            failureReason =
                "The Tyrant's Aegis save-data container is unavailable.";
            return false;
        }

        TyrantsAegisSaveRecipe recipe =
            saveData.TyrantsAegis;
        if (recipe == null) {
            failureReason =
                "The Tyrant's Aegis save recipe is missing.";
            return false;
        }
        if (!EncounterPersistenceValidation
                .IsValidEntityId(recipe.BossId)) {
            failureReason =
                "The Tyrant's Aegis saved Boss ID is invalid.";
            return false;
        }
        if (!context.TryResolveEnemy(
                recipe.BossId,
                requireLiving: false,
                out BaseUnitEntity boss)) {
            failureReason =
                "The Tyrant's Aegis saved Boss could not be resolved as a loaded combat enemy.";
            return false;
        }

        bool resolved = !IsLivingBoss(boss);
        var protectedEnemies = resolved
            ? new List<BaseUnitEntity>()
            : new List<BaseUnitEntity>(
                context.LivingEnemies.Count);
        if (!resolved) {
            bool bossFound = false;
            for (int index = 0;
                 index < context.LivingEnemies.Count;
                 index++) {
                BaseUnitEntity candidate =
                    context.LivingEnemies[index];
                if (ReferenceEquals(candidate, boss)) {
                    bossFound = true;
                    continue;
                }
                if (!IsValidProtectedEnemy(
                        candidate,
                        boss) ||
                    FindUnitIndex(
                        protectedEnemies,
                        candidate) >= 0) {
                    continue;
                }

                protectedEnemies.Add(candidate);
            }
            if (!bossFound) {
                failureReason =
                    "The living Tyrant's Aegis Boss is absent from the loaded living-enemy roster.";
                return false;
            }
        }

        Boss = boss;
        ProtectedEnemies = protectedEnemies;
        Resolved = resolved;
        Active = true;

        if (!resolved) {
            for (int index = 0;
                 index < protectedEnemies.Count;
                 index++) {
                BaseUnitEntity protectedEnemy =
                    protectedEnemies[index];
                DamageControl.SetIncomingDamageReduction(
                    protectedEnemy,
                    100);
                UnitMarker.SetMarker(
                    protectedEnemy,
                    InvulnerableMarker,
                    ChaosColors.Grey);
            }

            UnitMarker.SetMarker(
                boss,
                BossMarker,
                ChaosColors.Red);
            EncounterHud.Show(
                HudTitle,
                HudDescription);
        } else {
            EncounterHud.Hide();
        }

        Main.LogInfo(
            $"Tyrant's Aegis restored: " +
            $"BossName={boss.CharacterName} " +
            $"ProtectedEnemyCount={protectedEnemies.Count} " +
            $"Resolved={resolved}");
        return true;
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
