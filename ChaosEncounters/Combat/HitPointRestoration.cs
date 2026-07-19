using System;
using HarmonyLib;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.PubSubSystem.Core;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Parts;

namespace ChaosEncounters.Combat;

internal static class HitPointRestoration {
    private const string HarmonyId = "ChaosEncounters.HitPointRestoration";
    private static bool Initialized;
    private static RuleCalculateHeal ActiveCalculation;

    internal static void Initialize() {
        if (Initialized) {
            return;
        }

        new Harmony(HarmonyId)
            .CreateClassProcessor(typeof(RuleCalculateHealOnTriggerPatch))
            .Patch();
        Initialized = true;
    }

    internal static int RestoreHitPoints(
        BaseUnitEntity unit,
        int requestedAmount) {
        if (unit == null || requestedAmount <= 0) {
            return 0;
        }

        try {
            if (unit is StarshipEntity ||
                !unit.IsInGame ||
                unit.IsDisposed ||
                !unit.IsInCombat) {
                return 0;
            }

            PartLifeState lifeState = unit.LifeState;
            if (lifeState == null ||
                !lifeState.IsConscious ||
                lifeState.IsDead ||
                lifeState.IsFinallyDead ||
                lifeState.MarkedForDeath ||
                lifeState.ScriptedKill) {
                return 0;
            }

            PartHealth health = unit.GetHealthOptional();
            PartDestructionStagesManager destructionStages =
                unit.GetDestructionStagesManagerOptional();
            if (health == null ||
                health.MaxHitPoints <= 0 ||
                destructionStages?.Stage == DestructionStage.Destroyed) {
                return 0;
            }

            int damageBefore = health.Damage;
            if (damageBefore <= 0) {
                return 0;
            }

            int amountToRestore = requestedAmount < damageBefore
                ? requestedAmount
                : damageBefore;
            if (amountToRestore <= 0) {
                return 0;
            }

            if (ActiveCalculation != null) {
                Main.LogError(
                    "Hit-point restoration rejected because another Chaos Encounters restoration is already active.");
                return 0;
            }

            var rule = new RuleHealDamage(
                unit,
                unit,
                DiceFormula.Zero,
                amountToRestore);
            RuleCalculateHeal calculation = rule.CalculateHealRule;
            bool referenceRemainedConsistent;

            ActiveCalculation = calculation;
            try {
                Rulebook.Trigger(rule);
            } finally {
                referenceRemainedConsistent =
                    ReferenceEquals(ActiveCalculation, calculation);
                ActiveCalculation = null;
            }

            if (!referenceRemainedConsistent) {
                Main.LogError(
                    "Hit-point restoration calculation reference changed unexpectedly.");
                return 0;
            }

            int damageAfter = health.Damage;
            int applied = damageBefore - damageAfter;
            if (applied < 0 || applied > damageBefore) {
                Main.LogError(
                    $"Hit-point restoration produced an invalid applied amount: " +
                    $"DamageBefore={damageBefore}, DamageAfter={damageAfter}, Applied={applied}.");
                return 0;
            }

            return applied;
        } catch (Exception exception) {
            Main.LogError($"Hit-point restoration failed: {exception}");
            return 0;
        }
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
