using ChaosEncounters.Combat.Persistence;
using ChaosEncounters.UI;
using Kingmaker.EntitySystem.Entities;

namespace ChaosEncounters.Combat.Mechanics.Boss;

internal sealed class EliteGuardMechanic :
    IEncounterMechanic,
    IPersistableEncounterMechanic {
    private const string MechanicId = "EliteGuard";
    private const string HudTitle = "The Elite Guard";
    private const string HudDescription =
        "The Boss and two chosen Guards form an elite defensive unit. The Boss and both Guards begin with 60% damage reduction, which falls to 30% after the first member of the group dies and to 0% after the second. The Guards deal 30% increased damage for the entire encounter.";
    private const string BossMarker = "Boss";
    private const string GuardMarker = "Guard";
    private const int InitialIncomingReduction = 60;
    private const int WeakenedIncomingReduction = 30;
    private const int GuardOutgoingIncrease = 30;

    private static System.Random GuardSelectionRandom;

    private bool Active;
    private string BossId;
    private string GuardOneId;
    private string GuardTwoId;
    private BaseUnitEntity Boss;
    private BaseUnitEntity GuardOne;
    private BaseUnitEntity GuardTwo;
    private int GroupDeaths;
    private bool BossDead;
    private bool GuardOneDead;
    private bool GuardTwoDead;

    public string Id => MechanicId;
    public string DisplayName => HudTitle;
    public string Description => HudDescription;

    public bool CanActivate(EncounterSession session) {
        if (session == null ||
            !session.SupportsEncounterType(
                EncounterType.Boss) ||
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
        if (Active) {
            throw new InvalidOperationException(
                "The Elite Guard is already active.");
        }
        if (!session.SupportsEncounterType(
                EncounterType.Boss)) {
            throw new InvalidOperationException(
                "The Elite Guard requires Boss encounter eligibility.");
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

        string bossId = leader.UniqueId;
        string guardOneId = guardOne.UniqueId;
        string guardTwoId = guardTwo.UniqueId;
        if (!AreValidDistinctIds(
                bossId,
                guardOneId,
                guardTwoId)) {
            throw new InvalidOperationException(
                "The Elite Guard requires three distinct persistent member IDs.");
        }

        BossId = bossId;
        GuardOneId = guardOneId;
        GuardTwoId = guardTwoId;
        Boss = leader;
        GuardOne = guardOne;
        GuardTwo = guardTwo;
        GroupDeaths = 0;
        BossDead = false;
        GuardOneDead = false;
        GuardTwoDead = false;
        Active = true;

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
        if (!Active || unit == null) {
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
        Active = false;
        BossId = null;
        GuardOneId = null;
        GuardTwoId = null;
        Boss = null;
        GuardOne = null;
        GuardTwo = null;
        GroupDeaths = 0;
        BossDead = false;
        GuardOneDead = false;
        GuardTwoDead = false;
    }

    bool IPersistableEncounterMechanic.TryCaptureSaveData(
        EncounterMechanicSaveData saveData,
        out string failureReason) {
        failureReason = null;
        if (saveData == null) {
            failureReason =
                "The Elite Guard save-data container is unavailable.";
            return false;
        }
        if (saveData.EliteGuard != null) {
            failureReason =
                "The Elite Guard save-data container is already populated.";
            return false;
        }
        if (!Active) {
            failureReason =
                "The Elite Guard is not active for save capture.";
            return false;
        }
        if (!AreValidDistinctIds(
                BossId,
                GuardOneId,
                GuardTwoId)) {
            failureReason =
                "The Elite Guard has invalid or duplicated persistent member IDs.";
            return false;
        }

        int expectedGroupDeaths =
            (BossDead ? 1 : 0) +
            (GuardOneDead ? 1 : 0) +
            (GuardTwoDead ? 1 : 0);
        if (GroupDeaths < 0 ||
            GroupDeaths > 3 ||
            GroupDeaths != expectedGroupDeaths) {
            failureReason =
                "The Elite Guard death count does not match its member death flags.";
            return false;
        }
        if (!IsCaptureSlotValid(
                Boss,
                BossId,
                BossDead)) {
            failureReason =
                "The Elite Guard Boss slot is inconsistent with its saved identity and death state.";
            return false;
        }
        if (!IsCaptureSlotValid(
                GuardOne,
                GuardOneId,
                GuardOneDead)) {
            failureReason =
                "The Elite Guard first Guard slot is inconsistent with its saved identity and death state.";
            return false;
        }
        if (!IsCaptureSlotValid(
                GuardTwo,
                GuardTwoId,
                GuardTwoDead)) {
            failureReason =
                "The Elite Guard second Guard slot is inconsistent with its saved identity and death state.";
            return false;
        }

        saveData.EliteGuard =
            new EliteGuardSaveRecipe {
                BossId = BossId,
                GuardOneId = GuardOneId,
                GuardTwoId = GuardTwoId
            };
        return true;
    }

    bool IPersistableEncounterMechanic.TryRestoreFromSave(
        EncounterRestoreContext context,
        EncounterMechanicSaveData saveData,
        out string failureReason) {
        failureReason = null;
        if (Active ||
            Boss != null ||
            GuardOne != null ||
            GuardTwo != null ||
            BossId != null ||
            GuardOneId != null ||
            GuardTwoId != null ||
            GroupDeaths != 0 ||
            BossDead ||
            GuardOneDead ||
            GuardTwoDead) {
            failureReason =
                "The Elite Guard is already active or retains runtime state.";
            return false;
        }
        if (context == null) {
            failureReason =
                "The Elite Guard restore context is unavailable.";
            return false;
        }
        if (saveData == null) {
            failureReason =
                "The Elite Guard save-data container is unavailable.";
            return false;
        }

        EliteGuardSaveRecipe recipe =
            saveData.EliteGuard;
        if (recipe == null) {
            failureReason =
                "The Elite Guard save recipe is missing.";
            return false;
        }
        if (!AreValidDistinctIds(
                recipe.BossId,
                recipe.GuardOneId,
                recipe.GuardTwoId)) {
            failureReason =
                "The Elite Guard saved member IDs are invalid or duplicated.";
            return false;
        }

        bool bossResolved = context.TryResolveEnemy(
            recipe.BossId,
            requireLiving: false,
            out BaseUnitEntity boss);
        bool guardOneResolved = context.TryResolveEnemy(
            recipe.GuardOneId,
            requireLiving: false,
            out BaseUnitEntity guardOne);
        bool guardTwoResolved = context.TryResolveEnemy(
            recipe.GuardTwoId,
            requireLiving: false,
            out BaseUnitEntity guardTwo);

        if ((bossResolved &&
             guardOneResolved &&
             ReferenceEquals(boss, guardOne)) ||
            (bossResolved &&
             guardTwoResolved &&
             ReferenceEquals(boss, guardTwo)) ||
            (guardOneResolved &&
             guardTwoResolved &&
             ReferenceEquals(guardOne, guardTwo))) {
            failureReason =
                "The Elite Guard saved member IDs resolved to aliased entity references.";
            return false;
        }

        bool bossDead =
            !bossResolved ||
            !IsLivingMember(boss);
        bool guardOneDead =
            !guardOneResolved ||
            !IsLivingMember(guardOne);
        bool guardTwoDead =
            !guardTwoResolved ||
            !IsLivingMember(guardTwo);
        int groupDeaths =
            (bossDead ? 1 : 0) +
            (guardOneDead ? 1 : 0) +
            (guardTwoDead ? 1 : 0);
        int incomingReduction =
            groupDeaths == 0
                ? InitialIncomingReduction
                : groupDeaths == 1
                    ? WeakenedIncomingReduction
                    : 0;
        bool showHud =
            !guardOneDead ||
            !guardTwoDead;

        BossId = recipe.BossId;
        GuardOneId = recipe.GuardOneId;
        GuardTwoId = recipe.GuardTwoId;
        Boss = boss;
        GuardOne = guardOne;
        GuardTwo = guardTwo;
        BossDead = bossDead;
        GuardOneDead = guardOneDead;
        GuardTwoDead = guardTwoDead;
        GroupDeaths = groupDeaths;
        Active = true;

        if (!bossDead) {
            if (incomingReduction > 0) {
                DamageControl.SetIncomingDamageReduction(
                    boss,
                    incomingReduction);
            }
            UnitMarker.SetMarker(
                boss,
                BossMarker,
                ChaosColors.Red);
        }
        if (!guardOneDead) {
            if (incomingReduction > 0) {
                DamageControl.SetIncomingDamageReduction(
                    guardOne,
                    incomingReduction);
            }
            DamageControl.SetOutgoingDamageIncrease(
                guardOne,
                GuardOutgoingIncrease);
            UnitMarker.SetMarker(
                guardOne,
                GuardMarker,
                ChaosColors.Orange);
        }
        if (!guardTwoDead) {
            if (incomingReduction > 0) {
                DamageControl.SetIncomingDamageReduction(
                    guardTwo,
                    incomingReduction);
            }
            DamageControl.SetOutgoingDamageIncrease(
                guardTwo,
                GuardOutgoingIncrease);
            UnitMarker.SetMarker(
                guardTwo,
                GuardMarker,
                ChaosColors.Orange);
        }

        if (showHud) {
            EncounterHud.Show(
                HudTitle,
                HudDescription);
        } else {
            EncounterHud.Hide();
        }

        Main.LogInfo(
            $"The Elite Guard restored: " +
            $"BossAlive={!bossDead} " +
            $"GuardOneAlive={!guardOneDead} " +
            $"GuardTwoAlive={!guardTwoDead} " +
            $"GroupDeaths={groupDeaths} " +
            $"IncomingReduction={incomingReduction}");
        return true;
    }

    private static bool IsCaptureSlotValid(
        BaseUnitEntity unit,
        string id,
        bool dead) {
        if (unit != null &&
            !string.Equals(
                unit.UniqueId,
                id,
                StringComparison.Ordinal)) {
            return false;
        }

        return dead
            ? !IsLivingMember(unit)
            : unit != null && IsLivingMember(unit);
    }

    private static bool IsLivingMember(
        BaseUnitEntity unit) {
        return unit != null &&
               unit is not StarshipEntity &&
               !unit.IsDisposed &&
               unit.IsInGame &&
               unit.IsInCombat &&
               unit.IsPlayerEnemy &&
               unit.LifeState != null &&
               !unit.LifeState.IsDead &&
               !unit.LifeState.IsFinallyDead;
    }

    private static bool AreValidDistinctIds(
        string bossId,
        string guardOneId,
        string guardTwoId) {
        return EncounterPersistenceValidation
                   .IsValidEntityId(bossId) &&
               EncounterPersistenceValidation
                   .IsValidEntityId(guardOneId) &&
               EncounterPersistenceValidation
                   .IsValidEntityId(guardTwoId) &&
               !string.Equals(
                   bossId,
                   guardOneId,
                   StringComparison.Ordinal) &&
               !string.Equals(
                   bossId,
                   guardTwoId,
                   StringComparison.Ordinal) &&
               !string.Equals(
                   guardOneId,
                   guardTwoId,
                   StringComparison.Ordinal);
    }
}
