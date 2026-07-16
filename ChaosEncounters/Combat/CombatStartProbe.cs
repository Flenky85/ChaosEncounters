using Kingmaker.PubSubSystem.Core;

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
    }
}
