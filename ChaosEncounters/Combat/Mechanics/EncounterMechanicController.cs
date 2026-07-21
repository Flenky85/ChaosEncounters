using ChaosEncounters.Configuration;
using ChaosEncounters.UI;
using ChaosEncounters.Combat.Mechanics.Boss;
using ChaosEncounters.Combat.Mechanics.Common;
using Kingmaker.EntitySystem.Entities;

namespace ChaosEncounters.Combat.Mechanics;

internal static class EncounterMechanicController {
    private static System.Random SelectionRandom;

    private static readonly IEncounterMechanic[] CommonMechanics = {
        new ExecutionListMechanic(),
        new RisingVengeanceMechanic(),
        new EqualizerMechanic()
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

    internal static bool HasEnemyJoinAwareMechanic =>
        ActiveMechanic is IEnemyJoinAwareMechanic;

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
                    "Registered mechanics require one exact supported encounter category.");
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

        bool supportsCommon =
            session.SupportsEncounterType(
                EncounterType.Common);
        bool supportsBoss =
            session.SupportsEncounterType(
                EncounterType.Boss);
        if (!supportsCommon && !supportsBoss) {
            throw new InvalidOperationException(
                $"Unsupported encounter eligibility: {session.Type}.");
        }

        ActiveSession = session;
        int compatibleCandidateCount = 0;
        if (supportsCommon) {
            compatibleCandidateCount +=
                CountEnabledCompatibleCandidates(
                    CommonMechanics,
                    session);
        }
        if (supportsBoss) {
            compatibleCandidateCount +=
                CountEnabledCompatibleCandidates(
                    BossMechanics,
                    session);
        }
        if (compatibleCandidateCount == 0) {
            Main.LogInfo(
                $"No enabled compatible encounter mechanic was available: " +
                $"EligibleEncounterTypes={session.Type}");
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
        EncounterType selectedPool = EncounterType.None;
        if (supportsCommon &&
            TryResolveEnabledCompatibleCandidate(
                CommonMechanics,
                session,
                selectedCompatibleOrdinal,
                ref compatibleOrdinal,
                out selectedMechanic,
                out selectedIndex)) {
            selectedPool = EncounterType.Common;
        }
        if (selectedMechanic == null &&
            supportsBoss &&
            TryResolveEnabledCompatibleCandidate(
                BossMechanics,
                session,
                selectedCompatibleOrdinal,
                ref compatibleOrdinal,
                out selectedMechanic,
                out selectedIndex)) {
            selectedPool = EncounterType.Boss;
        }
        if (selectedMechanic == null ||
            selectedIndex < 0 ||
            selectedPool == EncounterType.None) {
            throw new InvalidOperationException(
                "The selected compatible encounter mechanic could not be resolved.");
        }

        ActiveMechanic = selectedMechanic;
        Main.LogInfo(
            $"Encounter mechanic selected: EligibleEncounterTypes={session.Type} " +
            $"CandidateCount={compatibleCandidateCount} " +
            $"SelectedCompatibleOrdinal={selectedCompatibleOrdinal} " +
            $"SelectedPool={selectedPool} " +
            $"SelectedIndex={selectedIndex} " +
            $"SelectedMechanicId={selectedMechanic.Id}");
        selectedMechanic.Activate(session);
    }

    private static int CountEnabledCompatibleCandidates(
        IEncounterMechanic[] candidates,
        EncounterSession session) {
        int compatibleCandidateCount = 0;
        for (int index = 0;
             index < candidates.Length;
             index++) {
            if (IsEnabledCompatibleCandidate(
                    candidates[index],
                    session)) {
                compatibleCandidateCount++;
            }
        }

        return compatibleCandidateCount;
    }

    private static bool TryResolveEnabledCompatibleCandidate(
        IEncounterMechanic[] candidates,
        EncounterSession session,
        int selectedCompatibleOrdinal,
        ref int compatibleOrdinal,
        out IEncounterMechanic selectedMechanic,
        out int selectedIndex) {
        for (int index = 0;
             index < candidates.Length;
             index++) {
            IEncounterMechanic candidate = candidates[index];
            if (!IsEnabledCompatibleCandidate(
                    candidate,
                    session)) {
                continue;
            }
            if (compatibleOrdinal ==
                selectedCompatibleOrdinal) {
                selectedMechanic = candidate;
                selectedIndex = index;
                return true;
            }

            compatibleOrdinal++;
        }

        selectedMechanic = null;
        selectedIndex = -1;
        return false;
    }

    private static bool IsEnabledCompatibleCandidate(
        IEncounterMechanic candidate,
        EncounterSession session) {
        return candidate != null &&
               ModSettings.IsEncounterMechanicEnabled(
                   candidate.Id) &&
               candidate.CanActivate(session);
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

    internal static void HandleEnemyJoined(
        BaseUnitEntity unit) {
        if (ActiveMechanic is
            IEnemyJoinAwareMechanic handler) {
            handler.HandleEnemyJoined(unit);
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
