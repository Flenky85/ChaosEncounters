using System;
using HarmonyLib;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.GameModes;
using Kingmaker.PubSubSystem.Core;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Parts;

namespace ChaosEncounters.Combat;

internal static class DamageControlRuntimeValidation {
    private const string HarmonyId =
        "ChaosEncounters.DamageControlRuntimeValidation";
    private const int DiagnosticEventCap = 40;

    private static BaseUnitEntity ImmunityTarget;
    private static BaseUnitEntity PreventDeathTarget;
    private static bool ValidationActive;
    private static int DiagnosticEventCount;

    internal static void Initialize() {
        new Harmony(HarmonyId)
            .CreateClassProcessor(typeof(RuleDealDamageOnTriggerPatch))
            .Patch();
    }

    internal static void Activate(
        Game game,
        EncounterSession session,
        int combatRound) {
        if (ValidationActive ||
            game == null ||
            session == null ||
            game.CurrentMode == GameModeType.SpaceCombat ||
            game.CurrentMode == GameModeType.StarSystem) {
            return;
        }

        BaseUnitEntity immunityCandidate =
            game.Player?.MainCharacterEntity;
        BaseUnitEntity preventDeathCandidate = null;

        if (IsValidImmunityTarget(immunityCandidate)) {
            DamageControl.SetPolicy(
                immunityCandidate,
                DamagePolicy.Immunity);
            if (DamageControl.HasPolicyForValidation(
                    immunityCandidate,
                    DamagePolicy.Immunity)) {
                ImmunityTarget = immunityCandidate;
            }
        }

        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity enemy = session.InitialEnemies[index];
            if (!IsValidPreventDeathTarget(enemy, ImmunityTarget)) {
                continue;
            }

            DamageControl.SetPolicy(enemy, DamagePolicy.PreventDeath);
            if (DamageControl.HasPolicyForValidation(
                    enemy,
                    DamagePolicy.PreventDeath)) {
                preventDeathCandidate = enemy;
            }
            break;
        }

        PreventDeathTarget = preventDeathCandidate;
        ValidationActive = true;
        DiagnosticEventCount = 0;

        int confirmedRegistrations = 0;
        if (ImmunityTarget != null) {
            confirmedRegistrations++;
        }
        if (PreventDeathTarget != null) {
            confirmedRegistrations++;
        }

        Main.LogInfo(
            $"Damage-control runtime validation activated:\n" +
            $"  CombatRound: {combatRound}\n" +
            $"  ImmunityTargetName: " +
            $"{ImmunityTarget?.CharacterName ?? "None"}\n" +
            $"  ImmunityTargetBlueprint: " +
            $"{ImmunityTarget?.Blueprint?.name ?? "None"}\n" +
            $"  PreventDeathTargetName: " +
            $"{PreventDeathTarget?.CharacterName ?? "None"}\n" +
            $"  PreventDeathTargetBlueprint: " +
            $"{PreventDeathTarget?.Blueprint?.name ?? "None"}\n" +
            $"  ConfirmedRegistrations: {confirmedRegistrations}");

        if (PreventDeathTarget == null) {
            Main.LogWarning(
                "Damage-control runtime validation found no valid " +
                "PreventDeath target in the initial enemy snapshot.");
        }
    }

    internal static void RecordRollPolicy(
        BaseUnitEntity target,
        DamagePolicy policy,
        int originalResultValue,
        int adjustedResultValue) {
        if (!ValidationActive ||
            (!ReferenceEquals(target, ImmunityTarget) &&
             !ReferenceEquals(target, PreventDeathTarget))) {
            return;
        }

        if (DiagnosticEventCount < 0 ||
            DiagnosticEventCount >= DiagnosticEventCap) {
            return;
        }

        DiagnosticEventCount++;

        try {
            PartHealth health = target.GetHealthOptional();
            int hitPointsAtBoundary = health?.HitPointsLeft ?? -1;
            int combatRound =
                Game.Instance?.TurnController?.CombatRound ?? -1;

            Main.LogInfo(
                $"Phase=ROLL_POLICY " +
                $"CombatRound={combatRound} " +
                $"TargetName={target.CharacterName} " +
                $"TargetBlueprint={target.Blueprint?.name ?? "None"} " +
                $"Policy={policy} " +
                $"OriginalResultValue={originalResultValue} " +
                $"AdjustedResultValue={adjustedResultValue} " +
                $"HitPointsAtBoundary={hitPointsAtBoundary}");
        } catch (Exception exception) {
            Main.LogError(
                "Damage-control roll validation diagnostic failed: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    internal static void VerifyCleanupAndReset(string reason) {
        if (!ValidationActive &&
            ImmunityTarget == null &&
            PreventDeathTarget == null) {
            return;
        }

        int remainingPolicies = 0;
        try {
            if (DamageControl.HasPolicyForValidation(
                    ImmunityTarget,
                    DamagePolicy.Immunity)) {
                remainingPolicies++;
            }
            if (DamageControl.HasPolicyForValidation(
                    PreventDeathTarget,
                    DamagePolicy.PreventDeath)) {
                remainingPolicies++;
            }

            Main.LogInfo(
                $"Phase=VALIDATION_CLEANUP " +
                $"Reason={reason} " +
                $"RemainingValidationPolicies={remainingPolicies}");
        } finally {
            ImmunityTarget = null;
            PreventDeathTarget = null;
            ValidationActive = false;
            DiagnosticEventCount = 0;
        }
    }

    private static bool IsValidImmunityTarget(BaseUnitEntity unit) {
        return unit != null &&
               unit is not StarshipEntity &&
               unit.IsInGame &&
               !unit.IsDisposed &&
               unit.IsInCombat;
    }

    private static bool IsValidPreventDeathTarget(
        BaseUnitEntity unit,
        BaseUnitEntity immunityTarget) {
        if (unit == null ||
            unit is StarshipEntity ||
            ReferenceEquals(unit, immunityTarget) ||
            !unit.IsInGame ||
            unit.IsDisposed ||
            !unit.IsInCombat) {
            return false;
        }

        PartLifeState lifeState = unit.LifeState;
        return lifeState != null &&
               lifeState.IsConscious &&
               !lifeState.IsDead &&
               !lifeState.IsFinallyDead &&
               !lifeState.MarkedForDeath &&
               !lifeState.ScriptedKill &&
               unit.GetHealthOptional() != null;
    }

    private static void RecordDealCompleted(RuleDealDamage rule) {
        if (!ValidationActive || rule == null) {
            return;
        }

        BaseUnitEntity target = rule.Target as BaseUnitEntity;
        DamagePolicy policy;
        if (ReferenceEquals(target, ImmunityTarget)) {
            policy = DamagePolicy.Immunity;
        } else if (ReferenceEquals(target, PreventDeathTarget)) {
            policy = DamagePolicy.PreventDeath;
        } else {
            return;
        }

        if (DiagnosticEventCount <= 0 ||
            DiagnosticEventCount > DiagnosticEventCap) {
            return;
        }

        try {
            PartHealth health = rule.TargetHealth;
            int hitPointsAfter = health?.HitPointsLeft ?? -1;
            int cumulativeDamageAfter = health?.Damage ?? -1;
            int temporaryHitPointsAfter =
                health?.TemporaryHitPoints ?? -1;
            string lifeStateAfter =
                target.LifeState?.State.ToString() ?? "Unavailable";
            int combatRound =
                Game.Instance?.TurnController?.CombatRound ?? -1;

            Main.LogInfo(
                $"Phase=DEAL_COMPLETED " +
                $"CombatRound={combatRound} " +
                $"TargetName={target.CharacterName} " +
                $"TargetBlueprint={target.Blueprint?.name ?? "None"} " +
                $"Policy={policy} " +
                $"RuleDealDamageResult={rule.Result} " +
                $"RuleRollDamageResultValue=" +
                $"{rule.RollDamageRule.ResultValue} " +
                $"HPBeforeDamage={rule.HPBeforeDamage} " +
                $"HitPointsAfter={hitPointsAfter} " +
                $"CumulativeHealthDamageAfter=" +
                $"{cumulativeDamageAfter} " +
                $"TemporaryHitPointsAfter={temporaryHitPointsAfter} " +
                $"LifeStateAfter={lifeStateAfter}");

            if (DiagnosticEventCount == DiagnosticEventCap) {
                DiagnosticEventCount = -DiagnosticEventCap;
                Main.LogInfo(
                    "Damage-control runtime validation diagnostic " +
                    $"cap reached: {DiagnosticEventCap} matching damage events.");
            }
        } catch (Exception exception) {
            Main.LogError(
                "Damage-control deal validation diagnostic failed: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    [HarmonyPatch(
        typeof(RuleDealDamage),
        nameof(RuleDealDamage.OnTrigger),
        typeof(RulebookEventContext))]
    private static class RuleDealDamageOnTriggerPatch {
        [HarmonyPostfix]
        private static void Postfix(RuleDealDamage __instance) {
            RecordDealCompleted(__instance);
        }
    }
}
