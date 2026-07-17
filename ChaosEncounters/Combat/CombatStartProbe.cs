using Kingmaker;
using Kingmaker.Blueprints.Classes.Experience;
using Kingmaker.PubSubSystem.Core;

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
        var player = Game.Instance.Player;
        var mainCharacter = player.MainCharacterEntity;
        var party = player.Party;
        var partyAndPets = player.PartyAndPets;

        int combatUnitCount = 0;
        int mainCharacterCount = 0;
        int companionCount = 0;
        int playerPetCount = 0;
        int alliedNpcCount = 0;
        int enemyCount = 0;
        int neutralCount = 0;
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

        foreach (var unit in Game.Instance.State.AllBaseAwakeUnitsForSure) {
            if (!unit.IsInCombat) {
                continue;
            }

            combatUnitCount++;

            bool isMainCharacter = unit == mainCharacter;
            bool isPlayerCharacter = party.Contains(unit);
            bool isPlayerPet =
                !isPlayerCharacter &&
                unit.IsPet &&
                partyAndPets.Contains(unit);

            string role;
            if (isMainCharacter) {
                role = "MainCharacter";
                mainCharacterCount++;
            } else if (isPlayerCharacter) {
                role = "Companion";
                companionCount++;
            } else if (isPlayerPet) {
                role = "PlayerPet";
                playerPetCount++;
            } else if (unit.IsPlayerEnemy) {
                role = "Enemy";
                enemyCount++;
            } else if (unit.IsPlayerFaction || unit.IsHelpingPlayerFaction) {
                role = "AlliedNpc";
                alliedNpcCount++;
            } else if (unit.IsNeutral) {
                role = "Neutral";
                neutralCount++;
            } else {
                role = "Other";
                otherUnitCount++;
            }

            int nativeWeight = 0;
            if (unit.IsPlayerEnemy) {
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

                nativeWeight = ExperienceHelper.GetMobExp(unit.Blueprint.DifficultyType, areaCr);
                totalNativeEnemyWeight += nativeWeight;
                if (unit.Blueprint.Army == null) {
                    enemiesWithoutArmyCount++;
                }
            }

            Main.LogInfo(
                $"Combat unit:\n" +
                $"  Name: {unit.CharacterName}\n" +
                $"  Role: {role}\n" +
                $"  Blueprint: {unit.Blueprint.name}\n" +
                $"  IsPet: {unit.IsPet}\n" +
                $"  IsInPlayerParty: {unit.IsInPlayerParty}\n" +
                $"  PlayerFaction: {unit.IsPlayerFaction}\n" +
                $"  IsHelpingPlayerFaction: {unit.IsHelpingPlayerFaction}\n" +
                $"  PlayerEnemy: {unit.IsPlayerEnemy}\n" +
                $"  IsNeutral: {unit.IsNeutral}\n" +
                $"  InCombat: {unit.IsInCombat}\n" +
                $"  DifficultyType: {unit.Blueprint.DifficultyType}\n" +
                $"  AreaCR: {areaCr}\n" +
                $"  ArmyBased: {unit.Blueprint.Army != null}\n" +
                $"  NativeWeight: {nativeWeight}");
        }

        Main.LogInfo($"Combat units: {combatUnitCount}");

        Main.LogInfo(
            $"Combat role summary:\n" +
            $"  MainCharacters: {mainCharacterCount}\n" +
            $"  Companions: {companionCount}\n" +
            $"  PlayerPets: {playerPetCount}\n" +
            $"  AlliedNpcs: {alliedNpcCount}\n" +
            $"  Enemies: {enemyCount}\n" +
            $"  NeutralUnits: {neutralCount}\n" +
            $"  OtherUnits: {otherUnitCount}");

        Main.LogInfo(
            $"Enemy difficulty summary:\n" +
            $"  AreaCR: {areaCr}\n" +
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
