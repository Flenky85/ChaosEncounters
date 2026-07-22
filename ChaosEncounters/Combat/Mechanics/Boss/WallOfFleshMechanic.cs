using System.Collections.Generic;
using ChaosEncounters.Combat.Persistence;
using ChaosEncounters.UI;
using Kingmaker.EntitySystem.Entities;

namespace ChaosEncounters.Combat.Mechanics.Boss;

internal sealed class WallOfFleshMechanic :
    IEncounterMechanic,
    IEnemyJoinAwareMechanic,
    IPersistableEncounterMechanic {
    private const string MechanicId = "WallOfFlesh";
    private const string HudTitle = "Wall of Flesh";
    private const string HudDescription =
        "The Boss uses its followers as a living shield and remains invulnerable until they are all dead.";
    private const string BossMarker = "Boss";

    private bool Active;
    private BaseUnitEntity Boss;
    private List<BaseUnitEntity> LivingMinions;
    private bool ProtectionActive;

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
                "Wall of Flesh requires an encounter session.");
        }
        if (Active) {
            throw new InvalidOperationException(
                "Wall of Flesh is already active.");
        }
        if (!session.SupportsEncounterType(
                EncounterType.Boss)) {
            throw new InvalidOperationException(
                "Wall of Flesh requires Boss encounter eligibility.");
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

        Boss = leader;
        LivingMinions = livingMinions;
        ProtectionActive = LivingMinions.Count > 0;
        Active = true;

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
        if (!Active ||
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
        if (!Active || unit == null) {
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
        Active = false;
        Boss = null;
        LivingMinions = null;
        ProtectionActive = false;
    }

    bool IPersistableEncounterMechanic.TryCaptureSaveData(
        EncounterMechanicSaveData saveData,
        out string failureReason) {
        failureReason = null;
        if (saveData == null) {
            failureReason =
                "The Wall of Flesh save-data container is unavailable.";
            return false;
        }
        if (saveData.WallOfFlesh != null) {
            failureReason =
                "The Wall of Flesh save-data container is already populated.";
            return false;
        }
        if (!Active || Boss == null) {
            failureReason =
                "Wall of Flesh is not initialized for save capture.";
            return false;
        }

        string bossId = Boss.UniqueId;
        if (!EncounterPersistenceValidation
                .IsValidEntityId(bossId)) {
            failureReason =
                "The Wall of Flesh Boss has an invalid persistent ID.";
            return false;
        }

        if (IsLivingBoss(Boss)) {
            if (LivingMinions == null) {
                failureReason =
                    "The living Wall of Flesh Boss has no minion roster.";
                return false;
            }
            if (ProtectionActive !=
                (LivingMinions.Count > 0)) {
                failureReason =
                    "The Wall of Flesh protection state does not match its living-minion roster.";
                return false;
            }

            for (int index = 0;
                 index < LivingMinions.Count;
                 index++) {
                BaseUnitEntity minion =
                    LivingMinions[index];
                if (!IsValidSubordinate(
                        minion,
                        Boss)) {
                    failureReason =
                        $"The Wall of Flesh minion at index {index} is invalid.";
                    return false;
                }

                for (int duplicateIndex = 0;
                     duplicateIndex < index;
                     duplicateIndex++) {
                    if (ReferenceEquals(
                            LivingMinions[duplicateIndex],
                            minion)) {
                        failureReason =
                            $"The Wall of Flesh minion at index {index} is duplicated.";
                        return false;
                    }
                }
            }
        } else {
            if (ProtectionActive) {
                failureReason =
                    "The dead Wall of Flesh Boss still has active protection.";
                return false;
            }
            if (LivingMinions != null) {
                failureReason =
                    "The dead Wall of Flesh Boss still owns a minion roster.";
                return false;
            }
        }

        saveData.WallOfFlesh =
            new WallOfFleshSaveRecipe {
                BossId = bossId
            };
        return true;
    }

    bool IPersistableEncounterMechanic.TryRestoreFromSave(
        EncounterRestoreContext context,
        EncounterMechanicSaveData saveData,
        out string failureReason) {
        failureReason = null;
        if (Active ||
            Boss != null ||
            LivingMinions != null ||
            ProtectionActive) {
            failureReason =
                "Wall of Flesh is already active or retains runtime state.";
            return false;
        }
        if (context == null) {
            failureReason =
                "The Wall of Flesh restore context is unavailable.";
            return false;
        }
        if (saveData == null) {
            failureReason =
                "The Wall of Flesh save-data container is unavailable.";
            return false;
        }

        WallOfFleshSaveRecipe recipe =
            saveData.WallOfFlesh;
        if (recipe == null) {
            failureReason =
                "The Wall of Flesh save recipe is missing.";
            return false;
        }
        if (!EncounterPersistenceValidation
                .IsValidEntityId(recipe.BossId)) {
            failureReason =
                "The Wall of Flesh saved Boss ID is invalid.";
            return false;
        }
        if (!context.TryResolveEnemy(
                recipe.BossId,
                requireLiving: false,
                out BaseUnitEntity boss)) {
            failureReason =
                "The Wall of Flesh saved Boss could not be resolved as a loaded combat enemy.";
            return false;
        }

        bool bossAlive = IsLivingBoss(boss);
        List<BaseUnitEntity> livingMinions = null;
        bool protectionActive = false;
        if (bossAlive) {
            livingMinions = new List<BaseUnitEntity>(
                context.LivingEnemies.Count);
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
                if (!IsValidSubordinate(
                        candidate,
                        boss) ||
                    FindUnitIndex(
                        livingMinions,
                        candidate) >= 0) {
                    continue;
                }

                livingMinions.Add(candidate);
            }
            if (!bossFound) {
                failureReason =
                    "The living Wall of Flesh Boss is absent from the loaded living-enemy roster.";
                return false;
            }

            protectionActive = livingMinions.Count > 0;
        }

        Boss = boss;
        LivingMinions = livingMinions;
        ProtectionActive = protectionActive;
        Active = true;

        if (bossAlive) {
            if (protectionActive) {
                DamageControl.SetIncomingDamageReduction(
                    boss,
                    100);
            }
            UnitMarker.SetMarker(
                boss,
                BossMarker,
                ChaosColors.Grey);
            if (protectionActive) {
                EncounterHud.Show(
                    HudTitle,
                    HudDescription);
            } else {
                EncounterHud.Hide();
            }
        } else {
            EncounterHud.Hide();
        }

        Main.LogInfo(
            $"Wall of Flesh restored: " +
            $"BossName={boss.CharacterName} " +
            $"LivingMinions={livingMinions?.Count ?? 0} " +
            $"ProtectionActive={protectionActive} " +
            $"BossAlive={bossAlive}");
        return true;
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
