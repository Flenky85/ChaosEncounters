using System;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Controllers.TurnBased;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.GameModes;
using Kingmaker.PubSubSystem.Core;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Parts;

namespace ChaosEncounters.Combat;

internal sealed class RoundEndHealingPrototype : IRoundEndHandler {
    private const string HarmonyId = "ChaosEncounters.RoundEndHealingPrototype";
    internal static readonly RoundEndHealingPrototype Instance = new();
    private static bool Initialized;
    private static bool RuntimeEnabled = true;
    private static RuleCalculateHeal ActiveCalculation;

    internal static void Initialize() {
        if (Initialized) {
            return;
        }

        var harmony = new Harmony(HarmonyId);
        harmony.CreateClassProcessor(typeof(RuleCalculateHealOnTriggerPatch)).Patch();
        Initialized = true;
    }

    public void HandleRoundEnd(bool isTurnBased, bool isFirst) {
        if (!RuntimeEnabled || !isTurnBased || isFirst) {
            return;
        }

        Game game = Game.Instance;
        if (game?.TurnController?.TbActive != true ||
            !game.TurnController.TurnBasedModeActive ||
            game.Player?.IsInCombat != true ||
            game.CurrentMode == GameModeType.SpaceCombat ||
            game.CurrentMode == GameModeType.StarSystem) {
            return;
        }

        try {
            ProcessRound(game);
        } catch (Exception exception) {
            Disable($"Round-end healing infrastructure failed: {exception}");
        }
    }

    private static void ProcessRound(Game game) {
        int combatRound = game.TurnController.CombatRound;
        int examinedEnemies = 0;
        int healedEnemies = 0;
        int skippedFullHealth = 0;
        int skippedInvalid = 0;
        long totalRequestedHealing = 0;
        long totalAppliedHealing = 0;

        foreach (BaseUnitEntity unit in game.State.AllBaseAwakeUnitsForSure) {
            if (unit is StarshipEntity ||
                unit.IsInPlayerParty ||
                !unit.Faction.IsPlayerEnemy) {
                continue;
            }

            examinedEnemies++;

            PartLifeState lifeState = unit.LifeState;
            if (!unit.IsInGame ||
                unit.IsDisposed ||
                !unit.IsInCombat ||
                lifeState == null ||
                !lifeState.IsConscious ||
                lifeState.IsDead ||
                lifeState.IsFinallyDead ||
                lifeState.MarkedForDeath ||
                lifeState.ScriptedKill) {
                skippedInvalid++;
                continue;
            }

            PartHealth health = unit.GetHealthOptional();
            PartDestructionStagesManager destructionStages =
                unit.GetDestructionStagesManagerOptional();
            if (health == null ||
                health.MaxHitPoints <= 0 ||
                destructionStages?.Stage == DestructionStage.Destroyed) {
                skippedInvalid++;
                continue;
            }

            int maximumHitPoints = health.MaxHitPoints;
            int hitPointsBefore = health.HitPointsLeft;
            int missingHitPoints = maximumHitPoints - hitPointsBefore;
            if (missingHitPoints <= 0) {
                skippedFullHealth++;
                continue;
            }

            int halfMaximumHitPoints = maximumHitPoints / 2;
            int requestedHealing = halfMaximumHitPoints < missingHitPoints
                ? halfMaximumHitPoints
                : missingHitPoints;
            if (requestedHealing <= 0) {
                skippedInvalid++;
                continue;
            }

            totalRequestedHealing += requestedHealing;
            if (!TryHealUnit(
                    unit,
                    health,
                    combatRound,
                    maximumHitPoints,
                    hitPointsBefore,
                    missingHitPoints,
                    halfMaximumHitPoints,
                    requestedHealing,
                    out int appliedHealing)) {
                break;
            }

            healedEnemies++;
            totalAppliedHealing += appliedHealing;
        }

        Main.LogInfo(
            $"Round-end healing summary:\n" +
            $"  CombatRound: {combatRound}\n" +
            $"  ExaminedEnemies: {examinedEnemies}\n" +
            $"  HealedEnemies: {healedEnemies}\n" +
            $"  SkippedFullHealth: {skippedFullHealth}\n" +
            $"  SkippedInvalid: {skippedInvalid}\n" +
            $"  TotalRequestedHealing: {totalRequestedHealing}\n" +
            $"  TotalAppliedHealing: {totalAppliedHealing}");
    }

    private static bool TryHealUnit(
        BaseUnitEntity unit,
        PartHealth health,
        int combatRound,
        int maximumHitPoints,
        int hitPointsBefore,
        int missingHitPoints,
        int halfMaximumHitPoints,
        int requestedHealing,
        out int appliedHealing) {
        appliedHealing = 0;
        string characterName = unit.CharacterName;
        string blueprintName = unit.Blueprint.name;

        if (ActiveCalculation != null) {
            Disable(
                $"Round-end healing found an active calculation before processing " +
                $"Name={characterName}, Blueprint={blueprintName}.");
            return false;
        }

        var rule = new RuleHealDamage(
            unit,
            unit,
            DiceFormula.Zero,
            requestedHealing);
        RuleCalculateHeal calculation = rule.CalculateHealRule;
        Exception failure = null;
        bool referenceRemainedConsistent;

        ActiveCalculation = calculation;
        try {
            Rulebook.Trigger(rule);
        } catch (Exception exception) {
            failure = exception;
        } finally {
            referenceRemainedConsistent = ReferenceEquals(ActiveCalculation, calculation);
            ActiveCalculation = null;
        }

        if (!referenceRemainedConsistent) {
            Disable(
                $"Round-end healing calculation reference changed unexpectedly for " +
                $"Name={characterName}, Blueprint={blueprintName}.");
            return false;
        }

        if (failure != null) {
            Disable(
                $"Round-end healing failed for Name={characterName}, " +
                $"Blueprint={blueprintName}: {failure}");
            return false;
        }

        int hitPointsAfter = health.HitPointsLeft;
        appliedHealing = hitPointsAfter - hitPointsBefore;
        int ruleReportedHealing = rule.Value;

        Main.LogInfo(
            $"Round-end enemy healing:\n" +
            $"  Name: {characterName}\n" +
            $"  Blueprint: {blueprintName}\n" +
            $"  CombatRound: {combatRound}\n" +
            $"  MaximumHitPoints: {maximumHitPoints}\n" +
            $"  HitPointsBefore: {hitPointsBefore}\n" +
            $"  MissingHitPoints: {missingHitPoints}\n" +
            $"  HalfMaximumHitPoints: {halfMaximumHitPoints}\n" +
            $"  RequestedHealing: {requestedHealing}\n" +
            $"  RuleReportedHealing: {ruleReportedHealing}\n" +
            $"  HitPointsAfter: {hitPointsAfter}\n" +
            $"  AppliedHealing: {appliedHealing}");

        if (appliedHealing != requestedHealing) {
            Main.LogWarning(
                $"Round-end healing mismatch: " +
                $"Name={characterName}, Blueprint={blueprintName}, " +
                $"CombatRound={combatRound}, MaximumHitPoints={maximumHitPoints}, " +
                $"HitPointsBefore={hitPointsBefore}, MissingHitPoints={missingHitPoints}, " +
                $"HalfMaximumHitPoints={halfMaximumHitPoints}, " +
                $"RequestedHealing={requestedHealing}, " +
                $"RuleReportedHealing={ruleReportedHealing}, " +
                $"HitPointsAfter={hitPointsAfter}, AppliedHealing={appliedHealing}.");
        }

        return true;
    }

    private static void Disable(string reason) {
        ActiveCalculation = null;
        RuntimeEnabled = false;
        Main.LogError($"Round-end healing prototype was disabled: {reason}");
    }

    [HarmonyPatch(
        typeof(RuleCalculateHeal),
        nameof(RuleCalculateHeal.OnTrigger),
        typeof(RulebookEventContext))]
    private static class RuleCalculateHealOnTriggerPatch {
        [HarmonyPrefix]
        private static void Prefix(RuleCalculateHeal __instance) {
            RuleCalculateHeal activeCalculation = ActiveCalculation;
            if (!ReferenceEquals(__instance, activeCalculation)) {
                return;
            }

            __instance.FlatBonus = 0;
            __instance.PercentBonus = 0;
            __instance.Nullify = false;
            __instance.UICriticalBonusRoll = null;
            __instance.UICriticalBonusChance = null;
            __instance.UIPercentCriticalBonus = 0;
            __instance.UIPercentCriticalBonuses.Clear();
        }
    }
}
