using ChaosEncounters.UI;
using ChaosEncounters.Combat.Mechanics.Boss;
using ChaosEncounters.Combat.Mechanics.Common;
using Kingmaker.EntitySystem.Entities;

namespace ChaosEncounters.Combat.Mechanics;

internal static class EncounterMechanicController {
    private static System.Random SelectionRandom;

    private static readonly IEncounterMechanic[] CommonMechanics = {
        new ExecutionListMechanic()
    };

    private static readonly IEncounterMechanic[] BossMechanics = {
        new TyrantsAegisMechanic(),
        new WallOfFleshMechanic(),
        new EliteGuardMechanic()
    };

    private static EncounterSession ActiveSession;
    private static IEncounterMechanic ActiveMechanic;

    internal static bool HasActiveMechanic =>
        ActiveMechanic != null;

    internal static IReadOnlyList<IEncounterMechanic>
        GetRegisteredMechanics(EncounterType encounterType) {
        switch (encounterType) {
            case EncounterType.Common:
                return CommonMechanics;
            case EncounterType.Boss:
                return BossMechanics;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(encounterType),
                    encounterType,
                    "Unsupported encounter type.");
        }
    }

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
        int compatibleCandidateCount = 0;
        for (int index = 0; index < candidates.Length; index++) {
            IEncounterMechanic candidate = candidates[index];
            if (candidate != null &&
                candidate.CanActivate(session)) {
                compatibleCandidateCount++;
            }
        }
        if (compatibleCandidateCount == 0) {
            return;
        }

        int selectedCompatibleOrdinal;
        if (compatibleCandidateCount == 1) {
            selectedCompatibleOrdinal = 0;
        } else {
            System.Random random = SelectionRandom;
            if (random == null) {
                random = new System.Random();
                SelectionRandom = random;
            }

            selectedCompatibleOrdinal =
                random.Next(compatibleCandidateCount);
        }

        int selectedIndex = -1;
        IEncounterMechanic selectedMechanic = null;
        int compatibleOrdinal = 0;
        for (int index = 0; index < candidates.Length; index++) {
            IEncounterMechanic candidate = candidates[index];
            if (candidate == null ||
                !candidate.CanActivate(session)) {
                continue;
            }
            if (compatibleOrdinal == selectedCompatibleOrdinal) {
                selectedIndex = index;
                selectedMechanic = candidate;
                break;
            }

            compatibleOrdinal++;
        }
        if (selectedMechanic == null || selectedIndex < 0) {
            throw new InvalidOperationException(
                "The selected compatible encounter mechanic could not be resolved.");
        }

        ActiveMechanic = selectedMechanic;
        Main.LogInfo(
            $"Encounter mechanic selected: EncounterType={session.Type} " +
            $"CandidateCount={compatibleCandidateCount} " +
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

    internal static bool DisableActiveMechanicForCurrentCombat() {
        IEncounterMechanic mechanic = ActiveMechanic;
        if (mechanic == null) {
            return false;
        }

        string mechanicId = mechanic.Id;
        ActiveMechanic = null;
        CleanupMechanic(
            mechanic,
            EncounterMechanicEndReason.ManualEmergencyDisable);
        Main.LogWarning(
            $"Encounter mechanic disabled for the current combat: " +
            $"MechanicId={mechanicId}");
        return true;
    }

    internal static void Deactivate(
        EncounterMechanicEndReason reason) {
        IEncounterMechanic mechanic = ActiveMechanic;
        ActiveMechanic = null;
        ActiveSession = null;

        CleanupMechanic(mechanic, reason);
    }

    private static void CleanupMechanic(
        IEncounterMechanic mechanic,
        EncounterMechanicEndReason reason) {
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
