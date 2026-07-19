using System;
using HarmonyLib;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.GameModes;
using Kingmaker.PubSubSystem.Core;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Parts;

namespace ChaosEncounters.Combat;

internal static class DamageControlPrototype {
    private const string HarmonyId = "ChaosEncounters.DamageControlPrototype";
    private static bool Initialized;
    private static bool RuntimeEnabled = true;

    internal static void Initialize() {
        if (Initialized) {
            return;
        }

        var harmony = new Harmony(HarmonyId);
        harmony.CreateClassProcessor(typeof(RuleRollDamageOnTriggerPatch)).Patch();
        Initialized = true;
    }

    internal static void HandleCompletedDamageRoll(RuleRollDamage rule) {
        if (!RuntimeEnabled) {
            return;
        }

        try {
            ApplyTemporaryTestPolicy(rule);
        } catch (Exception exception) {
            RuntimeEnabled = false;
            Main.LogError($"Damage-control prototype failed and was disabled: {exception}");
        }
    }

    private static void ApplyTemporaryTestPolicy(RuleRollDamage rule) {
        if (rule == null ||
            rule.ResultValue <= 0 ||
            rule.Target is not BaseUnitEntity targetUnit ||
            targetUnit is StarshipEntity ||
            !targetUnit.IsInCombat) {
            return;
        }

        Game game = Game.Instance;
        if (game?.TurnController?.TurnBasedModeActive != true ||
            game.CurrentMode == GameModeType.SpaceCombat ||
            game.CurrentMode == GameModeType.StarSystem) {
            return;
        }

        if (targetUnit.IsInPlayerParty) {
            rule.ResultValue = 0;
            return;
        }

        if (!targetUnit.Faction.IsPlayerEnemy) {
            return;
        }

        PartHealth health = targetUnit.GetHealthOptional();
        if (health == null) {
            return;
        }

        int originalFinalDamage = rule.ResultValue;
        int currentHitPoints = health.HitPointsLeft;
        int maximumAllowedDamage = currentHitPoints > 1 ? currentHitPoints - 1 : 0;
        int adjustedDamage = originalFinalDamage < maximumAllowedDamage
            ? originalFinalDamage
            : maximumAllowedDamage;
        if (adjustedDamage != originalFinalDamage) {
            rule.ResultValue = adjustedDamage;
        }
    }
}

[HarmonyPatch(
    typeof(RuleRollDamage),
    nameof(RuleRollDamage.OnTrigger),
    typeof(RulebookEventContext))]
internal static class RuleRollDamageOnTriggerPatch {
    [HarmonyPostfix]
    private static void Postfix(RuleRollDamage __instance) {
        DamageControlPrototype.HandleCompletedDamageRoll(__instance);
    }
}
