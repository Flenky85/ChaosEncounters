using System.Collections.ObjectModel;
using Kingmaker.Blueprints.Classes.Experience;
using Kingmaker.EntitySystem.Entities;

namespace ChaosEncounters.Combat;

internal enum EncounterType {
    Common,
    Boss
}

internal sealed class EncounterSession {
    internal ReadOnlyCollection<BaseUnitEntity> InitialEnemies { get; }
    internal EncounterType Type { get; }
    internal BaseUnitEntity Leader { get; }

    internal EncounterSession(
        List<BaseUnitEntity> initialEnemies,
        EncounterType type,
        BaseUnitEntity leader) {
        InitialEnemies = initialEnemies.AsReadOnly();
        Type = type;
        Leader = leader;
    }
}

internal static class EncounterClassifier {
    internal static void Classify(
        List<BaseUnitEntity> initialEnemies,
        out EncounterType encounterType,
        out BaseUnitEntity leader,
        out UnitDifficultyType? highestRank,
        out int highestRankTieCount,
        out string invalidCompositionReason) {
        encounterType = EncounterType.Common;
        leader = null;
        highestRank = null;
        highestRankTieCount = 0;
        invalidCompositionReason = null;

        if (initialEnemies == null || initialEnemies.Count == 0) {
            invalidCompositionReason = "The initial enemy snapshot is empty.";
            return;
        }

        int highestRankValue = -1;
        BaseUnitEntity uniqueHighestRankEnemy = null;
        for (int index = 0; index < initialEnemies.Count; index++) {
            BaseUnitEntity enemy = initialEnemies[index];
            if (enemy?.Blueprint == null) {
                highestRank = null;
                highestRankTieCount = 0;
                invalidCompositionReason =
                    $"Initial enemy at index {index} has no available blueprint.";
                return;
            }

            UnitDifficultyType rank = enemy.Blueprint.DifficultyType;
            if (!TryGetRankValue(rank, out int rankValue)) {
                highestRank = null;
                highestRankTieCount = 0;
                invalidCompositionReason =
                    $"Initial enemy at index {index} has unsupported rank value {(int)rank}.";
                return;
            }

            if (rankValue > highestRankValue) {
                highestRankValue = rankValue;
                highestRank = rank;
                highestRankTieCount = 1;
                uniqueHighestRankEnemy = enemy;
            } else if (rankValue == highestRankValue) {
                highestRankTieCount++;
                uniqueHighestRankEnemy = null;
            }
        }

        if (highestRankValue >= GetKnownRankValue(UnitDifficultyType.Elite) &&
            highestRankTieCount == 1) {
            encounterType = EncounterType.Boss;
            leader = uniqueHighestRankEnemy;
        }
    }

    private static bool TryGetRankValue(UnitDifficultyType rank, out int value) {
        switch (rank) {
            case UnitDifficultyType.Swarm:
                value = 0;
                return true;
            case UnitDifficultyType.Common:
                value = 1;
                return true;
            case UnitDifficultyType.Hard:
                value = 2;
                return true;
            case UnitDifficultyType.Elite:
                value = 3;
                return true;
            case UnitDifficultyType.MiniBoss:
                value = 4;
                return true;
            case UnitDifficultyType.Boss:
                value = 5;
                return true;
            case UnitDifficultyType.ChapterBoss:
                value = 6;
                return true;
            default:
                value = -1;
                return false;
        }
    }

    private static int GetKnownRankValue(UnitDifficultyType rank) {
        TryGetRankValue(rank, out int value);
        return value;
    }
}
