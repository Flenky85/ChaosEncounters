using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem.Core;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Parts;
using UnityEngine;

namespace ChaosEncounters.Combat;

internal static class DamageControl {
    private const string HarmonyId = "ChaosEncounters.DamageControl";
    private static readonly Dictionary<BaseUnitEntity, DamageAdjustment> Adjustments =
        new(UnitReferenceComparer.Instance);
    private static readonly Dictionary<BaseUnitEntity, OffTargetDamagePolicy>
        OffTargetDamagePolicies =
            new(UnitReferenceComparer.Instance);
    private static bool Initialized;

    internal static void Initialize() {
        if (Initialized) {
            return;
        }

        new Harmony(HarmonyId)
            .CreateClassProcessor(typeof(RuleRollDamageOnTriggerPatch))
            .Patch();
        Initialized = true;
    }

    internal static void SetIncomingDamageReduction(
        BaseUnitEntity unit,
        int percent) {
        ValidatePercent(percent, nameof(percent));
        if (percent == 0) {
            ClearIncomingDamageReduction(unit);
            return;
        }
        if (!IsSupportedUnit(unit)) {
            return;
        }

        Adjustments.TryGetValue(
            unit,
            out DamageAdjustment current);
        SetAdjustment(
            unit,
            new DamageAdjustment(
                percent,
                current.OutgoingIncreasePercent,
                current.PreventDeath));
    }

    internal static void ClearIncomingDamageReduction(
        BaseUnitEntity unit) {
        if (unit == null ||
            !Adjustments.TryGetValue(
                unit,
                out DamageAdjustment current)) {
            return;
        }

        SetAdjustment(
            unit,
            new DamageAdjustment(
                0,
                current.OutgoingIncreasePercent,
                current.PreventDeath));
    }

    internal static void SetOutgoingDamageIncrease(
        BaseUnitEntity unit,
        int percent) {
        ValidatePercent(percent, nameof(percent));
        if (percent == 0) {
            ClearOutgoingDamageIncrease(unit);
            return;
        }
        if (!IsSupportedUnit(unit)) {
            return;
        }

        Adjustments.TryGetValue(
            unit,
            out DamageAdjustment current);
        SetAdjustment(
            unit,
            new DamageAdjustment(
                current.IncomingReductionPercent,
                percent,
                current.PreventDeath));
    }

    internal static void ClearOutgoingDamageIncrease(
        BaseUnitEntity unit) {
        if (unit == null ||
            !Adjustments.TryGetValue(
                unit,
                out DamageAdjustment current)) {
            return;
        }

        SetAdjustment(
            unit,
            new DamageAdjustment(
                current.IncomingReductionPercent,
                0,
                current.PreventDeath));
    }

    internal static void SetPreventDeath(
        BaseUnitEntity unit,
        bool enabled) {
        if (!enabled) {
            if (unit == null ||
                !Adjustments.TryGetValue(
                    unit,
                    out DamageAdjustment current)) {
                return;
            }

            SetAdjustment(
                unit,
                new DamageAdjustment(
                    current.IncomingReductionPercent,
                    current.OutgoingIncreasePercent,
                    false));
            return;
        }
        if (!IsSupportedUnit(unit)) {
            return;
        }

        Adjustments.TryGetValue(
            unit,
            out DamageAdjustment existing);
        SetAdjustment(
            unit,
            new DamageAdjustment(
                existing.IncomingReductionPercent,
                existing.OutgoingIncreasePercent,
                true));
    }

    internal static void SetOffTargetDamageReduction(
        BaseUnitEntity attacker,
        BaseUnitEntity allowedTarget,
        int percent) {
        ValidatePercent(percent, nameof(percent));
        if (percent == 0 || allowedTarget == null) {
            ClearOffTargetDamageReduction(attacker);
            return;
        }
        if (!IsSupportedUnit(attacker)) {
            ClearOffTargetDamageReduction(attacker);
            return;
        }

        OffTargetDamagePolicies[attacker] =
            new OffTargetDamagePolicy(
                allowedTarget,
                percent);
    }

    internal static void ClearOffTargetDamageReduction(
        BaseUnitEntity attacker) {
        if (attacker != null) {
            OffTargetDamagePolicies.Remove(attacker);
        }
    }

    internal static void ClearPolicy(BaseUnitEntity unit) {
        if (unit != null) {
            Adjustments.Remove(unit);
            OffTargetDamagePolicies.Remove(unit);
        }
    }

    internal static void ClearAllPolicies() {
        Adjustments.Clear();
        OffTargetDamagePolicies.Clear();
    }

    private static void HandleCompletedDamageRoll(
        RuleRollDamage rule) {
        if (Adjustments.Count == 0 &&
            OffTargetDamagePolicies.Count == 0) {
            return;
        }
        if (rule == null) {
            return;
        }

        int originalDamage = rule.ResultValue;
        if (originalDamage <= 0) {
            return;
        }
        if (rule.TargetUnit is not BaseUnitEntity targetUnit ||
            targetUnit is StarshipEntity) {
            return;
        }

        try {
            int outgoingIncreasePercent = 0;
            int offTargetReductionPercent = 0;
            BaseUnitEntity initiatorUnit =
                rule.InitiatorUnit as BaseUnitEntity;
            if (initiatorUnit != null &&
                Adjustments.TryGetValue(
                    initiatorUnit,
                    out DamageAdjustment initiatorAdjustment)) {
                outgoingIncreasePercent =
                    initiatorAdjustment.OutgoingIncreasePercent;
            }
            if (initiatorUnit != null &&
                targetUnit.IsPlayerEnemy &&
                OffTargetDamagePolicies.TryGetValue(
                    initiatorUnit,
                    out OffTargetDamagePolicy offTargetPolicy) &&
                !ReferenceEquals(
                    targetUnit,
                    offTargetPolicy.AllowedTarget)) {
                offTargetReductionPercent =
                    offTargetPolicy.ReductionPercent;
            }

            Adjustments.TryGetValue(
                targetUnit,
                out DamageAdjustment targetAdjustment);
            int incomingReductionPercent =
                targetAdjustment.IncomingReductionPercent;
            bool preventDeath = targetAdjustment.PreventDeath;

            if (outgoingIncreasePercent == 0 &&
                offTargetReductionPercent == 0 &&
                incomingReductionPercent == 0 &&
                !preventDeath) {
                return;
            }

            int adjustedDamage;
            if (incomingReductionPercent == 100) {
                adjustedDamage = 0;
            } else {
                float multiplier =
                    (1f + outgoingIncreasePercent / 100f) *
                    (1f - offTargetReductionPercent / 100f) *
                    (1f - incomingReductionPercent / 100f);
                adjustedDamage = Mathf.RoundToInt(
                    originalDamage * multiplier);
                if (adjustedDamage < 0) {
                    adjustedDamage = 0;
                }
            }

            if (preventDeath) {
                adjustedDamage = ApplyPreventDeath(
                    targetUnit,
                    adjustedDamage);
            }

            rule.ResultValue = adjustedDamage;
        } catch (Exception exception) {
            Adjustments.Clear();
            OffTargetDamagePolicies.Clear();
            Main.LogError(
                $"Damage-control adjustment processing failed; all policies were cleared: {exception}");
        }
    }

    private static int ApplyPreventDeath(
        BaseUnitEntity targetUnit,
        int adjustedDamage) {
        PartHealth health = targetUnit.GetHealthOptional();
        if (health == null) {
            return adjustedDamage;
        }

        int currentHitPoints = health.HitPointsLeft;
        int maximumAllowedDamage = currentHitPoints > 1
            ? currentHitPoints - 1
            : 0;
        int finalDamage = adjustedDamage < maximumAllowedDamage
            ? adjustedDamage
            : maximumAllowedDamage;
        return finalDamage < 0 ? 0 : finalDamage;
    }

    private static bool IsSupportedUnit(BaseUnitEntity unit) {
        return unit != null &&
               unit is not StarshipEntity &&
               !unit.IsDisposed &&
               unit.IsInGame;
    }

    private static void ValidatePercent(
        int percent,
        string parameterName) {
        if (percent < 0 || percent > 100) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                percent,
                "Damage adjustment percentages must be between 0 and 100.");
        }
    }

    private static void SetAdjustment(
        BaseUnitEntity unit,
        DamageAdjustment adjustment) {
        if (adjustment.IsEmpty) {
            Adjustments.Remove(unit);
        } else {
            Adjustments[unit] = adjustment;
        }
    }

    private readonly struct DamageAdjustment {
        internal int IncomingReductionPercent { get; }
        internal int OutgoingIncreasePercent { get; }
        internal bool PreventDeath { get; }

        internal bool IsEmpty =>
            IncomingReductionPercent == 0 &&
            OutgoingIncreasePercent == 0 &&
            !PreventDeath;

        internal DamageAdjustment(
            int incomingReductionPercent,
            int outgoingIncreasePercent,
            bool preventDeath) {
            IncomingReductionPercent = incomingReductionPercent;
            OutgoingIncreasePercent = outgoingIncreasePercent;
            PreventDeath = preventDeath;
        }
    }

    private readonly struct OffTargetDamagePolicy {
        internal BaseUnitEntity AllowedTarget { get; }
        internal int ReductionPercent { get; }

        internal OffTargetDamagePolicy(
            BaseUnitEntity allowedTarget,
            int reductionPercent) {
            AllowedTarget = allowedTarget;
            ReductionPercent = reductionPercent;
        }
    }

    private sealed class UnitReferenceComparer :
        IEqualityComparer<BaseUnitEntity> {
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
