using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ChaosEncounters.UI;
using Kingmaker;
using Kingmaker.Blueprints.Classes.Experience;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Parts;
using UnityEngine;

namespace ChaosEncounters.Combat.Mechanics.Common;

internal sealed class RisingVengeanceMechanic :
    IEncounterMechanic {
    private const string MechanicId = "RisingVengeance";
    private const string HudTitle = "Rising Vengeance";
    private const string HudDescription =
        "Every fallen enemy strengthens those left behind. When an enemy dies, all surviving enemies, including reinforcements, gain marks equal to the defeated unit's rank, from I to VI, up to 8. Each mark grants +5% damage dealt, 10% damage reduction, and restores 10% of maximum health when gained. All enemies lose 2 marks at the end of each round. Example: a rank II death grants 2 marks; a rank IV death grants 4 marks.";
    private const int MaximumMarks = 8;
    private const int OutgoingIncreasePerMark = 5;
    private const int IncomingReductionPerMark = 10;
    private const int MarksLostPerRound = 2;
    private const float HealingFractionPerMark = 0.10f;

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

            int newMarks = state.Marks - MarksLostPerRound;
            if (newMarks <= 0) {
                markedEnemies.RemoveAt(index);
                ClearEffects(unit);
                continue;
            }

            markedEnemies[index] =
                new MarkedEnemyState(unit, newMarks);
            ApplyEffects(unit, newMarks);
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
