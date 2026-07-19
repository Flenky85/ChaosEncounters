using System.Diagnostics;
using System.Globalization;
using Kingmaker;
using Kingmaker.Blueprints.Classes.Experience;
using Kingmaker.Controllers.TurnBased;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.GameModes;
using Kingmaker.PubSubSystem;
using Kingmaker.PubSubSystem.Core;
using ChaosEncounters.UI;

namespace ChaosEncounters.Combat;

internal sealed class EncounterRuntime :
    IPartyCombatHandler,
    IRoundStartHandler,
    IRoundEndHandler,
    ITurnStartHandler,
    ITurnEndHandler,
    IUnitDieHandler {
    private static readonly EncounterRuntime Instance = new();
    private static EncounterSession CurrentSession;
    private static bool SessionActivated;
    private static int ActivationCombatRound;
    private static bool DuplicateStartWarningLogged;
    private static bool RuntimeFaulted;
    private static bool RuntimeFaultLogged;

    internal static void Initialize() {
        if (!EventBus.IsGloballySubscribed(Instance)) {
            EventBus.Subscribe(Instance);
        }
    }

    public void HandleRoundStart(bool isTurnBased) {
        if (!isTurnBased ||
            RuntimeFaulted ||
            CurrentSession == null) {
            return;
        }

        try {
            Game game = Game.Instance;
            if (game?.TurnController?.TbActive != true) {
                return;
            }

            int combatRound = game.TurnController.CombatRound;
            if (!SessionActivated) {
                SessionActivated = true;
                ActivationCombatRound = combatRound;
                Main.LogInfo(
                    $"Encounter runtime session activated:\n" +
                    $"  CombatRound: {ActivationCombatRound}");
            }

            Main.LogInfo(
                $"Combat round started:\n" +
                $"  CombatRound: {combatRound}");
        } catch (Exception exception) {
            FaultRuntime(nameof(HandleRoundStart), exception);
        }
    }

    public void HandleRoundEnd(bool isTurnBased, bool isFirst) {
        if (!isTurnBased ||
            isFirst ||
            RuntimeFaulted ||
            CurrentSession == null ||
            !SessionActivated) {
            return;
        }

        try {
            Game game = Game.Instance;
            if (game?.TurnController?.TbActive != true ||
                game.CurrentMode == GameModeType.SpaceCombat ||
                game.CurrentMode == GameModeType.StarSystem) {
                return;
            }

            int combatRound = game.TurnController.CombatRound;
            Main.LogInfo(
                $"Encounter runtime valid round end dispatched:\n" +
                $"  CombatRound: {combatRound}");
        } catch (Exception exception) {
            FaultRuntime(nameof(HandleRoundEnd), exception);
        }
    }

    public void HandleUnitStartTurn(bool isTurnBased) {
        if (!isTurnBased ||
            RuntimeFaulted ||
            CurrentSession == null ||
            !SessionActivated ||
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
            RuntimeFaulted ||
            CurrentSession == null ||
            !SessionActivated ||
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
        if (RuntimeFaulted ||
            CurrentSession == null ||
            !SessionActivated) {
            return;
        }

        try {
            if (EventInvokerExtensions.AbstractUnitEntity is not BaseUnitEntity unit ||
                !unit.IsInCombat ||
                !unit.IsPlayerEnemy ||
                !unit.LifeState.IsDead) {
                return;
            }

            string characterName = unit.CharacterName;
            string blueprintName = unit.Blueprint.name;

            Main.LogInfo(
                $"Encounter runtime enemy death dispatched:\n" +
                $"  CombatRound: {Game.Instance.TurnController.CombatRound}\n" +
                $"  Name: {characterName}\n" +
                $"  Blueprint: {blueprintName}\n" +
                $"  DifficultyType: {unit.Blueprint.DifficultyType}\n" +
                $"  PlayerEnemy: {unit.IsPlayerEnemy}\n" +
                $"  InCombat: {unit.IsInCombat}\n" +
                $"  IsDead: {unit.LifeState.IsDead}");
        } catch (Exception exception) {
            FaultRuntime(nameof(OnUnitDie), exception);
        }
    }

    public void HandlePartyCombatStateChanged(bool inCombat) {
        if (!inCombat) {
            HandleCombatEnd();
            return;
        }

        if (RuntimeFaulted) {
            return;
        }

        try {
            if (CurrentSession != null) {
                if (!DuplicateStartWarningLogged) {
                    Main.LogWarning("Combat-start callback was received while an encounter diagnostic session already exists; the initial immutable session was preserved.");
                    DuplicateStartWarningLogged = true;
                }
                return;
            }

            SessionActivated = false;
            ActivationCombatRound = 0;
            DuplicateStartWarningLogged = false;
            RuntimeFaulted = false;
            RuntimeFaultLogged = false;
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
        var initialEnemies = new List<BaseUnitEntity>();
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
                initialEnemies.Add(unit);
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
        EncounterClassifier.Classify(
            initialEnemies,
            out EncounterType encounterType,
            out BaseUnitEntity leader,
            out UnitDifficultyType? highestRank,
            out int highestRankTieCount,
            out string invalidCompositionReason);
        CurrentSession = new EncounterSession(initialEnemies, encounterType, leader);
        Main.LogInfo("Encounter runtime session created; activation is pending the first valid turn-based round start.");
        string leaderName = leader?.CharacterName ?? "None";
        string leaderBlueprint = leader?.Blueprint?.name ?? "None";
        string leaderRank = leader == null ? "None" : highestRank.Value.ToString();
        string highestRankText = highestRank?.ToString() ?? "None";
        string invalidCompositionReasonText = invalidCompositionReason ?? "None";
        unitDataAndCalculationTicks += Stopwatch.GetTimestamp() - phaseStarted;

        phaseStarted = Stopwatch.GetTimestamp();
        string classificationDiagnostic =
            $"Initial immutable encounter classification:\n" +
            $"  EncounterType: {CurrentSession.Type}\n" +
            $"  InitialEnemyCount: {CurrentSession.InitialEnemies.Count}\n" +
            $"  HighestRank: {highestRankText}\n" +
            $"  HighestRankTieCount: {highestRankTieCount}\n" +
            $"  LeaderName: {leaderName}\n" +
            $"  LeaderBlueprint: {leaderBlueprint}\n" +
            $"  LeaderRank: {leaderRank}\n" +
            $"  InvalidCompositionReason: {invalidCompositionReasonText}\n" +
            $"  Scope: Initial enemy snapshot; classification and leader are immutable for this combat.";
        stringConstructionTicks += Stopwatch.GetTimestamp() - phaseStarted;

        phaseStarted = Stopwatch.GetTimestamp();
        Main.LogInfo(classificationDiagnostic);
        loggerCallTicks += Stopwatch.GetTimestamp() - phaseStarted;

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
        } catch (Exception exception) {
            FaultRuntime(nameof(HandlePartyCombatStateChanged), exception);
        }
    }

    private static void HandleCombatEnd() {
        try {
            Main.LogInfo("Combat ended.");
        } catch (Exception exception) {
            FaultRuntime($"{nameof(HandlePartyCombatStateChanged)}(false)", exception);
        } finally {
            ClearRuntimeState();
        }
    }

    private static void FaultRuntime(string callbackName, Exception exception) {
        DamageControl.ClearAllPolicies();
        UnitMarker.ClearAllMarkers();
        EncounterHud.Hide();
        bool hadSession = CurrentSession != null;
        CurrentSession = null;
        SessionActivated = false;
        ActivationCombatRound = 0;
        RuntimeFaulted = true;

        if (!RuntimeFaultLogged) {
            RuntimeFaultLogged = true;
            Main.LogError($"Encounter runtime faulted in {callbackName}: {exception}");
        }
        if (hadSession) {
            Main.LogInfo("Encounter runtime session cleared after a runtime fault.");
        }
    }

    private static void ClearRuntimeState() {
        DamageControl.ClearAllPolicies();
        UnitMarker.ClearAllMarkers();
        EncounterHud.Hide();
        bool hadSession = CurrentSession != null;
        CurrentSession = null;
        SessionActivated = false;
        ActivationCombatRound = 0;
        DuplicateStartWarningLogged = false;
        RuntimeFaulted = false;
        RuntimeFaultLogged = false;

        if (hadSession) {
            Main.LogInfo("Encounter runtime session cleared at combat end.");
        }
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
