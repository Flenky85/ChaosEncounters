using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Kingmaker;
using Kingmaker.Blueprints;
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

        var stopwatch = Stopwatch.StartNew();
        long unitDataAndCalculationTicks = 0;
        long stringConstructionTicks = 0;
        long loggerCallTicks = 0;

        long phaseStarted = Stopwatch.GetTimestamp();
        Main.LogInfo("Combat started.");
        loggerCallTicks += Stopwatch.GetTimestamp() - phaseStarted;

        phaseStarted = Stopwatch.GetTimestamp();
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
        var encounterBlueprints = new List<BlueprintUnit>();
        unitDataAndCalculationTicks += Stopwatch.GetTimestamp() - phaseStarted;

        long loopStringConstructionTicks = stringConstructionTicks;
        long loopLoggerCallTicks = loggerCallTicks;
        phaseStarted = Stopwatch.GetTimestamp();
        foreach (var unit in Game.Instance.State.AllBaseAwakeUnitsForSure) {
            if (!unit.IsInCombat) {
                continue;
            }

            combatUnitCount++;

            if (!unit.IsPlayerFaction && unit.Blueprint != null) {
                encounterBlueprints.Add(unit.Blueprint);
            }

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

            string characterName = unit.CharacterName;
            string blueprintName = unit.Blueprint.name;
            bool isPet = unit.IsPet;
            bool isInPlayerParty = unit.IsInPlayerParty;
            bool isPlayerFaction = unit.IsPlayerFaction;
            bool isHelpingPlayerFaction = unit.IsHelpingPlayerFaction;
            bool isPlayerEnemy = unit.IsPlayerEnemy;
            bool isNeutral = unit.IsNeutral;
            bool isInCombat = unit.IsInCombat;
            UnitDifficultyType difficultyType = unit.Blueprint.DifficultyType;
            bool armyBased = unit.Blueprint.Army != null;

            long stringConstructionStarted = Stopwatch.GetTimestamp();
            string unitDiagnostic =
                $"Combat unit:\n" +
                $"  Name: {characterName}\n" +
                $"  Role: {role}\n" +
                $"  Blueprint: {blueprintName}\n" +
                $"  IsPet: {isPet}\n" +
                $"  IsInPlayerParty: {isInPlayerParty}\n" +
                $"  PlayerFaction: {isPlayerFaction}\n" +
                $"  IsHelpingPlayerFaction: {isHelpingPlayerFaction}\n" +
                $"  PlayerEnemy: {isPlayerEnemy}\n" +
                $"  IsNeutral: {isNeutral}\n" +
                $"  InCombat: {isInCombat}\n" +
                $"  DifficultyType: {difficultyType}\n" +
                $"  AreaCR: {areaCr}\n" +
                $"  ArmyBased: {armyBased}\n" +
                $"  NativeWeight: {nativeWeight}";
            stringConstructionTicks += Stopwatch.GetTimestamp() - stringConstructionStarted;

            long loggerCallStarted = Stopwatch.GetTimestamp();
            Main.LogInfo(unitDiagnostic);
            loggerCallTicks += Stopwatch.GetTimestamp() - loggerCallStarted;
        }
        int totalChallengeRating =
            Kingmaker.Cheats.Utilities.GetTotalChallengeRating(encounterBlueprints);
        unitDataAndCalculationTicks +=
            Stopwatch.GetTimestamp() - phaseStarted -
            (stringConstructionTicks - loopStringConstructionTicks) -
            (loggerCallTicks - loopLoggerCallTicks);

        phaseStarted = Stopwatch.GetTimestamp();
        string combatUnitSummary = $"Combat units: {combatUnitCount}";
        stringConstructionTicks += Stopwatch.GetTimestamp() - phaseStarted;

        phaseStarted = Stopwatch.GetTimestamp();
        Main.LogInfo(combatUnitSummary);
        loggerCallTicks += Stopwatch.GetTimestamp() - phaseStarted;

        phaseStarted = Stopwatch.GetTimestamp();
        string combatRoleSummary =
            $"Combat role summary:\n" +
            $"  MainCharacters: {mainCharacterCount}\n" +
            $"  Companions: {companionCount}\n" +
            $"  PlayerPets: {playerPetCount}\n" +
            $"  AlliedNpcs: {alliedNpcCount}\n" +
            $"  Enemies: {enemyCount}\n" +
            $"  NeutralUnits: {neutralCount}\n" +
            $"  OtherUnits: {otherUnitCount}";
        stringConstructionTicks += Stopwatch.GetTimestamp() - phaseStarted;

        phaseStarted = Stopwatch.GetTimestamp();
        Main.LogInfo(combatRoleSummary);
        loggerCallTicks += Stopwatch.GetTimestamp() - phaseStarted;

        phaseStarted = Stopwatch.GetTimestamp();
        string enemyDifficultySummary =
            $"Enemy difficulty summary:\n" +
            $"  AreaCR: {areaCr}\n" +
            $"  TotalChallengeRating: {totalChallengeRating}\n" +
            $"  Swarm: {swarmCount}\n" +
            $"  Common: {commonCount}\n" +
            $"  Hard: {hardCount}\n" +
            $"  Elite: {eliteCount}\n" +
            $"  MiniBoss: {miniBossCount}\n" +
            $"  Boss: {bossCount}\n" +
            $"  ChapterBoss: {chapterBossCount}\n" +
            $"  EnemiesWithoutArmy: {enemiesWithoutArmyCount}\n" +
            $"  TotalNativeEnemyWeight: {totalNativeEnemyWeight}";
        stringConstructionTicks += Stopwatch.GetTimestamp() - phaseStarted;

        phaseStarted = Stopwatch.GetTimestamp();
        Main.LogInfo(enemyDifficultySummary);
        long finalLoggerCallEnded = Stopwatch.GetTimestamp();
        stopwatch.Stop();
        loggerCallTicks += finalLoggerCallEnded - phaseStarted;

        double timestampTicksToMilliseconds = 1000.0 / Stopwatch.Frequency;
        double unitDataAndCalculationMilliseconds = unitDataAndCalculationTicks * timestampTicksToMilliseconds;
        double stringConstructionMilliseconds = stringConstructionTicks * timestampTicksToMilliseconds;
        double loggerCallMilliseconds = loggerCallTicks * timestampTicksToMilliseconds;
        double totalMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        double unclassifiedOverheadMilliseconds =
            totalMilliseconds -
            unitDataAndCalculationMilliseconds -
            stringConstructionMilliseconds -
            loggerCallMilliseconds;
        if (unclassifiedOverheadMilliseconds < 0) {
            unclassifiedOverheadMilliseconds = 0;
        }

        Main.LogInfo(
            $"Combat diagnostics timing:\n" +
            $"  Unit data and calculations: {unitDataAndCalculationMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms\n" +
            $"  Diagnostic string construction: {stringConstructionMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms\n" +
            $"  Logger calls: {loggerCallMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms\n" +
            $"  Unclassified overhead: {unclassifiedOverheadMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms\n" +
            $"  Total: {totalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms");
    }
}
