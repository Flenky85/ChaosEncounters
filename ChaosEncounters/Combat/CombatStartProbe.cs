using Kingmaker;
using Kingmaker.PubSubSystem.Core;
using System.Linq;

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

        Main.LogInfo("Combat started.");

        var combatUnits = Game.Instance.State.AllBaseAwakeUnitsForSure
            .Where(unit => unit.IsInCombat)
            .ToList();

        Main.LogInfo($"Combat units: {combatUnits.Count}");
        foreach (var unit in combatUnits) {
            Main.LogInfo($"Combat unit: {unit.CharacterName}");
        }
    }
}
