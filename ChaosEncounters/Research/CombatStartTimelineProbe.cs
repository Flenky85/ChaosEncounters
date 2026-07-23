using System.Globalization;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Controllers.Combat;
using Kingmaker.Controllers.TurnBased;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.PubSubSystem.Core;
using Kingmaker.UnitLogic.Parts;
using UnityEngine;

namespace ChaosEncounters.Research;

internal sealed class CombatStartTimelineProbe :
    IPartyCombatHandler,
    IAnyUnitCombatHandler,
    ITurnBasedModeHandler,
    IRoundStartHandler,
    IPreparationTurnBeginHandler,
    IPreparationTurnEndHandler,
    ITurnStartHandler,
    IAreaHandler,
    IAreaActivationHandler,
    IAreaLoadingStagesHandler {
    private const string HarmonyId = "ChaosEncounters.Research.CombatStartTimeline";
    private static readonly CombatStartTimelineProbe Instance = new();
    private static readonly ReferenceComparer UnitReferenceComparer = new();
    private static readonly object Sync = new();

    private static bool Initialized;
    private static bool CaptureActive;
    private static bool CaptureComplete;
    private static bool FirstRealTurnStarted;
    private static int CaptureNumber;
    private static long Sequence;
    private static Dictionary<string, UnitState> PreviousSnapshot = new();

    internal static void Initialize() {
        if (Initialized) {
            return;
        }

        try {
            var harmony = new Harmony(HarmonyId);
            harmony.CreateClassProcessor(typeof(CombatTimelineUnitJoinPatch)).Patch();
            harmony.CreateClassProcessor(typeof(CombatTimelineInitiativeRollPatch)).Patch();
            harmony.CreateClassProcessor(typeof(CombatTimelineEnterTbPatch)).Patch();
            harmony.CreateClassProcessor(typeof(CombatTimelineAddUnitsPatch)).Patch();
            harmony.CreateClassProcessor(typeof(CombatTimelineBeginPreparationPatch)).Patch();
            harmony.CreateClassProcessor(typeof(CombatTimelineEndPreparationPatch)).Patch();
            harmony.CreateClassProcessor(typeof(CombatTimelineNextTurnPatch)).Patch();
            harmony.CreateClassProcessor(typeof(CombatTimelineTurnControllerStartPatch)).Patch();
            harmony.CreateClassProcessor(typeof(CombatTimelinePostLoadFixesPatch)).Patch();
            harmony.CreateClassProcessor(typeof(CombatTimelineCombatJoinControllerEnablePatch)).Patch();
            if (!EventBus.IsGloballySubscribed(Instance)) {
                EventBus.Subscribe(Instance);
            }
            Initialized = true;
            WriteRecord("PROBE_INITIALIZED", "callback entry", null);
        } catch (Exception exception) {
            WriteFailure("Initialize", exception);
        }
    }

    public void OnAreaBeginUnloading() =>
        Guard("OnAreaBeginUnloading", () => {
            WriteRecord("AREA_LOADING_START", "callback entry", null);
            if (CaptureActive) {
                TakeSnapshot("AREA_LOADING_START");
            }
        });

    public void OnAreaDidLoad() =>
        Guard("OnAreaDidLoad", () => WriteRecord("AREA_DID_LOAD", "callback entry", null));

    public void OnAreaActivated() =>
        Guard("OnAreaActivated", () => WriteRecord("AREA_ACTIVATED", "callback entry", null));

    public void OnAreaScenesLoaded() =>
        Guard("OnAreaScenesLoaded", () => WriteRecord("AREA_SCENES_LOADED", "callback entry", null));

    public void OnAreaLoadingComplete() =>
        Guard("OnAreaLoadingComplete", () => {
            WriteRecord("AREA_LOAD_COMPLETE", "callback entry", null);
            if (IsCombatStatePresent()) {
                EnsureCapture("AREA_LOAD_COMPLETE");
                TakeSnapshot("AREA_LOAD_COMPLETE");
            }
        });

    public void HandlePartyCombatStateChanged(bool inCombat) =>
        Guard("HandlePartyCombatStateChanged", () => {
            WriteRecord($"PARTY_COMBAT_{inCombat.ToString().ToUpperInvariant()}", "callback entry", null);
            if (inCombat) {
                EnsureCapture("PARTY_COMBAT_TRUE");
                TakeSnapshot("PARTY_COMBAT_TRUE");
            } else {
                if (CaptureActive) {
                    TakeSnapshot("PARTY_COMBAT_FALSE");
                }
                WriteRecord("CAPTURE_CLEANUP", "callback exit", null);
                ResetCapture();
            }
        });

    public void HandleUnitJoinCombat(BaseUnitEntity unit) =>
        Guard("HandleUnitJoinCombat", () => {
            if (CaptureComplete) {
                return;
            }
            EnsureCapture("UNIT_JOIN");
            WriteUnitRecord("JOIN_CALLBACK_ENTRY", "callback entry", unit);
            if (!CaptureComplete && IsEnemyOrPotentiallyHostile(unit)) {
                TakeSnapshot("AFTER_EACH_UNIT_JOIN");
            }
            WriteUnitRecord("JOIN_CALLBACK_EXIT", "callback exit", unit);
        });

    public void HandleUnitLeaveCombat(BaseUnitEntity unit) =>
        Guard("HandleUnitLeaveCombat", () => {
            if (CaptureComplete) {
                return;
            }
            WriteUnitRecord("LEAVE_CALLBACK_ENTRY", "callback entry", unit);
            WriteUnitRecord("LEAVE_CALLBACK_EXIT", "callback exit", unit);
        });

    public void HandleTurnBasedModeSwitched(bool isTurnBased) =>
        Guard("HandleTurnBasedModeSwitched", () => {
            if (CaptureComplete && isTurnBased) {
                return;
            }
            WriteRecord($"TURN_BASED_MODE_{(isTurnBased ? "BEGIN" : "END")}", "callback entry", null);
            if (isTurnBased) {
                EnsureCapture("TURN_BASED_MODE_BEGIN");
                TakeSnapshot("TURN_BASED_MODE_BEGIN");
            }
        });

    public void HandleRoundStart(bool isTurnBased) =>
        Guard("HandleRoundStart", () => {
            if (CaptureComplete) {
                return;
            }
            WriteRecord($"ROUND_START isTurnBased={isTurnBased}", "callback entry", null);
            if (isTurnBased) {
                EnsureCapture("ROUND_START");
                TakeSnapshot("ROUND_START");
            }
        });

    public void HandleBeginPreparationTurn(bool canDeploy) =>
        Guard("HandleBeginPreparationTurn", () => {
            if (CaptureComplete) {
                return;
            }
            EnsureCapture("PREPARATION_BEGIN");
            WriteRecord($"PREPARATION_BEGIN canDeploy={canDeploy}", "callback entry", null);
            TakeSnapshot("PREPARATION_BEGIN");
        });

    public void HandleEndPreparationTurn() =>
        Guard("HandleEndPreparationTurn", () => {
            if (CaptureComplete) {
                return;
            }
            WriteRecord("PREPARATION_END", "callback entry", null);
            TakeSnapshot("PREPARATION_END");
        });

    public void HandleUnitStartTurn(bool isTurnBased) =>
        Guard("HandleUnitStartTurn", () => {
            if (!isTurnBased || CaptureComplete) {
                return;
            }

            BaseUnitEntity unit = EventInvokerExtensions.MechanicEntity as BaseUnitEntity;
            if (Safe(() => Game.Instance.TurnController.IsPreparationTurn, false)) {
                WriteUnitRecord("PREPARATION_UNIT_TURN", "callback entry", unit);
                return;
            }

            EnsureCapture("FIRST_REAL_UNIT_TURN");
            FirstRealTurnStarted = true;
            WriteUnitRecord("FIRST_REAL_UNIT_TURN_ACTOR", "callback entry", unit);
            TakeSnapshot("FIRST_REAL_UNIT_TURN");
            CaptureComplete = true;
            WriteRecord("CAPTURE_COMPLETE", "final snapshot", null);
        });

    internal static void NativeBoundary(
        string point,
        string position,
        BaseUnitEntity unit = null,
        bool fullSnapshot = false) {
        Guard(point, () => {
            if (CaptureComplete) {
                return;
            }
            if (!CaptureActive && IsCombatStartBoundary(point)) {
                EnsureCapture(point);
            }
            if (!CaptureActive && !IsCombatStatePresent()) {
                return;
            }
            if (unit == null) {
                WriteRecord(point, position, null);
            } else {
                WriteUnitRecord(point, position, unit);
            }
            if (fullSnapshot && !CaptureComplete) {
                TakeSnapshot(point);
            }
        });
    }

    private static void EnsureCapture(string reason) {
        if (CaptureActive) {
            return;
        }

        CaptureActive = true;
        CaptureComplete = false;
        FirstRealTurnStarted = false;
        CaptureNumber++;
        PreviousSnapshot = new Dictionary<string, UnitState>();
        WriteRecord($"CAPTURE_START reason={reason}", "callback entry", null);
    }

    private static void ResetCapture() {
        CaptureActive = false;
        CaptureComplete = false;
        FirstRealTurnStarted = false;
        PreviousSnapshot.Clear();
    }

    private static void TakeSnapshot(string name) {
        if (!CaptureActive || CaptureComplete) {
            return;
        }

        Game game = Game.Instance;
        var awake = Collect(
            game?.State?.AllBaseAwakeUnitsForSure,
            out int awakeTotal,
            out int awakeNonNull,
            out int awakeDuplicates);
        var allBase = Collect(
            game?.State?.AllBaseUnits?.All,
            out int allBaseTotal,
            out int allBaseNonNull,
            out int allBaseDuplicates);
        var turnControllerUnits = CollectTurnControllerUnits(game?.TurnController);
        var initiative = new HashSet<BaseUnitEntity>(UnitReferenceComparer);
        foreach (BaseUnitEntity unit in turnControllerUnits) {
            if (Safe(() => !unit.Initiative.Empty, false)) {
                initiative.Add(unit);
            }
        }

        var union = new HashSet<BaseUnitEntity>(UnitReferenceComparer);
        union.UnionWith(awake);
        union.UnionWith(allBase);
        union.UnionWith(turnControllerUnits);

        WriteRecord($"SNAPSHOT_BEGIN name={name}", "final snapshot", null);
        WriteCollectionSummary(
            name,
            "AllBaseAwakeUnitsForSure",
            awake,
            awakeTotal,
            awakeNonNull,
            awakeDuplicates);
        WriteCollectionSummary(
            name,
            "AllBaseUnits.All",
            allBase,
            allBaseTotal,
            allBaseNonNull,
            allBaseDuplicates);
        WriteCollectionSummary(
            name,
            "TurnController.AllUnits(BaseUnitEntity)",
            turnControllerUnits,
            turnControllerUnits.Count,
            turnControllerUnits.Count,
            0);
        WriteCollectionSummary(
            name,
            "Initiative(!Initiative.Empty)",
            initiative,
            initiative.Count,
            initiative.Count,
            0);

        int enemiesInitiativeMissingAwake = CountWhere(
            initiative,
            u => IsLivingEnemy(u) && !awake.Contains(u));
        int enemiesAwakeMissingInitiative = CountWhere(
            awake,
            u => IsLivingEnemy(u) && !initiative.Contains(u));
        int enemiesAllBaseMissingAwake = CountWhere(
            allBase,
            u => IsLivingEnemy(u) && !awake.Contains(u));
        WriteRecord(
            $"INITIATIVE_COMPARISON snapshot={name} totalInitiativeEntries={initiative.Count} " +
            $"livingEnemiesInInitiative={CountWhere(initiative, IsLivingEnemy)} " +
            $"enemiesInInitiativeMissingAwake={enemiesInitiativeMissingAwake} " +
            $"enemiesInAwakeMissingInitiative={enemiesAwakeMissingInitiative} " +
            $"enemiesInAllBaseMissingAwake={enemiesAllBaseMissingAwake}",
            "final snapshot",
            null);

        var current = new Dictionary<string, UnitState>();
        int ordinal = 0;
        foreach (BaseUnitEntity unit in union) {
            ordinal++;
            UnitState state = CaptureUnitState(unit, awake, allBase, turnControllerUnits, initiative);
            current[state.Identity] = state;
            WriteUnitState($"UNIT snapshot={name} ordinal={ordinal}", "final snapshot", state);
        }

        WriteSnapshotDelta(name, PreviousSnapshot, current);
        PreviousSnapshot = current;
        WriteRecord($"SNAPSHOT_END name={name} units={union.Count}", "final snapshot", null);
    }

    private static HashSet<BaseUnitEntity> Collect(IEnumerable<BaseUnitEntity> source) =>
        Collect(source, out _, out _, out _);

    private static HashSet<BaseUnitEntity> Collect(
        IEnumerable<BaseUnitEntity> source,
        out int totalEntries,
        out int nonNullEntries,
        out int duplicateReferences) {
        var result = new HashSet<BaseUnitEntity>(UnitReferenceComparer);
        totalEntries = 0;
        nonNullEntries = 0;
        duplicateReferences = 0;
        if (source == null) {
            return result;
        }
        foreach (BaseUnitEntity unit in source) {
            totalEntries++;
            if (unit != null) {
                nonNullEntries++;
                if (!result.Add(unit)) {
                    duplicateReferences++;
                }
            }
        }
        return result;
    }

    private static HashSet<BaseUnitEntity> CollectTurnControllerUnits(TurnController controller) {
        var result = new HashSet<BaseUnitEntity>(UnitReferenceComparer);
        if (controller == null) {
            return result;
        }
        foreach (MechanicEntity entity in controller.AllUnits) {
            if (entity is BaseUnitEntity unit) {
                result.Add(unit);
            }
        }
        return result;
    }

    private static void WriteCollectionSummary(
        string snapshot,
        string collection,
        HashSet<BaseUnitEntity> units,
        int totalEntries,
        int nonNullEntries,
        int duplicateReferences) {
        int inGame = 0;
        int inCombat = 0;
        int enemies = 0;
        int livingEnemies = 0;
        int playerCharacters = 0;
        int neutrals = 0;
        int disposed = 0;
        int starships = 0;
        foreach (BaseUnitEntity unit in units) {
            if (Safe(() => unit.IsInGame, false)) inGame++;
            if (Safe(() => unit.IsInCombat, false)) inCombat++;
            if (Safe(() => unit.IsPlayerEnemy, false)) enemies++;
            if (IsLivingEnemy(unit)) livingEnemies++;
            if (Safe(() => unit.IsInPlayerParty, false)) playerCharacters++;
            if (Safe(() => unit.IsNeutral, false)) neutrals++;
            if (Safe(() => unit.IsDisposed, false)) disposed++;
            if (unit is StarshipEntity) starships++;
        }
        WriteRecord(
            $"COLLECTION_SUMMARY snapshot={snapshot} collection={collection} totalEntries={totalEntries} " +
            $"nonNullEntries={nonNullEntries} baseUnitEntries={nonNullEntries} terrestrialUnits={units.Count - starships} " +
            $"unitsInGame={inGame} unitsInCombat={inCombat} playerEnemies={enemies} " +
            $"livingPlayerEnemies={livingEnemies} playerCharacters={playerCharacters} neutralUnits={neutrals} " +
            $"disposedUnits={disposed} starships={starships} uniqueReferenceCount={units.Count} duplicateReferenceCount={duplicateReferences}",
            "final snapshot",
            null);
    }

    private static UnitState CaptureUnitState(
        BaseUnitEntity unit,
        HashSet<BaseUnitEntity> awake,
        HashSet<BaseUnitEntity> allBase,
        HashSet<BaseUnitEntity> turnUnits,
        HashSet<BaseUnitEntity> initiative) {
        int referenceHash = RuntimeHelpers.GetHashCode(unit);
        string uniqueId = Safe(() => unit.UniqueId, "Unavailable");
        string identity = !string.IsNullOrWhiteSpace(uniqueId) && uniqueId != "Unavailable"
            ? $"id:{uniqueId}"
            : $"ref:{referenceHash}";
        PartLifeState life = Safe(() => unit.LifeState, null);
        PartHealth health = Safe(() => unit.GetHealthOptional(), null);
        bool inAwake = awake.Contains(unit);
        bool inAllBase = allBase.Contains(unit);
        bool inTurnUnits = turnUnits.Contains(unit);
        bool inInitiative = initiative.Contains(unit);

        return new UnitState {
            Identity = identity,
            ReferenceHash = referenceHash,
            UniqueId = uniqueId,
            CharacterName = Safe(() => unit.CharacterName, "Unavailable"),
            BlueprintName = Safe(() => unit.Blueprint?.name, "Unavailable"),
            BlueprintGuid = Safe(() => unit.Blueprint?.AssetGuid.ToString(), "Unavailable"),
            Difficulty = Safe(() => unit.Blueprint?.DifficultyType.ToString(), "Unavailable"),
            RuntimeType = Safe(() => unit.GetType().FullName, "Unavailable"),
            Faction = Safe(() => unit.Faction?.Blueprint?.name, "Unavailable"),
            Position = Safe(() => unit.Position.ToString("F3"), "Unavailable"),
            NearestPartyDistance = NearestPartyDistance(unit),
            IsInGame = Safe(() => unit.IsInGame, false),
            IsInCombat = Safe(() => unit.IsInCombat, false),
            IsPlayerEnemy = Safe(() => unit.IsPlayerEnemy, false),
            IsPlayerFaction = Safe(() => unit.IsPlayerFaction, false),
            IsDisposed = Safe(() => unit.IsDisposed, false),
            IsStarship = unit is StarshipEntity,
            LifeStateAvailable = life != null,
            IsConscious = life != null && Safe(() => life.IsConscious, false),
            IsDead = life != null && Safe(() => life.IsDead, false),
            IsFinallyDead = life != null && Safe(() => life.IsFinallyDead, false),
            MarkedForDeath = life != null && Safe(() => life.MarkedForDeath, false),
            ScriptedKill = life != null && Safe(() => life.ScriptedKill, false),
            HealthAvailable = health != null,
            HitPoints = health == null ? "Unavailable" : Safe(() => health.HitPointsLeft.ToString(CultureInfo.InvariantCulture), "Unavailable"),
            MaxHitPoints = health == null ? "Unavailable" : Safe(() => health.MaxHitPoints.ToString(CultureInfo.InvariantCulture), "Unavailable"),
            PresentInAwake = inAwake,
            PresentInAllBase = inAllBase,
            PresentInTurnController = inTurnUnits,
            PresentInInitiative = inInitiative,
            PreparationParticipant = Safe(() => Game.Instance.TurnController.IsPreparationTurn && unit.IsInCombat, false),
            CurrentTurn = Safe(() => ReferenceEquals(Game.Instance.TurnController.CurrentUnit, unit), false),
            EligibleForChaosEncounters = inAwake &&
                Safe(() => unit.IsInGame && unit.IsInCombat && unit.IsPlayerEnemy, false) &&
                unit is not StarshipEntity
        };
    }

    private static void WriteUnitRecord(string point, string position, BaseUnitEntity unit) {
        if (unit == null) {
            WriteRecord($"{point} unit=Unavailable", position, null);
            return;
        }
        var empty = new HashSet<BaseUnitEntity>(UnitReferenceComparer);
        Game game = Safe(() => Game.Instance, null);
        var awake = Collect(Safe(() => game.State.AllBaseAwakeUnitsForSure, null));
        var allBase = Collect(Safe(() => game.State.AllBaseUnits.All, null));
        var turn = CollectTurnControllerUnits(Safe(() => game.TurnController, null));
        foreach (BaseUnitEntity candidate in turn) {
            if (Safe(() => !candidate.Initiative.Empty, false)) {
                empty.Add(candidate);
            }
        }
        WriteUnitState(point, position, CaptureUnitState(unit, awake, allBase, turn, empty));
    }

    private static void WriteUnitState(string point, string position, UnitState s) {
        WriteRecord(
            $"{point} identity={Escape(s.Identity)} referenceHash={s.ReferenceHash} uniqueId={Escape(s.UniqueId)} " +
            $"characterName={Escape(s.CharacterName)} blueprintName={Escape(s.BlueprintName)} blueprintGuid={Escape(s.BlueprintGuid)} " +
            $"difficulty={Escape(s.Difficulty)} runtimeType={Escape(s.RuntimeType)} faction={Escape(s.Faction)} " +
            $"position={Escape(s.Position)} nearestPartyDistance={Escape(s.NearestPartyDistance)} " +
            $"isInGame={s.IsInGame} isInCombat={s.IsInCombat} isPlayerEnemy={s.IsPlayerEnemy} " +
            $"isPlayerFaction={s.IsPlayerFaction} isDisposed={s.IsDisposed} isStarship={s.IsStarship} " +
            $"lifeStateAvailable={s.LifeStateAvailable} conscious={s.IsConscious} dead={s.IsDead} finallyDead={s.IsFinallyDead} " +
            $"markedForDeath={s.MarkedForDeath} scriptedKill={s.ScriptedKill} healthAvailable={s.HealthAvailable} " +
            $"hitPoints={s.HitPoints} maxHitPoints={s.MaxHitPoints} presentInAwake={s.PresentInAwake} " +
            $"presentInAllBase={s.PresentInAllBase} presentInTurnController={s.PresentInTurnController} " +
            $"presentInInitiative={s.PresentInInitiative} preparationParticipant={s.PreparationParticipant} " +
            $"currentTurn={s.CurrentTurn} eligibleForChaosEncounters={s.EligibleForChaosEncounters}",
            position,
            null);
    }

    private static void WriteSnapshotDelta(
        string name,
        Dictionary<string, UnitState> previous,
        Dictionary<string, UnitState> current) {
        foreach (var pair in current) {
            if (!previous.TryGetValue(pair.Key, out UnitState old)) {
                WriteRecord($"NewSincePreviousSnapshot snapshot={name} identity={Escape(pair.Key)}", "final snapshot", null);
                continue;
            }
            string changes = ChangedFlags(old, pair.Value);
            if (changes.Length != 0) {
                WriteRecord(
                    $"ChangedStateSincePreviousSnapshot snapshot={name} identity={Escape(pair.Key)} changes={Escape(changes)}",
                    "final snapshot",
                    null);
            }
        }
        foreach (var pair in previous) {
            if (!current.ContainsKey(pair.Key)) {
                WriteRecord($"MissingSincePreviousSnapshot snapshot={name} identity={Escape(pair.Key)}", "final snapshot", null);
            }
        }
    }

    private static string ChangedFlags(UnitState a, UnitState b) {
        var changes = new List<string>();
        Add(nameof(UnitState.IsInGame), a.IsInGame, b.IsInGame);
        Add(nameof(UnitState.IsInCombat), a.IsInCombat, b.IsInCombat);
        Add(nameof(UnitState.IsPlayerEnemy), a.IsPlayerEnemy, b.IsPlayerEnemy);
        Add(nameof(UnitState.IsPlayerFaction), a.IsPlayerFaction, b.IsPlayerFaction);
        Add(nameof(UnitState.IsDisposed), a.IsDisposed, b.IsDisposed);
        Add(nameof(UnitState.IsConscious), a.IsConscious, b.IsConscious);
        Add(nameof(UnitState.IsDead), a.IsDead, b.IsDead);
        Add(nameof(UnitState.IsFinallyDead), a.IsFinallyDead, b.IsFinallyDead);
        Add(nameof(UnitState.MarkedForDeath), a.MarkedForDeath, b.MarkedForDeath);
        Add(nameof(UnitState.ScriptedKill), a.ScriptedKill, b.ScriptedKill);
        Add(nameof(UnitState.PresentInAwake), a.PresentInAwake, b.PresentInAwake);
        Add(nameof(UnitState.PresentInAllBase), a.PresentInAllBase, b.PresentInAllBase);
        Add(nameof(UnitState.PresentInTurnController), a.PresentInTurnController, b.PresentInTurnController);
        Add(nameof(UnitState.PresentInInitiative), a.PresentInInitiative, b.PresentInInitiative);
        Add(nameof(UnitState.PreparationParticipant), a.PreparationParticipant, b.PreparationParticipant);
        Add(nameof(UnitState.CurrentTurn), a.CurrentTurn, b.CurrentTurn);
        Add(nameof(UnitState.EligibleForChaosEncounters), a.EligibleForChaosEncounters, b.EligibleForChaosEncounters);
        return string.Join("; ", changes);

        void Add(string field, bool before, bool after) {
            if (before != after) changes.Add($"{field}: {before} -> {after}");
        }
    }

    private static string NearestPartyDistance(BaseUnitEntity unit) {
        try {
            float? nearest = null;
            foreach (BaseUnitEntity party in Game.Instance.Player.PartyAndPets) {
                if (party == null) continue;
                float distance = Vector3.Distance(unit.Position, party.Position);
                if (!nearest.HasValue || distance < nearest.Value) nearest = distance;
            }
            return nearest?.ToString("F3", CultureInfo.InvariantCulture) ?? "Unavailable";
        } catch {
            return "Unavailable";
        }
    }

    private static bool IsLivingEnemy(BaseUnitEntity unit) =>
        Safe(() => unit.IsPlayerEnemy && unit.LifeState != null && !unit.LifeState.IsDead, false);

    private static bool IsEnemyOrPotentiallyHostile(BaseUnitEntity unit) =>
        unit != null && Safe(() => unit.IsPlayerEnemy || !unit.IsPlayerFaction || unit.IsNeutral, true);

    private static int CountWhere(IEnumerable<BaseUnitEntity> units, Func<BaseUnitEntity, bool> predicate) {
        int count = 0;
        foreach (BaseUnitEntity unit in units) if (predicate(unit)) count++;
        return count;
    }

    private static bool IsCombatStatePresent() =>
        Safe(() => Game.Instance.Player.IsInCombat || Game.Instance.TurnController.TbActive, false);

    private static bool IsCombatStartBoundary(string point) =>
        point.IndexOf("JOIN", StringComparison.Ordinal) >= 0 ||
        point.IndexOf("ENTER_TB", StringComparison.Ordinal) >= 0;

    private static void WriteRecord(string point, string position, BaseUnitEntity _) {
        lock (Sync) {
            long sequence = ++Sequence;
            DateTime now = DateTime.Now;
            string frame = Safe(() => Time.frameCount.ToString(CultureInfo.InvariantCulture), "Unavailable");
            string mode = Safe(() => Game.Instance.CurrentMode.ToString(), "Unavailable");
            string playerCombat = Safe(() => Game.Instance.Player.IsInCombat.ToString(), "Unavailable");
            string tbActive = Safe(() => Game.Instance.TurnController.TbActive.ToString(), "Unavailable");
            string round = Safe(() => Game.Instance.TurnController.CombatRound.ToString(CultureInfo.InvariantCulture), "Unavailable");
            string preparation = Safe(() => Game.Instance.TurnController.IsPreparationTurn.ToString(), "Unavailable");
            CombatStartTimelineLogger.Write(
                $"seq={sequence} time={now:yyyy-MM-dd HH:mm:ss.fff} frame={frame} capture={CaptureNumber} " +
                $"point={Escape(point)} position={Escape(position)} gameMode={mode} playerIsInCombat={playerCombat} " +
                $"tbActive={tbActive} combatRound={round} preparationActive={preparation} " +
                $"firstRealUnitTurnStarted={FirstRealTurnStarted}");
        }
    }

    private static void Guard(string source, Action action) {
        try {
            action();
        } catch (Exception exception) {
            WriteFailure(source, exception);
        }
    }

    private static void WriteFailure(string source, Exception exception) {
        try {
            CombatStartTimelineLogger.Write(
                $"seq={++Sequence} time={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} point=PROBE_EXCEPTION " +
                $"source={Escape(source)} exception={Escape(exception.ToString())}");
        } catch {
            // Research failures must never propagate into the game or production runtime.
        }
    }

    private static T Safe<T>(Func<T> read, T unavailable) {
        try {
            T value = read();
            return value == null ? unavailable : value;
        } catch {
            return unavailable;
        }
    }

    private static string Escape(string value) =>
        value == null ? "Unavailable" : $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n")}\"";

    private sealed class ReferenceComparer : IEqualityComparer<BaseUnitEntity> {
        public bool Equals(BaseUnitEntity x, BaseUnitEntity y) => ReferenceEquals(x, y);
        public int GetHashCode(BaseUnitEntity obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private sealed class UnitState {
        internal string Identity;
        internal int ReferenceHash;
        internal string UniqueId;
        internal string CharacterName;
        internal string BlueprintName;
        internal string BlueprintGuid;
        internal string Difficulty;
        internal string RuntimeType;
        internal string Faction;
        internal string Position;
        internal string NearestPartyDistance;
        internal bool IsInGame;
        internal bool IsInCombat;
        internal bool IsPlayerEnemy;
        internal bool IsPlayerFaction;
        internal bool IsDisposed;
        internal bool IsStarship;
        internal bool LifeStateAvailable;
        internal bool IsConscious;
        internal bool IsDead;
        internal bool IsFinallyDead;
        internal bool MarkedForDeath;
        internal bool ScriptedKill;
        internal bool HealthAvailable;
        internal string HitPoints;
        internal string MaxHitPoints;
        internal bool PresentInAwake;
        internal bool PresentInAllBase;
        internal bool PresentInTurnController;
        internal bool PresentInInitiative;
        internal bool PreparationParticipant;
        internal bool CurrentTurn;
        internal bool EligibleForChaosEncounters;
    }
}

[HarmonyPatch(typeof(PartUnitCombatState), nameof(PartUnitCombatState.JoinCombat), typeof(bool))]
internal static class CombatTimelineUnitJoinPatch {
    [HarmonyPrefix]
    private static void Prefix(PartUnitCombatState __instance) =>
        CombatStartTimelineProbe.NativeBoundary("UNIT_JOIN_PREFIX", "prefix", __instance?.Owner);

    [HarmonyPostfix]
    private static void Postfix(PartUnitCombatState __instance) =>
        CombatStartTimelineProbe.NativeBoundary("UNIT_JOIN_POSTFIX", "postfix", __instance?.Owner, fullSnapshot: true);
}

[HarmonyPatch(typeof(InitiativeHelper), nameof(InitiativeHelper.Roll), typeof(IEnumerable<MechanicEntity>), typeof(bool))]
internal static class CombatTimelineInitiativeRollPatch {
    [HarmonyPrefix]
    private static void Prefix() =>
        CombatStartTimelineProbe.NativeBoundary("INITIATIVE_ADD_PREFIX", "prefix", fullSnapshot: true);

    [HarmonyPostfix]
    private static void Postfix() =>
        CombatStartTimelineProbe.NativeBoundary("INITIATIVE_ADD_POSTFIX", "postfix", fullSnapshot: true);
}

[HarmonyPatch(typeof(TurnController), "EnterTb")]
internal static class CombatTimelineEnterTbPatch {
    [HarmonyPrefix]
    private static void Prefix() =>
        CombatStartTimelineProbe.NativeBoundary("ENTER_TB", "prefix", fullSnapshot: true);

    [HarmonyPostfix]
    private static void Postfix() =>
        CombatStartTimelineProbe.NativeBoundary("ENTER_TB", "postfix", fullSnapshot: true);
}

[HarmonyPatch(typeof(TurnController), "AddUnitsToCombat")]
internal static class CombatTimelineAddUnitsPatch {
    [HarmonyPrefix]
    private static void Prefix() =>
        CombatStartTimelineProbe.NativeBoundary("BEFORE_INITIATIVE_INITIALIZATION", "prefix", fullSnapshot: true);

    [HarmonyPostfix]
    private static void Postfix() =>
        CombatStartTimelineProbe.NativeBoundary("AFTER_INITIATIVE_INITIALIZATION", "postfix", fullSnapshot: true);
}

[HarmonyPatch(typeof(TurnController), nameof(TurnController.BeginPreparationTurn), typeof(bool))]
internal static class CombatTimelineBeginPreparationPatch {
    [HarmonyPrefix]
    private static void Prefix() =>
        CombatStartTimelineProbe.NativeBoundary("BEGIN_PREPARATION_TURN", "prefix", fullSnapshot: true);

    [HarmonyPostfix]
    private static void Postfix() =>
        CombatStartTimelineProbe.NativeBoundary("BEGIN_PREPARATION_TURN", "postfix", fullSnapshot: true);
}

[HarmonyPatch(typeof(TurnController), nameof(TurnController.ForceEndPreparationTurn))]
internal static class CombatTimelineEndPreparationPatch {
    [HarmonyPrefix]
    private static void Prefix() =>
        CombatStartTimelineProbe.NativeBoundary("END_PREPARATION_TURN", "prefix", fullSnapshot: true);

    [HarmonyPostfix]
    private static void Postfix() =>
        CombatStartTimelineProbe.NativeBoundary("END_PREPARATION_TURN", "postfix", fullSnapshot: true);
}

[HarmonyPatch(typeof(TurnController), "NextTurnTB")]
internal static class CombatTimelineNextTurnPatch {
    [HarmonyPrefix]
    private static void Prefix() =>
        CombatStartTimelineProbe.NativeBoundary("FIRST_NORMAL_TURN_TRANSITION", "prefix", fullSnapshot: true);

    [HarmonyPostfix]
    private static void Postfix() =>
        CombatStartTimelineProbe.NativeBoundary("FIRST_NORMAL_TURN_TRANSITION", "postfix");
}

[HarmonyPatch(typeof(TurnController), nameof(TurnController.OnStart))]
internal static class CombatTimelineTurnControllerStartPatch {
    [HarmonyPrefix]
    private static void Prefix() =>
        CombatStartTimelineProbe.NativeBoundary("LOADED_STATE_RESTORATION_TURN_CONTROLLER_START", "prefix", fullSnapshot: true);

    [HarmonyPostfix]
    private static void Postfix() =>
        CombatStartTimelineProbe.NativeBoundary("LOADED_STATE_RESTORATION_TURN_CONTROLLER_START", "postfix", fullSnapshot: true);
}

[HarmonyPatch(typeof(TurnController), "ApplyPostLoadFixes")]
internal static class CombatTimelinePostLoadFixesPatch {
    [HarmonyPrefix]
    private static void Prefix() =>
        CombatStartTimelineProbe.NativeBoundary("LOADED_STATE_RESTORATION_POST_LOAD_FIXES", "prefix", fullSnapshot: true);

    [HarmonyPostfix]
    private static void Postfix() =>
        CombatStartTimelineProbe.NativeBoundary("LOADED_STATE_RESTORATION_POST_LOAD_FIXES", "postfix", fullSnapshot: true);
}

[HarmonyPatch(typeof(UnitCombatJoinController), nameof(UnitCombatJoinController.OnEnable))]
internal static class CombatTimelineCombatJoinControllerEnablePatch {
    [HarmonyPrefix]
    private static void Prefix() =>
        CombatStartTimelineProbe.NativeBoundary("LOADED_STATE_PLAYER_COMBAT_RECALCULATION", "prefix", fullSnapshot: true);

    [HarmonyPostfix]
    private static void Postfix() =>
        CombatStartTimelineProbe.NativeBoundary("LOADED_STATE_PLAYER_COMBAT_RECALCULATION", "postfix", fullSnapshot: true);
}
