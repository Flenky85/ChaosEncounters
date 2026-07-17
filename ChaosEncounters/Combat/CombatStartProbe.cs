using Kingmaker;
using Kingmaker.Blueprints.Classes.Experience;
using Kingmaker.PubSubSystem.Core;
using System.Linq;

namespace ChaosEncounters.Combat;

internal sealed class CombatStartProbe : IPartyCombatHandler {
    private static readonly CombatStartProbe Instance = new();

    internal static void Initialize() {
        if (!EventBus.IsGloballySubscribed(Instance)) {
            EventBus.Subscribe(Instance);
        }
    }

    public void HandlePartyCombatStateChanged(bool inCombat) {
        if (!inCombat) {
            return;
        }

        Main.LogInfo("Combat started.");

        int areaCr = Game.Instance.CurrentlyLoadedArea?.GetCR() ?? 0;
        var combatUnits = Game.Instance.State.AllBaseAwakeUnitsForSure
            .Where(unit => unit.IsInCombat)
            .ToList();

        int playerFactionUnitCount = 0;
        int playerEnemyCount = 0;
        int otherUnitCount = 0;
        int swarmCount = 0;
        int commonCount = 0;
        int hardCount = 0;
        int eliteCount = 0;
        int miniBossCount = 0;
        int bossCount = 0;
        int chapterBossCount = 0;
        int enemiesWithoutArmyCount = 0;
        int totalNativeEnemyWeight = 0;

        Main.LogInfo($"Combat units: {combatUnits.Count}");
        foreach (var unit in combatUnits) {
            if (unit.IsPlayerFaction) {
                playerFactionUnitCount++;
            } else if (unit.IsPlayerEnemy) {
                playerEnemyCount++;
            } else {
                otherUnitCount++;
            }

            switch (unit.Blueprint.DifficultyType) {
                case UnitDifficultyType.Swarm:
                    swarmCount++;
                    break;
                case UnitDifficultyType.Common:
                    commonCount++;
                    break;
                case UnitDifficultyType.Hard:
                    hardCount++;
                    break;
                case UnitDifficultyType.Elite:
                    eliteCount++;
                    break;
                case UnitDifficultyType.MiniBoss:
                    miniBossCount++;
                    break;
                case UnitDifficultyType.Boss:
                    bossCount++;
                    break;
                case UnitDifficultyType.ChapterBoss:
                    chapterBossCount++;
                    break;
            }

            int nativeWeight = 0;
            if (unit.IsPlayerEnemy) {
                nativeWeight = ExperienceHelper.GetMobExp(unit.Blueprint.DifficultyType, areaCr);
                totalNativeEnemyWeight += nativeWeight;
                if (unit.Blueprint.Army == null) {
                    enemiesWithoutArmyCount++;
                }
            }

            Main.LogInfo(
                $"Combat unit:\n" +
                $"  Name: {unit.CharacterName}\n" +
                $"  Blueprint: {unit.Blueprint.name}\n" +
                $"  PlayerFaction: {unit.IsPlayerFaction}\n" +
                $"  PlayerEnemy: {unit.IsPlayerEnemy}\n" +
                $"  InCombat: {unit.IsInCombat}\n" +
                $"  DifficultyType: {unit.Blueprint.DifficultyType}\n" +
                $"  AreaCR: {areaCr}\n" +
                $"  ArmyBased: {unit.Blueprint.Army != null}\n" +
                $"  NativeWeight: {nativeWeight}");
        }

        Main.LogInfo(
            $"Combat difficulty summary:\n" +
            $"  AreaCR: {areaCr}\n" +
            $"  PlayerFactionUnits: {playerFactionUnitCount}\n" +
            $"  PlayerEnemies: {playerEnemyCount}\n" +
            $"  OtherUnits: {otherUnitCount}\n" +
            $"  Swarm: {swarmCount}\n" +
            $"  Common: {commonCount}\n" +
            $"  Hard: {hardCount}\n" +
            $"  Elite: {eliteCount}\n" +
            $"  MiniBoss: {miniBossCount}\n" +
            $"  Boss: {bossCount}\n" +
            $"  ChapterBoss: {chapterBossCount}\n" +
            $"  EnemiesWithoutArmy: {enemiesWithoutArmyCount}\n" +
            $"  TotalNativeEnemyWeight: {totalNativeEnemyWeight}");
    }
}
