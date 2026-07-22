using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ChaosEncounters.Combat.Persistence;
using ChaosEncounters.UI;
using Kingmaker;
using Kingmaker.Blueprints.Classes.Experience;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Parts;
using UnityEngine;

namespace ChaosEncounters.Combat.Mechanics.Common;

internal sealed class RisingVengeanceMechanic :
    IEncounterMechanic,
    IPersistableEncounterMechanic {
    private const string MechanicId = "RisingVengeance";
    private const string HudTitle = "Rising Vengeance";
    private const string HudDescription =
        "Every fallen enemy strengthens those left behind. When an enemy dies, all surviving enemies, including reinforcements, gain marks equal to the defeated unit's rank, from I to VI, accumulating up to 20. Each mark grants +1% damage dealt and 4% damage reduction, and each newly gained mark restores 5% of maximum health. At the end of every round, each enemy loses half of its marks, rounding the number lost down. Example: a rank II death grants 2 marks; a rank IV death grants 4 marks.";
    private const int MaximumMarks = 20;
    private const int OutgoingIncreasePerMark = 1;
    private const int IncomingReductionPerMark = 4;
    private const float HealingFractionPerMark = 0.05f;

    private List<MarkedEnemyState> MarkedEnemies;
    private HashSet<BaseUnitEntity> ProcessedDeaths;
    private bool UnsupportedRankWarningLogged;

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
            if (IsEligibleLivingEnemy(
                    session.InitialEnemies[index])) {
                return true;
            }
        }

        return false;
    }

    public void Activate(EncounterSession session) {
        if (session == null) {
            throw new InvalidOperationException(
                "Rising Vengeance requires an encounter session.");
        }
        if (MarkedEnemies != null ||
            ProcessedDeaths != null) {
            throw new InvalidOperationException(
                "Rising Vengeance is already active.");
        }
        if (!session.SupportsEncounterType(
                EncounterType.Common)) {
            throw new InvalidOperationException(
                "Rising Vengeance requires Common encounter eligibility.");
        }

        bool hasLivingInitialEnemy = false;
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            if (IsEligibleLivingEnemy(
                    session.InitialEnemies[index])) {
                hasLivingInitialEnemy = true;
                break;
            }
        }
        if (!hasLivingInitialEnemy) {
            throw new InvalidOperationException(
                "Rising Vengeance requires at least one living initial terrestrial enemy.");
        }

        MarkedEnemies = new List<MarkedEnemyState>();
        ProcessedDeaths = new HashSet<BaseUnitEntity>(
            UnitReferenceComparer.Instance);
        UnsupportedRankWarningLogged = false;

        EncounterHud.Show(
            HudTitle,
            HudDescription);
        Main.LogInfo("Rising Vengeance activated.");
    }

    public void HandleRoundStart(int combatRound) {
    }

    public void HandleRoundEnd(int combatRound) {
        List<MarkedEnemyState> markedEnemies =
            MarkedEnemies;
        if (markedEnemies == null) {
            return;
        }

        for (int index = markedEnemies.Count - 1;
             index >= 0;
             index--) {
            MarkedEnemyState state = markedEnemies[index];
            BaseUnitEntity unit = state.Unit;
            if (!IsEligibleLivingEnemy(unit)) {
                markedEnemies.RemoveAt(index);
                DamageControl.ClearPolicy(unit);
                UnitMarker.ClearMarker(unit);
                continue;
            }

            int marksLost = state.Marks / 2;
            int remainingMarks = state.Marks - marksLost;
            if (remainingMarks <= 0) {
                markedEnemies.RemoveAt(index);
                ClearEffects(unit);
                continue;
            }
            if (remainingMarks == state.Marks) {
                continue;
            }

            markedEnemies[index] =
                new MarkedEnemyState(unit, remainingMarks);
            ApplyEffects(unit, remainingMarks);
        }
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
        List<MarkedEnemyState> markedEnemies =
            MarkedEnemies;
        HashSet<BaseUnitEntity> processedDeaths =
            ProcessedDeaths;
        if (markedEnemies == null ||
            processedDeaths == null ||
            unit == null ||
            unit is StarshipEntity ||
            processedDeaths.Contains(unit)) {
            return;
        }

        int deadIndex = FindMarkedEnemyIndex(
            markedEnemies,
            unit);
        if (deadIndex >= 0) {
            markedEnemies.RemoveAt(deadIndex);
            DamageControl.ClearPolicy(unit);
            UnitMarker.ClearMarker(unit);
        }

        processedDeaths.Add(unit);
        if (!TryGetGrantedMarks(unit, out int grantedMarks)) {
            LogUnsupportedRankOnce(unit);
            return;
        }

        Game game = Game.Instance;
        if (game?.State == null) {
            return;
        }

        bool foundLivingRecipient = false;
        foreach (BaseUnitEntity candidate in
            game.State.AllBaseAwakeUnitsForSure) {
            if (ReferenceEquals(candidate, unit) ||
                !IsEligibleLivingEnemy(candidate)) {
                continue;
            }

            foundLivingRecipient = true;
            AddMarks(candidate, grantedMarks);
        }

        if (!foundLivingRecipient) {
            EncounterHud.Hide();
        }
    }

    public void Deactivate(
        EncounterMechanicEndReason reason) {
        MarkedEnemies?.Clear();
        ProcessedDeaths?.Clear();
        MarkedEnemies = null;
        ProcessedDeaths = null;
        UnsupportedRankWarningLogged = false;
    }

    bool IPersistableEncounterMechanic.TryCaptureSaveData(
        EncounterMechanicSaveData saveData,
        out string failureReason) {
        failureReason = null;
        if (saveData == null) {
            failureReason =
                "The Rising Vengeance save-data container is unavailable.";
            return false;
        }
        if (saveData.RisingVengeance != null) {
            failureReason =
                "The Rising Vengeance save-data container is already populated.";
            return false;
        }

        List<MarkedEnemyState> markedEnemies =
            MarkedEnemies;
        if (markedEnemies == null ||
            ProcessedDeaths == null) {
            failureReason =
                "Rising Vengeance is not initialized for save capture.";
            return false;
        }
        if (markedEnemies.Count >
            EncounterPersistenceValidation.MaximumEntityCount) {
            failureReason =
                $"The Rising Vengeance marked-enemy count exceeds {EncounterPersistenceValidation.MaximumEntityCount}.";
            return false;
        }

        var savedEnemies =
            new List<RisingVengeanceMarkedEnemySaveData>(
                markedEnemies.Count);
        var uniqueIds = new HashSet<string>(
            StringComparer.Ordinal);
        for (int index = 0;
             index < markedEnemies.Count;
             index++) {
            MarkedEnemyState state = markedEnemies[index];
            BaseUnitEntity unit = state.Unit;
            if (unit == null ||
                unit is StarshipEntity ||
                unit.IsDisposed ||
                !unit.IsInGame ||
                !unit.IsInCombat ||
                !unit.IsPlayerEnemy ||
                unit.LifeState == null ||
                unit.LifeState.IsDead ||
                unit.LifeState.IsFinallyDead) {
                failureReason =
                    $"The Rising Vengeance marked enemy at index {index} is not a valid living combat enemy.";
                return false;
            }

            string id = unit.UniqueId;
            if (!EncounterPersistenceValidation
                    .IsValidEntityId(id)) {
                failureReason =
                    $"The Rising Vengeance marked enemy at index {index} has an invalid persistent ID.";
                return false;
            }
            if (!uniqueIds.Add(id)) {
                failureReason =
                    $"The Rising Vengeance marked enemy at index {index} has a duplicate persistent ID.";
                return false;
            }
            if (state.Marks < 1 ||
                state.Marks > MaximumMarks) {
                failureReason =
                    $"The Rising Vengeance marked enemy at index {index} has invalid Marks={state.Marks}.";
                return false;
            }

            savedEnemies.Add(
                new RisingVengeanceMarkedEnemySaveData {
                    UnitId = id,
                    Marks = state.Marks
                });
        }

        saveData.RisingVengeance =
            new RisingVengeanceSaveRecipe {
                MarkedEnemies = savedEnemies
            };
        return true;
    }

    bool IPersistableEncounterMechanic.TryRestoreFromSave(
        EncounterRestoreContext context,
        EncounterMechanicSaveData saveData,
        out string failureReason) {
        failureReason = null;
        if (MarkedEnemies != null ||
            ProcessedDeaths != null) {
            failureReason =
                "Rising Vengeance is already active.";
            return false;
        }
        if (context == null) {
            failureReason =
                "The Rising Vengeance restore context is unavailable.";
            return false;
        }
        if (saveData == null) {
            failureReason =
                "The Rising Vengeance save-data container is unavailable.";
            return false;
        }

        RisingVengeanceSaveRecipe recipe =
            saveData.RisingVengeance;
        if (recipe == null) {
            failureReason =
                "The Rising Vengeance save recipe is missing.";
            return false;
        }
        List<RisingVengeanceMarkedEnemySaveData> savedEnemies =
            recipe.MarkedEnemies;
        if (savedEnemies == null) {
            failureReason =
                "The Rising Vengeance marked-enemy list is missing.";
            return false;
        }
        if (savedEnemies.Count >
            EncounterPersistenceValidation.MaximumEntityCount) {
            failureReason =
                $"The Rising Vengeance marked-enemy count exceeds {EncounterPersistenceValidation.MaximumEntityCount}.";
            return false;
        }

        var uniqueIds = new HashSet<string>(
            StringComparer.Ordinal);
        for (int index = 0;
             index < savedEnemies.Count;
             index++) {
            RisingVengeanceMarkedEnemySaveData entry =
                savedEnemies[index];
            if (entry == null ||
                !EncounterPersistenceValidation
                    .IsValidEntityId(entry.UnitId)) {
                failureReason =
                    $"The Rising Vengeance saved entry at index {index} has an invalid persistent ID.";
                return false;
            }
            if (!uniqueIds.Add(entry.UnitId)) {
                failureReason =
                    $"The Rising Vengeance saved entry at index {index} has a duplicate persistent ID.";
                return false;
            }
            if (entry.Marks < 1 ||
                entry.Marks > MaximumMarks) {
                failureReason =
                    $"The Rising Vengeance saved entry at index {index} has invalid Marks={entry.Marks}.";
                return false;
            }
        }

        var restoredEnemies = new List<MarkedEnemyState>(
            savedEnemies.Count);
        for (int index = 0;
             index < savedEnemies.Count;
             index++) {
            RisingVengeanceMarkedEnemySaveData entry =
                savedEnemies[index];
            if (!context.TryResolveEnemy(
                    entry.UnitId,
                    requireLiving: true,
                    out BaseUnitEntity unit)) {
                continue;
            }

            restoredEnemies.Add(
                new MarkedEnemyState(
                    unit,
                    entry.Marks));
        }

        MarkedEnemies = restoredEnemies;
        ProcessedDeaths = new HashSet<BaseUnitEntity>(
            UnitReferenceComparer.Instance);
        UnsupportedRankWarningLogged = false;

        for (int index = 0;
             index < restoredEnemies.Count;
             index++) {
            MarkedEnemyState state = restoredEnemies[index];
            ApplyEffects(
                state.Unit,
                state.Marks);
        }

        if (context.LivingEnemies.Count > 0) {
            EncounterHud.Show(
                HudTitle,
                HudDescription);
        } else {
            EncounterHud.Hide();
        }

        int skippedMarkedCount =
            savedEnemies.Count - restoredEnemies.Count;
        Main.LogInfo(
            $"Rising Vengeance restored: " +
            $"SavedMarkedCount={savedEnemies.Count} " +
            $"ResolvedMarkedCount={restoredEnemies.Count} " +
            $"SkippedMarkedCount={skippedMarkedCount}");
        return true;
    }

    private void AddMarks(
        BaseUnitEntity unit,
        int grantedMarks) {
        List<MarkedEnemyState> markedEnemies =
            MarkedEnemies;
        if (markedEnemies == null) {
            return;
        }

        int markedIndex = FindMarkedEnemyIndex(
            markedEnemies,
            unit);
        int currentMarks = markedIndex >= 0
            ? markedEnemies[markedIndex].Marks
            : 0;
        int availableMarks = MaximumMarks - currentMarks;
        int actualGainedMarks = grantedMarks < availableMarks
            ? grantedMarks
            : availableMarks;
        if (actualGainedMarks <= 0) {
            return;
        }

        int newMarks = currentMarks + actualGainedMarks;
        var newState = new MarkedEnemyState(unit, newMarks);
        if (markedIndex >= 0) {
            markedEnemies[markedIndex] = newState;
        } else {
            markedEnemies.Add(newState);
        }

        ApplyEffects(unit, newMarks);

        PartHealth health = unit.GetHealthOptional();
        int maximumHitPoints = health?.MaxHitPoints ?? 0;
        if (maximumHitPoints <= 0) {
            return;
        }

        int requestedHealing = Mathf.RoundToInt(
            maximumHitPoints *
            actualGainedMarks *
            HealingFractionPerMark);
        if (requestedHealing > 0) {
            HitPointRestoration.RestoreHitPoints(
                unit,
                requestedHealing);
        }
    }

    private static void ApplyEffects(
        BaseUnitEntity unit,
        int marks) {
        if (marks <= 0) {
            ClearEffects(unit);
            return;
        }

        DamageControl.SetOutgoingDamageIncrease(
            unit,
            marks * OutgoingIncreasePerMark);
        DamageControl.SetIncomingDamageReduction(
            unit,
            marks * IncomingReductionPerMark);
        UnitMarker.SetMarker(
            unit,
            marks.ToString(),
            GetMarkerColor(marks));
    }

    private static void ClearEffects(BaseUnitEntity unit) {
        DamageControl.ClearPolicy(unit);
        UnitMarker.ClearMarker(unit);
    }

    private static int FindMarkedEnemyIndex(
        List<MarkedEnemyState> markedEnemies,
        BaseUnitEntity unit) {
        for (int index = 0;
             index < markedEnemies.Count;
             index++) {
            if (ReferenceEquals(
                    markedEnemies[index].Unit,
                    unit)) {
                return index;
            }
        }

        return -1;
    }

    private static bool IsEligibleLivingEnemy(
        BaseUnitEntity unit) {
        return unit != null &&
               unit is not StarshipEntity &&
               !unit.IsDisposed &&
               unit.IsInGame &&
               unit.IsInCombat &&
               unit.IsPlayerEnemy &&
               !unit.LifeState.IsDead;
    }

    private static bool TryGetGrantedMarks(
        BaseUnitEntity unit,
        out int grantedMarks) {
        if (unit?.Blueprint == null) {
            grantedMarks = 0;
            return false;
        }

        switch (unit.Blueprint.DifficultyType) {
            case UnitDifficultyType.Swarm:
                grantedMarks = 1;
                return true;
            case UnitDifficultyType.Common:
                grantedMarks = 2;
                return true;
            case UnitDifficultyType.Hard:
                grantedMarks = 3;
                return true;
            case UnitDifficultyType.Elite:
                grantedMarks = 4;
                return true;
            case UnitDifficultyType.MiniBoss:
                grantedMarks = 5;
                return true;
            case UnitDifficultyType.Boss:
            case UnitDifficultyType.ChapterBoss:
                grantedMarks = 6;
                return true;
            default:
                grantedMarks = 0;
                return false;
        }
    }

    private void LogUnsupportedRankOnce(BaseUnitEntity unit) {
        if (UnsupportedRankWarningLogged) {
            return;
        }

        UnsupportedRankWarningLogged = true;
        string rank = unit?.Blueprint == null
            ? "Unavailable"
            : $"{unit.Blueprint.DifficultyType} " +
              $"({(int)unit.Blueprint.DifficultyType})";
        Main.LogWarning(
            $"Rising Vengeance ignored an unsupported defeated-unit rank: Rank={rank}");
    }

    private static Color32 GetMarkerColor(int marks) {
        if (marks <= 2) {
            return ChaosColors.Green;
        }
        if (marks <= 4) {
            return ChaosColors.Yellow;
        }
        if (marks <= 6) {
            return ChaosColors.Orange;
        }

        return ChaosColors.Red;
    }

    private readonly struct MarkedEnemyState {
        internal BaseUnitEntity Unit { get; }
        internal int Marks { get; }

        internal MarkedEnemyState(
            BaseUnitEntity unit,
            int marks) {
            Unit = unit;
            Marks = marks;
        }
    }

    private sealed class UnitReferenceComparer :
        IEqualityComparer<BaseUnitEntity> {
        internal static readonly UnitReferenceComparer Instance = new();

        public bool Equals(BaseUnitEntity x, BaseUnitEntity y) {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(BaseUnitEntity unit) {
            return RuntimeHelpers.GetHashCode(unit);
        }
    }
}
