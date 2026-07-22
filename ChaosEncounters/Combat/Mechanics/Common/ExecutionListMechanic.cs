using System.Collections.Generic;
using ChaosEncounters.Combat.Persistence;
using ChaosEncounters.UI;
using Kingmaker;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;

namespace ChaosEncounters.Combat.Mechanics.Common;

internal sealed class ExecutionListMechanic :
    IEncounterMechanic,
    IEnemyJoinAwareMechanic {
    internal const string MechanicId = "ExecutionList";
    private const int MaximumPersistedEnemyCount = 4096;
    private const int MaximumEntityIdLength = 1024;
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

    internal bool TryCreateSaveRecipe(
        out ExecutionListSaveRecipe recipe,
        out string failureReason) {
        recipe = null;
        failureReason = null;
        List<BaseUnitEntity> orderedEnemies = OrderedEnemies;
        if (orderedEnemies == null) {
            failureReason =
                "The Execution List ordered enemy list is unavailable.";
            return false;
        }
        if (orderedEnemies.Count > MaximumPersistedEnemyCount) {
            failureReason =
                $"The Execution List ordered enemy count exceeds {MaximumPersistedEnemyCount}.";
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
            if (!IsValidPersistedId(id)) {
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

        recipe = new ExecutionListSaveRecipe {
            OrderedEnemyIds = orderedEnemyIds
        };
        Main.LogInfo(
            $"The Execution List save recipe captured: " +
            $"OrderedEnemyCount={orderedEnemyIds.Count}");
        return true;
    }

    internal bool TryRestore(
        EncounterSession session,
        ExecutionListSaveRecipe recipe,
        out string failureReason) {
        failureReason = null;
        if (OrderedEnemies != null) {
            failureReason =
                "The Execution List is already active.";
            return false;
        }
        if (session == null) {
            failureReason =
                "The Execution List restore has no encounter session.";
            return false;
        }
        if (!session.SupportsEncounterType(
                EncounterType.Common)) {
            failureReason =
                "The persisted encounter does not support The Execution List.";
            return false;
        }
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
        if (savedIds.Count > MaximumPersistedEnemyCount) {
            failureReason =
                $"The Execution List ordered enemy ID count exceeds {MaximumPersistedEnemyCount}.";
            return false;
        }

        var uniqueSavedIds = new HashSet<string>(
            StringComparer.Ordinal);
        for (int index = 0;
             index < savedIds.Count;
             index++) {
            string id = savedIds[index];
            if (!IsValidPersistedId(id)) {
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

        EntityService entityService = EntityService.Instance;
        if (entityService == null) {
            failureReason =
                "EntityService is unavailable during The Execution List restoration.";
            return false;
        }

        var restoredEnemies = new List<BaseUnitEntity>(
            savedIds.Count);
        int resolvedSavedCount = 0;
        for (int index = 0;
             index < savedIds.Count;
             index++) {
            string savedId = savedIds[index];
            BaseUnitEntity unit =
                entityService.GetEntity<BaseUnitEntity>(
                    savedId);
            if (!IsValidLivingCombatEnemy(unit) ||
                !string.Equals(
                    unit.UniqueId,
                    savedId,
                    StringComparison.Ordinal)) {
                continue;
            }

            restoredEnemies.Add(unit);
            resolvedSavedCount++;
        }

        Game game = Game.Instance;
        if (game?.State?.AllBaseAwakeUnitsForSure == null) {
            failureReason =
                "The loaded awake-unit collection is unavailable during The Execution List restoration.";
            return false;
        }

        int currentLivingEnemyCount = 0;
        int appendedCurrentEnemyCount = 0;
        foreach (BaseUnitEntity candidate in
            game.State.AllBaseAwakeUnitsForSure) {
            if (!IsValidLivingCombatEnemy(candidate)) {
                continue;
            }

            currentLivingEnemyCount++;
            bool alreadyRestored = false;
            for (int index = 0;
                 index < restoredEnemies.Count;
                 index++) {
                if (ReferenceEquals(
                        restoredEnemies[index],
                        candidate)) {
                    alreadyRestored = true;
                    break;
                }
            }
            if (alreadyRestored) {
                continue;
            }

            restoredEnemies.Add(candidate);
            appendedCurrentEnemyCount++;
        }

        if (resolvedSavedCount == 0 &&
            currentLivingEnemyCount > 0) {
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

    private static bool IsValidPersistedId(string id) {
        return !string.IsNullOrWhiteSpace(id) &&
               id.Length <= MaximumEntityIdLength;
    }

    private static bool IsValidLivingCombatEnemy(
        BaseUnitEntity unit) {
        return unit != null &&
               unit is not StarshipEntity &&
               !unit.IsDisposed &&
               unit.IsInGame &&
               unit.IsInCombat &&
               unit.IsPlayerEnemy &&
               unit.LifeState != null &&
               !unit.LifeState.IsDead &&
               !unit.LifeState.IsFinallyDead;
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
