using System.Diagnostics;
using System.Globalization;
using Kingmaker;
using Kingmaker.Blueprints.Classes.Experience;
using Kingmaker.Controllers.TurnBased;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.PubSubSystem.Core;

namespace ChaosEncounters.Combat;

internal sealed class CombatStartProbe :
    IPartyCombatHandler,
    IRoundStartHandler,
    ITurnStartHandler,
    ITurnEndHandler,
    IUnitDieHandler {
    private static readonly CombatStartProbe Instance = new();

    internal static void Initialize() {
        if (!EventBus.IsGloballySubscribed(Instance)) {
            EventBus.Subscribe(Instance);
        }
    }

    public void HandleRoundStart(bool isTurnBased) {
        if (!isTurnBased || !Game.Instance.TurnController.TbActive) {
            return;
        }

        Main.LogInfo(
            $"Combat round started:\n" +
            $"  CombatRound: {Game.Instance.TurnController.CombatRound}");
    }

    public void HandleUnitStartTurn(bool isTurnBased) {
        if (!isTurnBased ||
            EventInvokerExtensions.MechanicEntity is not BaseUnitEntity unit ||
            !unit.IsInCombat) {
            return;
        }

        var player = Game.Instance.Player;
        bool isMainCharacter = unit == player.MainCharacterEntity;
        bool isPlayerCharacter = player.Party.Contains(unit);
        bool isPlayerPet =
            !isPlayerCharacter &&
            unit.IsPet &&
            player.PartyAndPets.Contains(unit);
        string role = GetUnitRole(unit, isMainCharacter, isPlayerCharacter, isPlayerPet);
        string characterName = unit.CharacterName;
        string blueprintName = unit.Blueprint.name;

        Main.LogInfo(
            $"Unit turn started:\n" +
            $"  CombatRound: {Game.Instance.TurnController.CombatRound}\n" +
            $"  Name: {characterName}\n" +
            $"  Role: {role}\n" +
            $"  Blueprint: {blueprintName}\n" +
            $"  PlayerFaction: {unit.IsPlayerFaction}\n" +
            $"  PlayerEnemy: {unit.IsPlayerEnemy}\n" +
            $"  InCombat: {unit.IsInCombat}");
    }

    public void HandleUnitEndTurn(bool isTurnBased) {
        if (!isTurnBased ||
            EventInvokerExtensions.MechanicEntity is not BaseUnitEntity unit ||
            !unit.IsInCombat) {
            return;
        }

        var player = Game.Instance.Player;
        bool isMainCharacter = unit == player.MainCharacterEntity;
        bool isPlayerCharacter = player.Party.Contains(unit);
        bool isPlayerPet =
            !isPlayerCharacter &&
            unit.IsPet &&
            player.PartyAndPets.Contains(unit);
        string role = GetUnitRole(unit, isMainCharacter, isPlayerCharacter, isPlayerPet);
        string characterName = unit.CharacterName;
        string blueprintName = unit.Blueprint.name;

        Main.LogInfo(
            $"Unit turn ended:\n" +
            $"  CombatRound: {Game.Instance.TurnController.CombatRound}\n" +
            $"  Name: {characterName}\n" +
            $"  Role: {role}\n" +
            $"  Blueprint: {blueprintName}\n" +
            $"  PlayerFaction: {unit.IsPlayerFaction}\n" +
            $"  PlayerEnemy: {unit.IsPlayerEnemy}\n" +
            $"  InCombat: {unit.IsInCombat}");
    }

    public void OnUnitDie() {
        if (EventInvokerExtensions.AbstractUnitEntity is not BaseUnitEntity unit ||
            !unit.IsInCombat ||
            !unit.IsPlayerEnemy ||
            !unit.LifeState.IsDead) {
            return;
        }

        string characterName = unit.CharacterName;
        string blueprintName = unit.Blueprint.name;

        Main.LogInfo(
            $"Enemy died:\n" +
            $"  CombatRound: {Game.Instance.TurnController.CombatRound}\n" +
            $"  Name: {characterName}\n" +
            $"  Blueprint: {blueprintName}\n" +
            $"  DifficultyType: {unit.Blueprint.DifficultyType}\n" +
            $"  PlayerEnemy: {unit.IsPlayerEnemy}\n" +
            $"  InCombat: {unit.IsInCombat}\n" +
            $"  IsDead: {unit.LifeState.IsDead}");
    }

    public void HandlePartyCombatStateChanged(bool inCombat) {
        if (!inCombat) {
            Main.LogInfo("Combat ended.");
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
        unitDataAndCalculationTicks += Stopwatch.GetTimestamp() - phaseStarted;

        long loopStringConstructionTicks = stringConstructionTicks;
        long loopLoggerCallTicks = loggerCallTicks;
        phaseStarted = Stopwatch.GetTimestamp();
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

            string role = GetUnitRole(unit, isMainCharacter, isPlayerCharacter, isPlayerPet);
            switch (role) {
                case "MainCharacter":
                    mainCharacterCount++;
                    break;
                case "Companion":
                    companionCount++;
                    break;
                case "PlayerPet":
                    playerPetCount++;
                    break;
                case "Enemy":
                    enemyCount++;
                    break;
                case "AlliedNpc":
                    alliedNpcCount++;
                    break;
                case "Neutral":
                    neutralCount++;
                    break;
                default:
                    otherUnitCount++;
                    break;
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

    private static string GetUnitRole(
        BaseUnitEntity unit,
        bool isMainCharacter,
        bool isPlayerCharacter,
        bool isPlayerPet) {
        if (isMainCharacter) {
            return "MainCharacter";
        }
        if (isPlayerCharacter) {
            return "Companion";
        }
        if (isPlayerPet) {
            return "PlayerPet";
        }
        if (unit.IsPlayerEnemy) {
            return "Enemy";
        }
        if (unit.IsPlayerFaction || unit.IsHelpingPlayerFaction) {
            return "AlliedNpc";
        }
        if (unit.IsNeutral) {
            return "Neutral";
        }
        return "Other";
    }
}
