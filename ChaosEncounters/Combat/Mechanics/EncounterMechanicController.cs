using ChaosEncounters.UI;
using Kingmaker.EntitySystem.Entities;

namespace ChaosEncounters.Combat.Mechanics;

internal static class EncounterMechanicController {
    private static System.Random SelectionRandom;

    private static readonly IEncounterMechanic[] CommonMechanics =
        Array.Empty<IEncounterMechanic>();

    private static readonly IEncounterMechanic[] BossMechanics =
        Array.Empty<IEncounterMechanic>();

    private static EncounterSession ActiveSession;
    private static IEncounterMechanic ActiveMechanic;

    internal static void Activate(EncounterSession session) {
        if (session == null) {
            throw new ArgumentNullException(nameof(session));
        }
        if (ReferenceEquals(ActiveSession, session)) {
            return;
        }
        if (ActiveSession != null) {
            throw new InvalidOperationException(
                "A different encounter session is already active.");
        }

        IEncounterMechanic[] candidates;
        switch (session.Type) {
            case EncounterType.Common:
                candidates = CommonMechanics;
                break;
            case EncounterType.Boss:
                candidates = BossMechanics;
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported encounter type: {session.Type}.");
        }

        ActiveSession = session;
        if (candidates.Length == 0) {
            return;
        }

        System.Random random = SelectionRandom;
        if (random == null) {
            random = new System.Random();
            SelectionRandom = random;
        }

        int selectedIndex = random.Next(candidates.Length);
        IEncounterMechanic selectedMechanic =
            candidates[selectedIndex];
        if (selectedMechanic == null) {
            throw new InvalidOperationException(
                "The selected encounter mechanic candidate is null.");
        }

        ActiveMechanic = selectedMechanic;
        Main.LogInfo(
            $"Encounter mechanic selected: EncounterType={session.Type} " +
            $"CandidateCount={candidates.Length} " +
            $"SelectedIndex={selectedIndex} " +
            $"SelectedMechanicId={selectedMechanic.Id}");
        selectedMechanic.Activate(session);
    }

    internal static void HandleRoundStart(int combatRound) {
        IEncounterMechanic mechanic = ActiveMechanic;
        if (mechanic != null) {
            mechanic.HandleRoundStart(combatRound);
        }
    }

    internal static void HandleRoundEnd(int combatRound) {
        IEncounterMechanic mechanic = ActiveMechanic;
        if (mechanic != null) {
            mechanic.HandleRoundEnd(combatRound);
        }
    }

    internal static void HandleUnitTurnStart(
        BaseUnitEntity unit,
        int combatRound) {
        IEncounterMechanic mechanic = ActiveMechanic;
        if (mechanic != null) {
            mechanic.HandleUnitTurnStart(
                unit,
                combatRound);
        }
    }

    internal static void HandleUnitTurnEnd(
        BaseUnitEntity unit,
        int combatRound) {
        IEncounterMechanic mechanic = ActiveMechanic;
        if (mechanic != null) {
            mechanic.HandleUnitTurnEnd(
                unit,
                combatRound);
        }
    }

    internal static void HandleEnemyDeath(
        BaseUnitEntity unit,
        int combatRound) {
        IEncounterMechanic mechanic = ActiveMechanic;
        if (mechanic != null) {
            mechanic.HandleEnemyDeath(
                unit,
                combatRound);
        }
    }

    internal static void Deactivate(
        EncounterMechanicEndReason reason) {
        IEncounterMechanic mechanic = ActiveMechanic;
        ActiveMechanic = null;
        ActiveSession = null;

        try {
            if (mechanic != null) {
                try {
                    mechanic.Deactivate(reason);
                } catch (Exception exception) {
                    Main.LogError(
                        $"Encounter mechanic deactivation failed: {exception}");
                }
            }
        } finally {
            DamageControl.ClearAllPolicies();
            UnitMarker.ClearAllMarkers();
            EncounterHud.Hide();
        }
    }
}
