using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem.Core;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Parts;

namespace ChaosEncounters.Combat;

internal enum DamagePolicy {
    None,
    Immunity,
    PreventDeath
}

internal static class DamageControl {
    private const string HarmonyId = "ChaosEncounters.DamageControl";
    private static readonly Dictionary<BaseUnitEntity, DamagePolicy> Policies =
        new(UnitReferenceComparer.Instance);
    private static bool Initialized;

    internal static void Initialize() {
        if (Initialized) {
            return;
        }

        new Harmony(HarmonyId)
            .CreateClassProcessor(typeof(RuleRollDamageOnTriggerPatch))
            .Patch();
        DamageControlRuntimeValidation.Initialize();
        Initialized = true;
    }

    internal static void SetPolicy(BaseUnitEntity unit, DamagePolicy policy) {
        if (unit == null) {
            return;
        }

        if (policy == DamagePolicy.None) {
            ClearPolicy(unit);
            return;
        }

        if ((policy != DamagePolicy.Immunity &&
             policy != DamagePolicy.PreventDeath) ||
            unit is StarshipEntity ||
            unit.IsDisposed ||
            !unit.IsInGame) {
            return;
        }

        Policies[unit] = policy;
    }

    internal static void ClearPolicy(BaseUnitEntity unit) {
        if (unit != null) {
            Policies.Remove(unit);
        }
    }

    internal static void ClearAllPolicies() {
        Policies.Clear();
    }

    // Temporary support for the Point 4B runtime-validation harness.
    internal static bool HasPolicyForValidation(
        BaseUnitEntity unit,
        DamagePolicy expectedPolicy) {
        return unit != null &&
               Policies.TryGetValue(unit, out DamagePolicy policy) &&
               policy == expectedPolicy;
    }

    private static void HandleCompletedDamageRoll(RuleRollDamage rule) {
        if (Policies.Count == 0) {
            return;
        }

        if (rule == null) {
            return;
        }

        int originalDamage = rule.ResultValue;
        if (originalDamage <= 0) {
            return;
        }

        if (rule.Target is not BaseUnitEntity targetUnit ||
            targetUnit is StarshipEntity) {
            return;
        }

        if (!Policies.TryGetValue(targetUnit, out DamagePolicy policy)) {
            return;
        }

        try {
            switch (policy) {
                case DamagePolicy.Immunity:
                    rule.ResultValue = 0;
                    break;

                case DamagePolicy.PreventDeath:
                    ApplyPreventDeath(rule, targetUnit, originalDamage);
                    break;
            }
        } catch (Exception exception) {
            Policies.Clear();
            Main.LogError($"Damage-control policy processing failed; all policies were cleared: {exception}");
            return;
        }

        DamageControlRuntimeValidation.RecordRollPolicy(
            targetUnit,
            policy,
            originalDamage,
            rule.ResultValue);
    }

    private static void ApplyPreventDeath(
        RuleRollDamage rule,
        BaseUnitEntity targetUnit,
        int originalDamage) {
        PartHealth health = targetUnit.GetHealthOptional();
        if (health == null) {
            return;
        }

        int currentHitPoints = health.HitPointsLeft;
        int maximumAllowedDamage = currentHitPoints > 1
            ? currentHitPoints - 1
            : 0;
        int adjustedDamage = originalDamage < maximumAllowedDamage
            ? originalDamage
            : maximumAllowedDamage;
        if (adjustedDamage < 0) {
            adjustedDamage = 0;
        }

        if (adjustedDamage != originalDamage) {
            rule.ResultValue = adjustedDamage;
        }
    }

    private sealed class UnitReferenceComparer : IEqualityComparer<BaseUnitEntity> {
        internal static readonly UnitReferenceComparer Instance = new();

        public bool Equals(BaseUnitEntity x, BaseUnitEntity y) {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(BaseUnitEntity unit) {
            return RuntimeHelpers.GetHashCode(unit);
        }
    }

    [HarmonyPatch(
        typeof(RuleRollDamage),
        nameof(RuleRollDamage.OnTrigger),
        typeof(RulebookEventContext))]
    private static class RuleRollDamageOnTriggerPatch {
        [HarmonyPostfix]
        private static void Postfix(RuleRollDamage __instance) {
            HandleCompletedDamageRoll(__instance);
        }
    }
}
