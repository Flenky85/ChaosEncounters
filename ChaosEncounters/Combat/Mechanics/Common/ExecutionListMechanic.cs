using System.Collections.Generic;
using ChaosEncounters.Combat.Persistence;
using ChaosEncounters.UI;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;

namespace ChaosEncounters.Combat.Mechanics.Common;

internal sealed class ExecutionListMechanic :
    IEncounterMechanic,
    IEnemyJoinAwareMechanic,
    IPersistableEncounterMechanic {
    private const string MechanicId = "ExecutionList";
    private const string HudTitle = "The Execution List";
    private const string HudDescription =
        "Every enemy is assigned a position on the Execution List. Position 1 has 0% damage reduction, position 2 has 20%, position 3 has 40%, position 4 has 60%, position 5 has 80%, and positions 6 or higher are immune with 100% damage reduction. When an enemy dies, every enemy behind it moves up one position and its damage reduction is updated accordingly, bringing each survivor one step closer to execution.";

    private static System.Random OrderRandom;

    private List<BaseUnitEntity> OrderedEnemies;

    public string Id => MechanicId;
    public string DisplayName => HudTitle;
    public string Description => HudDescription;

    public bool CanActivate(EncounterSession session) {
        if (session == null ||
            !session.SupportsEncounterType(
                EncounterType.Common)) {
            return false;
        }

        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity candidate =
                session.InitialEnemies[index];
            if (candidate != null &&
                !candidate.IsDisposed &&
                !candidate.LifeState.IsDead) {
                return true;
            }
        }

        return false;
    }

    public void Activate(EncounterSession session) {
        if (session == null) {
            throw new InvalidOperationException(
                "The Execution List requires an encounter session.");
        }
        if (OrderedEnemies != null) {
            throw new InvalidOperationException(
                "The Execution List is already active.");
        }
        if (!session.SupportsEncounterType(
                EncounterType.Common)) {
            throw new InvalidOperationException(
                "The Execution List requires Common encounter eligibility.");
        }

        int livingEnemyCount = 0;
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity candidate =
                session.InitialEnemies[index];
            if (candidate != null &&
                !candidate.IsDisposed &&
                !candidate.LifeState.IsDead) {
                livingEnemyCount++;
            }
        }
        if (livingEnemyCount == 0) {
            throw new InvalidOperationException(
                "The Execution List requires at least one living initial enemy.");
        }

        var orderedEnemies =
            new List<BaseUnitEntity>(livingEnemyCount);
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity candidate =
                session.InitialEnemies[index];
            if (candidate == null ||
                candidate.IsDisposed ||
                candidate.LifeState.IsDead) {
                continue;
            }

            orderedEnemies.Add(candidate);
        }

        if (livingEnemyCount > 1) {
            System.Random random = OrderRandom;
            if (random == null) {
                random = new System.Random();
                OrderRandom = random;
            }

            for (int index = livingEnemyCount - 1;
                 index > 0;
                 index--) {
                int swapIndex = random.Next(index + 1);
                BaseUnitEntity temporary =
                    orderedEnemies[index];
                orderedEnemies[index] =
                    orderedEnemies[swapIndex];
                orderedEnemies[swapIndex] = temporary;
            }
        }

        OrderedEnemies = orderedEnemies;

        for (int index = 0;
             index < OrderedEnemies.Count;
             index++) {
            ApplyPosition(
                OrderedEnemies[index],
                index + 1);
        }

        EncounterHud.Show(
            HudTitle,
            HudDescription);
        Main.LogInfo(
            $"The Execution List activated: EnemyCount={OrderedEnemies.Count}");
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
        if (OrderedEnemies == null ||
            unit == null ||
            unit.IsDisposed ||
            unit.LifeState == null ||
            unit.LifeState.IsDead ||
            !unit.IsInCombat ||
            !unit.IsPlayerEnemy) {
            return;
        }

        for (int index = 0;
             index < OrderedEnemies.Count;
             index++) {
            if (ReferenceEquals(
                    OrderedEnemies[index],
                    unit)) {
                return;
            }
        }

        bool showHud = OrderedEnemies.Count == 0;
        OrderedEnemies.Add(unit);
        int position = OrderedEnemies.Count;
        ApplyPosition(unit, position);
        if (showHud) {
            EncounterHud.Show(
                HudTitle,
                HudDescription);
        }

        Main.LogInfo(
            $"The Execution List appended a reinforcement: " +
            $"Position={position} " +
            $"ActiveEnemyCount={OrderedEnemies.Count} " +
            $"UnitName={unit.CharacterName}");
    }

    public void HandleEnemyDeath(
        BaseUnitEntity unit,
        int combatRound) {
        if (OrderedEnemies == null ||
            unit == null ||
            OrderedEnemies.Count == 0) {
            return;
        }

        int deadIndex = -1;
        for (int index = 0;
             index < OrderedEnemies.Count;
             index++) {
            if (ReferenceEquals(
                    OrderedEnemies[index],
                    unit)) {
                deadIndex = index;
                break;
            }
        }
        if (deadIndex < 0) {
            return;
        }

        int fallenPosition = deadIndex + 1;
        DamageControl.ClearIncomingDamageReduction(unit);
        UnitMarker.ClearMarker(unit);

        OrderedEnemies.RemoveAt(deadIndex);
        for (int index = deadIndex;
             index < OrderedEnemies.Count;
             index++) {
            ApplyPosition(
                OrderedEnemies[index],
                index + 1);
        }

        Main.LogInfo(
            $"The Execution List advanced: FallenPosition={fallenPosition} " +
            $"RemainingEnemies={OrderedEnemies.Count}");
        if (OrderedEnemies.Count == 0) {
            EncounterHud.Hide();
        }
    }

    public void Deactivate(
        EncounterMechanicEndReason reason) {
        OrderedEnemies = null;
    }

    bool IPersistableEncounterMechanic.TryCaptureSaveData(
        EncounterMechanicSaveData saveData,
        out string failureReason) {
        failureReason = null;
        if (saveData == null) {
            failureReason =
                "The Execution List save-data container is unavailable.";
            return false;
        }
        if (saveData.ExecutionList != null) {
            failureReason =
                "The Execution List save-data container is already populated.";
            return false;
        }

        List<BaseUnitEntity> orderedEnemies = OrderedEnemies;
        if (orderedEnemies == null) {
            failureReason =
                "The Execution List ordered enemy list is unavailable.";
            return false;
        }
        if (orderedEnemies.Count >
            EncounterPersistenceValidation.MaximumEntityCount) {
            failureReason =
                $"The Execution List ordered enemy count exceeds {EncounterPersistenceValidation.MaximumEntityCount}.";
            return false;
        }

        var orderedEnemyIds = new List<string>(
            orderedEnemies.Count);
        var uniqueIds = new HashSet<string>(
            StringComparer.Ordinal);
        for (int index = 0;
             index < orderedEnemies.Count;
             index++) {
            string id = orderedEnemies[index]?.UniqueId;
            if (!EncounterPersistenceValidation
                    .IsValidEntityId(id)) {
                failureReason =
                    $"The Execution List enemy at index {index} has an invalid persistent ID.";
                return false;
            }
            if (!uniqueIds.Add(id)) {
                failureReason =
                    $"The Execution List enemy at index {index} has a duplicate persistent ID.";
                return false;
            }
            orderedEnemyIds.Add(id);
        }

        saveData.ExecutionList = new ExecutionListSaveRecipe {
            OrderedEnemyIds = orderedEnemyIds
        };
        Main.LogInfo(
            $"The Execution List save recipe captured: " +
            $"OrderedEnemyCount={orderedEnemyIds.Count}");
        return true;
    }

    bool IPersistableEncounterMechanic.TryRestoreFromSave(
        EncounterRestoreContext context,
        EncounterMechanicSaveData saveData,
        out string failureReason) {
        failureReason = null;
        if (OrderedEnemies != null) {
            failureReason =
                "The Execution List is already active.";
            return false;
        }
        if (context == null) {
            failureReason =
                "The Execution List restore context is unavailable.";
            return false;
        }
        ExecutionListSaveRecipe recipe =
            saveData?.ExecutionList;
        if (recipe == null) {
            failureReason =
                "The Execution List save recipe is missing.";
            return false;
        }
        List<string> savedIds = recipe.OrderedEnemyIds;
        if (savedIds == null) {
            failureReason =
                "The Execution List ordered enemy ID list is missing.";
            return false;
        }
        if (savedIds.Count >
            EncounterPersistenceValidation.MaximumEntityCount) {
            failureReason =
                $"The Execution List ordered enemy ID count exceeds {EncounterPersistenceValidation.MaximumEntityCount}.";
            return false;
        }

        var uniqueSavedIds = new HashSet<string>(
            StringComparer.Ordinal);
        for (int index = 0;
             index < savedIds.Count;
             index++) {
            string id = savedIds[index];
            if (!EncounterPersistenceValidation
                    .IsValidEntityId(id)) {
                failureReason =
                    $"The Execution List saved ID at index {index} is invalid.";
                return false;
            }
            if (!uniqueSavedIds.Add(id)) {
                failureReason =
                    $"The Execution List saved ID at index {index} is duplicated.";
                return false;
            }
        }

        var restoredEnemies = new List<BaseUnitEntity>(
            context.LivingEnemies.Count);
        var restoredIds = new HashSet<string>(
            StringComparer.Ordinal);
        int resolvedSavedCount = 0;
        for (int index = 0;
             index < savedIds.Count;
             index++) {
            string savedId = savedIds[index];
            if (!context.TryResolveEnemy(
                    savedId,
                    requireLiving: true,
                    out BaseUnitEntity unit)) {
                continue;
            }

            restoredEnemies.Add(unit);
            restoredIds.Add(savedId);
            resolvedSavedCount++;
        }

        int appendedCurrentEnemyCount = 0;
        for (int index = 0;
             index < context.LivingEnemies.Count;
             index++) {
            BaseUnitEntity candidate =
                context.LivingEnemies[index];
            if (!restoredIds.Add(candidate.UniqueId)) {
                continue;
            }

            restoredEnemies.Add(candidate);
            appendedCurrentEnemyCount++;
        }

        if (resolvedSavedCount == 0 &&
            context.LivingEnemies.Count > 0) {
            failureReason =
                "No saved Execution List enemy could be reconstructed while the loaded combat still contains living enemies.";
            return false;
        }

        OrderedEnemies = restoredEnemies;
        for (int index = 0;
             index < restoredEnemies.Count;
             index++) {
            ApplyPosition(
                restoredEnemies[index],
                index + 1);
        }

        if (restoredEnemies.Count > 0) {
            EncounterHud.Show(
                HudTitle,
                HudDescription);
        } else {
            EncounterHud.Hide();
        }

        int skippedSavedCount =
            savedIds.Count - resolvedSavedCount;
        Main.LogInfo(
            $"The Execution List restored: " +
            $"SavedIdCount={savedIds.Count} " +
            $"ResolvedSavedCount={resolvedSavedCount} " +
            $"SkippedSavedCount={skippedSavedCount} " +
            $"AppendedCurrentEnemyCount={appendedCurrentEnemyCount} " +
            $"RestoredEnemyCount={restoredEnemies.Count}");
        return true;
    }

    private static void ApplyPosition(
        BaseUnitEntity unit,
        int position) {
        int incomingReduction =
            GetIncomingReduction(position);
        Color32 markerColor = GetMarkerColor(position);

        DamageControl.SetIncomingDamageReduction(
            unit,
            incomingReduction);
        UnitMarker.SetMarker(
            unit,
            position.ToString(),
            markerColor);
    }

    private static int GetIncomingReduction(int position) {
        if (position >= 6) {
            return 100;
        }

        return (position - 1) * 20;
    }

    private static Color32 GetMarkerColor(int position) {
        switch (position) {
            case 1:
                return ChaosColors.Red;
            case 2:
                return ChaosColors.Orange;
            case 3:
                return ChaosColors.Yellow;
            case 4:
                return ChaosColors.Green;
            case 5:
                return ChaosColors.Blue;
            default:
                return ChaosColors.Grey;
        }
    }
}
