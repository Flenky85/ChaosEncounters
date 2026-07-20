using ChaosEncounters.UI;
using Kingmaker.EntitySystem.Entities;

namespace ChaosEncounters.Combat.Mechanics.Boss;

internal sealed class EliteGuardMechanic :
    IEncounterMechanic {
    private const string MechanicId = "EliteGuard";
    private const string HudTitle = "The Elite Guard";
    private const string HudDescription =
        "The Boss and two chosen Guards form an elite defensive unit. The Boss and both Guards begin with 40% damage reduction, which falls to 20% after the first member of the group dies and to 0% after the second. The Guards deal 30% increased damage for the entire encounter.";
    private const string BossMarker = "Boss";
    private const string GuardMarker = "Guard";
    private const int InitialIncomingReduction = 40;
    private const int WeakenedIncomingReduction = 20;
    private const int GuardOutgoingIncrease = 30;

    private static System.Random GuardSelectionRandom;

    private EncounterSession ActiveSession;
    private BaseUnitEntity Boss;
    private BaseUnitEntity GuardOne;
    private BaseUnitEntity GuardTwo;
    private int GroupDeaths;
    private bool BossDead;
    private bool GuardOneDead;
    private bool GuardTwoDead;

    public string Id => MechanicId;

    public bool CanActivate(EncounterSession session) {
        if (session == null ||
            session.Type != EncounterType.Boss ||
            session.Leader == null) {
            return false;
        }

        BaseUnitEntity leader = session.Leader;
        bool leaderFound = false;
        int eligibleGuardCount = 0;
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity candidate =
                session.InitialEnemies[index];
            if (ReferenceEquals(candidate, leader)) {
                leaderFound = true;
            } else if (candidate != null &&
                       !candidate.LifeState.IsDead) {
                eligibleGuardCount++;
            }
        }

        return leaderFound && eligibleGuardCount >= 2;
    }

    public void Activate(EncounterSession session) {
        if (session == null) {
            throw new InvalidOperationException(
                "The Elite Guard requires an encounter session.");
        }
        if (ActiveSession != null) {
            throw new InvalidOperationException(
                "The Elite Guard is already active.");
        }
        if (session.Type != EncounterType.Boss) {
            throw new InvalidOperationException(
                "The Elite Guard requires a Boss encounter.");
        }

        BaseUnitEntity leader = session.Leader;
        if (leader == null) {
            throw new InvalidOperationException(
                "The Elite Guard requires an exact Boss leader.");
        }

        bool leaderFound = false;
        int eligibleGuardCount = 0;
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity candidate =
                session.InitialEnemies[index];
            if (ReferenceEquals(candidate, leader)) {
                leaderFound = true;
            } else if (candidate != null &&
                       !candidate.LifeState.IsDead) {
                eligibleGuardCount++;
            }
        }
        if (!leaderFound || eligibleGuardCount < 2) {
            throw new InvalidOperationException(
                "The Elite Guard requires its exact Boss and at least two living initial subordinates.");
        }

        int firstOrdinal;
        int secondOrdinal;
        if (eligibleGuardCount == 2) {
            firstOrdinal = 0;
            secondOrdinal = 1;
        } else {
            System.Random random = GuardSelectionRandom;
            if (random == null) {
                random = new System.Random();
                GuardSelectionRandom = random;
            }

            firstOrdinal = random.Next(eligibleGuardCount);
            secondOrdinal = random.Next(eligibleGuardCount - 1);
            if (secondOrdinal >= firstOrdinal) {
                secondOrdinal++;
            }
        }

        BaseUnitEntity guardOne = null;
        BaseUnitEntity guardTwo = null;
        int eligibleOrdinal = 0;
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity candidate =
                session.InitialEnemies[index];
            if (ReferenceEquals(candidate, leader) ||
                candidate == null ||
                candidate.LifeState.IsDead) {
                continue;
            }

            if (eligibleOrdinal == firstOrdinal) {
                guardOne = candidate;
            }
            if (eligibleOrdinal == secondOrdinal) {
                guardTwo = candidate;
            }
            eligibleOrdinal++;
        }
        if (guardOne == null ||
            guardTwo == null ||
            ReferenceEquals(guardOne, guardTwo) ||
            ReferenceEquals(guardOne, leader) ||
            ReferenceEquals(guardTwo, leader)) {
            throw new InvalidOperationException(
                "The Elite Guard could not resolve two distinct eligible Guards.");
        }

        ActiveSession = session;
        Boss = leader;
        GuardOne = guardOne;
        GuardTwo = guardTwo;
        GroupDeaths = 0;
        BossDead = false;
        GuardOneDead = false;
        GuardTwoDead = false;

        DamageControl.SetIncomingDamageReduction(
            Boss,
            InitialIncomingReduction);
        UnitMarker.SetMarker(
            Boss,
            BossMarker,
            ChaosColors.Red);

        DamageControl.SetIncomingDamageReduction(
            GuardOne,
            InitialIncomingReduction);
        DamageControl.SetOutgoingDamageIncrease(
            GuardOne,
            GuardOutgoingIncrease);
        UnitMarker.SetMarker(
            GuardOne,
            GuardMarker,
            ChaosColors.Orange);

        DamageControl.SetIncomingDamageReduction(
            GuardTwo,
            InitialIncomingReduction);
        DamageControl.SetOutgoingDamageIncrease(
            GuardTwo,
            GuardOutgoingIncrease);
        UnitMarker.SetMarker(
            GuardTwo,
            GuardMarker,
            ChaosColors.Orange);

        EncounterHud.Show(
            HudTitle,
            HudDescription);
        Main.LogInfo(
            $"The Elite Guard activated: " +
            $"BossName={Boss.CharacterName} " +
            $"GuardOneName={GuardOne.CharacterName} " +
            $"GuardTwoName={GuardTwo.CharacterName} " +
            $"IncomingReduction={InitialIncomingReduction} " +
            $"GuardOutgoingIncrease={GuardOutgoingIncrease}");
    }

    public void HandleRoundStart(int combatRound) {
    }

    public void HandleRoundEnd(int combatRound) {
    }

    public void HandleUnitTurnStart(
        BaseUnitEntity unit,
        int combatRound) {
    }

    public void HandleUnitTurnEnd(
        BaseUnitEntity unit,
        int combatRound) {
    }

    public void HandleEnemyDeath(
        BaseUnitEntity unit,
        int combatRound) {
        if (ActiveSession == null || unit == null) {
            return;
        }

        if (ReferenceEquals(unit, Boss)) {
            if (BossDead) {
                return;
            }
            BossDead = true;
        } else if (ReferenceEquals(unit, GuardOne)) {
            if (GuardOneDead) {
                return;
            }
            GuardOneDead = true;
        } else if (ReferenceEquals(unit, GuardTwo)) {
            if (GuardTwoDead) {
                return;
            }
            GuardTwoDead = true;
        } else {
            return;
        }

        GroupDeaths++;
        DamageControl.ClearIncomingDamageReduction(unit);
        UnitMarker.ClearMarker(unit);

        if (GroupDeaths == 1) {
            if (!BossDead) {
                DamageControl.SetIncomingDamageReduction(
                    Boss,
                    WeakenedIncomingReduction);
            }
            if (!GuardOneDead) {
                DamageControl.SetIncomingDamageReduction(
                    GuardOne,
                    WeakenedIncomingReduction);
            }
            if (!GuardTwoDead) {
                DamageControl.SetIncomingDamageReduction(
                    GuardTwo,
                    WeakenedIncomingReduction);
            }

            Main.LogInfo(
                $"The Elite Guard defense weakened: " +
                $"FallenMember={unit.CharacterName} " +
                $"IncomingReduction={WeakenedIncomingReduction}");
        } else if (GroupDeaths == 2) {
            if (!BossDead) {
                DamageControl.ClearIncomingDamageReduction(Boss);
            }
            if (!GuardOneDead) {
                DamageControl.ClearIncomingDamageReduction(GuardOne);
            }
            if (!GuardTwoDead) {
                DamageControl.ClearIncomingDamageReduction(GuardTwo);
            }

            Main.LogInfo(
                $"The Elite Guard defense broken: " +
                $"FallenMember={unit.CharacterName} " +
                $"IncomingReduction=0");
        }

        if (GuardOneDead && GuardTwoDead) {
            EncounterHud.Hide();
        }
    }

    public void Deactivate(
        EncounterMechanicEndReason reason) {
        ActiveSession = null;
        Boss = null;
        GuardOne = null;
        GuardTwo = null;
        GroupDeaths = 0;
        BossDead = false;
        GuardOneDead = false;
        GuardTwoDead = false;
    }
}
