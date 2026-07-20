using System;
using System.Collections.Generic;
using ChaosEncounters.UI;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Controllers.Units;
using Kingmaker.Designers;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.PubSubSystem.Core;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Parts;
using UnityEngine;

namespace ChaosEncounters.Combat.Mechanics.Common;

internal sealed class EqualizerMechanic :
    IEncounterMechanic {
    private const string MechanicId = "Equalizer";
    private const string HudTitle = "The Equalizer";
    private const string HudDescription =
        "All enemies share a single health pool. Damage dealt to any enemy is redistributed across the group, prioritizing those with the highest remaining health percentage. No enemy can fall below 1 HP until the shared pool is depleted. For every 2% of maximum pool health lost, all enemies gain +1% damage dealt and 1% damage reduction. When the pool reaches 0%, every remaining enemy dies.";
    private const string HarmonyId = "ChaosEncounters.Equalizer";
    private const long MaximumSafePool = long.MaxValue / 101L;

    private static EqualizerMechanic ActiveInstance;
    private static bool HooksInstalled;

    private List<MemberState> Members;
    private MemberState[] WorkBuffer;
    private long MaximumPool;
    private long CurrentPool;
    private int NextRegistrationOrdinal;
    private int DisplayedPercent;
    private int AppliedBonusPercent;
    private int ExternalMutationDepth;
    private int InternalSynchronizationDepth;
    private int CollapseDepth;
    private int DeactivationDepth;
    private bool Faulted;
    private bool FailureLogged;
    private bool ExternalRemovalWarningLogged;

    public string Id => MechanicId;
    public string DisplayName => HudTitle;
    public string Description => HudDescription;

    public bool CanActivate(EncounterSession session) {
        if (session == null ||
            session.Type != EncounterType.Common ||
            session.InitialEnemies.Count == 0) {
            return false;
        }

        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            if (!TryGetEligibleHealth(
                    session.InitialEnemies[index],
                    out _,
                    out _)) {
                return false;
            }
        }

        return true;
    }

    public void Activate(EncounterSession session) {
        if (session == null) {
            throw new InvalidOperationException(
                "The Equalizer requires an encounter session.");
        }
        if (Members != null ||
            ReferenceEquals(ActiveInstance, this)) {
            throw new InvalidOperationException(
                "The Equalizer is already active.");
        }
        if (session.Type != EncounterType.Common) {
            throw new InvalidOperationException(
                "The Equalizer requires a Common encounter.");
        }

        var members = new List<MemberState>(
            session.InitialEnemies.Count);
        long maximumPool = 0;
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity unit =
                session.InitialEnemies[index];
            if (!TryGetEligibleHealth(
                    unit,
                    out PartHealth health,
                    out string failureReason)) {
                throw new InvalidOperationException(
                    $"The Equalizer cannot register an initial enemy: " +
                    $"Index={index}, Reason={failureReason}");
            }

            maximumPool = checked(
                maximumPool + health.MaxHitPoints);
            if (maximumPool > MaximumSafePool) {
                throw new InvalidOperationException(
                    "The Equalizer maximum pool is too large for safe percentage arithmetic.");
            }

            members.Add(
                new MemberState(
                    this,
                    unit,
                    health,
                    health.MaxHitPoints,
                    index));
        }
        if (members.Count == 0 || maximumPool <= 0) {
            throw new InvalidOperationException(
                "The Equalizer requires a valid positive initial pool.");
        }

        EnsureHooksInstalled();

        Members = members;
        MaximumPool = maximumPool;
        CurrentPool = maximumPool;
        NextRegistrationOrdinal = members.Count;
        DisplayedPercent = -1;
        AppliedBonusPercent = -1;
        ExternalMutationDepth = 0;
        InternalSynchronizationDepth = 0;
        CollapseDepth = 0;
        DeactivationDepth = 0;
        Faulted = false;
        FailureLogged = false;
        ExternalRemovalWarningLogged = false;
        EnsureWorkBufferCapacity(members.Count);

        for (int index = 0;
             index < members.Count;
             index++) {
            members[index].SubscribeToMaximumHitPoints();
        }

        ActiveInstance = this;
        try {
            SynchronizeGroup(
                PoolChangeDirection.Healing,
                rosterChanged: true);
            EncounterHud.Show(
                HudTitle,
                HudDescription);
            Main.LogInfo(
                $"The Equalizer activated: MemberCount={Members.Count} " +
                $"MaximumPool={MaximumPool}");
        } catch {
            ActiveInstance = null;
            CleanupOwnedState();
            throw;
        }
    }

    public void HandleRoundStart(int combatRound) {
        if (!IsOperational) {
            return;
        }

        RemoveInvalidMembers();
        if (!IsOperational) {
            return;
        }

        Game game = Game.Instance;
        if (game?.State == null) {
            FailActive(
                "The Equalizer could not inspect round-start reinforcements because game state was unavailable.",
                null);
            return;
        }

        bool rosterChanged = false;
        try {
            foreach (BaseUnitEntity candidate in
                game.State.AllBaseAwakeUnitsForSure) {
                rosterChanged |= TryRegisterReinforcement(
                    candidate);
            }

            if (rosterChanged && IsOperational) {
                SynchronizeGroup(
                    PoolChangeDirection.Healing,
                    rosterChanged: true);
            }
        } catch (Exception exception) {
            FailActive(
                "The Equalizer failed while registering round-start reinforcements.",
                exception);
        }
    }

    public void HandleRoundEnd(int combatRound) {
    }

    public void HandleUnitTurnStart(
        BaseUnitEntity unit,
        int combatRound) {
        if (!IsOperational) {
            return;
        }

        try {
            if (TryRegisterReinforcement(unit)) {
                SynchronizeGroup(
                    PoolChangeDirection.Healing,
                    rosterChanged: true);
            }
        } catch (Exception exception) {
            FailActive(
                "The Equalizer failed while registering a turn-start reinforcement.",
                exception);
        }
    }

    public void HandleUnitTurnEnd(
        BaseUnitEntity unit,
        int combatRound) {
    }

    public void HandleEnemyDeath(
        BaseUnitEntity unit,
        int combatRound) {
        if (!IsOperational ||
            CollapseDepth > 0 ||
            unit == null) {
            return;
        }

        int memberIndex = FindMemberIndex(unit);
        if (memberIndex < 0) {
            return;
        }

        try {
            RemoveMemberAt(
                memberIndex,
                adjustPool: true,
                logExternalRemoval: true);
            ContinueAfterRosterLoss();
        } catch (Exception exception) {
            FailActive(
                "The Equalizer failed while removing an externally dead member.",
                exception);
        }
    }

    public void Deactivate(
        EncounterMechanicEndReason reason) {
        DeactivationDepth++;
        try {
            if (ReferenceEquals(ActiveInstance, this)) {
                ActiveInstance = null;
            }
            CleanupOwnedState();
        } finally {
            InternalSynchronizationDepth = 0;
            CollapseDepth = 0;
            DeactivationDepth--;
        }
    }

    private bool IsOperational =>
        ReferenceEquals(ActiveInstance, this) &&
        Members != null &&
        !Faulted &&
        DeactivationDepth == 0;

    private static void EnsureHooksInstalled() {
        if (HooksInstalled) {
            return;
        }

        var harmony = new Harmony(HarmonyId);
        harmony
            .CreateClassProcessor(
                typeof(PartHealthSetDamagePatch))
            .Patch();
        harmony
            .CreateClassProcessor(
                typeof(RuleDealDamageOnTriggerPatch))
            .Patch();
        HooksInstalled = true;
    }

    private static bool TryGetEligibleHealth(
        BaseUnitEntity unit,
        out PartHealth health,
        out string failureReason) {
        health = null;
        if (unit == null) {
            failureReason = "Unit is null.";
            return false;
        }
        if (unit is StarshipEntity) {
            failureReason = "Unit is a starship.";
            return false;
        }
        if (unit.IsDisposed) {
            failureReason = "Unit is disposed.";
            return false;
        }
        if (!unit.IsInGame) {
            failureReason = "Unit is outside the game.";
            return false;
        }
        if (!unit.IsInCombat) {
            failureReason = "Unit is outside combat.";
            return false;
        }
        if (!unit.IsPlayerEnemy) {
            failureReason = "Unit is not hostile to the player.";
            return false;
        }
        if (unit.LifeState == null ||
            !unit.LifeState.IsConscious ||
            unit.LifeState.IsDead ||
            unit.LifeState.IsFinallyDead ||
            unit.LifeState.MarkedForDeath ||
            unit.LifeState.ScriptedKill) {
            failureReason = "Unit is not a valid living member.";
            return false;
        }

        health = unit.GetHealthOptional();
        if (health == null) {
            failureReason = "Unit has no health part.";
            return false;
        }
        if (health.MaxHitPoints <= 0) {
            failureReason = "Unit has nonpositive maximum hit points.";
            return false;
        }
        if (health.MinHitPoints > 1) {
            failureReason =
                $"Unit has unsupported MinHitPoints={health.MinHitPoints}.";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool IsPotentialReinforcement(
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

    private bool TryRegisterReinforcement(
        BaseUnitEntity unit) {
        if (!IsOperational ||
            unit == null ||
            FindMemberIndex(unit) >= 0) {
            return false;
        }
        if (!TryGetEligibleHealth(
                unit,
                out PartHealth health,
                out string failureReason)) {
            if (IsPotentialReinforcement(unit)) {
                throw new InvalidOperationException(
                    $"The Equalizer rejected an incompatible reinforcement: " +
                    $"Unit={unit.CharacterName}, Reason={failureReason}");
            }
            return false;
        }

        long newMaximumPool = checked(
            MaximumPool + health.MaxHitPoints);
        if (newMaximumPool > MaximumSafePool) {
            throw new InvalidOperationException(
                "The Equalizer reinforcement would exceed safe percentage arithmetic.");
        }
        long newCurrentPool = checked(
            CurrentPool + health.MaxHitPoints);

        var member = new MemberState(
            this,
            unit,
            health,
            health.MaxHitPoints,
            NextRegistrationOrdinal);
        NextRegistrationOrdinal++;
        Members.Add(member);
        EnsureWorkBufferCapacity(Members.Count);
        member.SubscribeToMaximumHitPoints();
        MaximumPool = newMaximumPool;
        CurrentPool = newCurrentPool;
        return true;
    }

    private int FindMemberIndex(BaseUnitEntity unit) {
        List<MemberState> members = Members;
        if (members == null || unit == null) {
            return -1;
        }

        for (int index = 0;
             index < members.Count;
             index++) {
            if (ReferenceEquals(
                    members[index].Unit,
                    unit)) {
                return index;
            }
        }

        return -1;
    }

    private MemberState FindMember(PartHealth health) {
        List<MemberState> members = Members;
        if (members == null || health == null) {
            return null;
        }

        for (int index = 0;
             index < members.Count;
             index++) {
            MemberState member = members[index];
            if (ReferenceEquals(member.Health, health)) {
                return member;
            }
        }

        return null;
    }

    private void EnsureRuleParticipantsRegistered(
        RuleDealDamage rule) {
        if (!IsOperational || rule == null) {
            return;
        }

        bool rosterChanged = false;
        if (rule.Initiator is BaseUnitEntity initiator) {
            rosterChanged |= TryRegisterReinforcement(
                initiator);
        }
        if (rule.Target is BaseUnitEntity target &&
            !ReferenceEquals(target, rule.Initiator)) {
            rosterChanged |= TryRegisterReinforcement(
                target);
        }

        if (rosterChanged) {
            SynchronizeGroup(
                PoolChangeDirection.Healing,
                rosterChanged: true);
        }
    }

    private HealthMutationState BeginHealthMutation(
        PartHealth health,
        int requestedDamage) {
        if (!IsOperational ||
            InternalSynchronizationDepth > 0 ||
            CollapseDepth > 0 ||
            DeactivationDepth > 0) {
            return default;
        }
        if (ExternalMutationDepth > 0) {
            throw new InvalidOperationException(
                "The Equalizer detected a nested external health mutation.");
        }

        MemberState member = FindMember(health);
        if (member == null ||
            member.Unit == null ||
            member.Unit.IsDisposed ||
            member.Unit.LifeState == null ||
            member.Unit.LifeState.IsDead ||
            !ReferenceEquals(
                member.Unit.GetHealthOptional(),
                health)) {
            return default;
        }

        int oldDamage = health.Damage;
        int oldHitPoints = health.HitPointsLeft;
        if (oldHitPoints != member.LastSynchronizedRealHP) {
            throw new InvalidOperationException(
                $"The Equalizer detected unsynchronized member health before mutation: " +
                $"Unit={member.Unit.CharacterName}, " +
                $"Expected={member.LastSynchronizedRealHP}, " +
                $"Actual={oldHitPoints}.");
        }

        ExternalMutationDepth++;
        return new HealthMutationState(
            this,
            member,
            health,
            oldDamage,
            oldHitPoints,
            requestedDamage,
            externalGuardEntered: true);
    }

    private void HandleCompletedHealthMutation(
        HealthMutationState state) {
        if (!state.ShouldProcess ||
            !ReferenceEquals(state.Owner, this) ||
            !IsOperational ||
            !ReferenceEquals(state.Member.Health, state.Health) ||
            FindMemberIndex(state.Member.Unit) < 0) {
            return;
        }

        if (state.RequestedDamage > state.OldDamage) {
            long attemptedRealDamage =
                (long)state.RequestedDamage -
                state.OldDamage;
            CurrentPool = attemptedRealDamage >= CurrentPool
                ? 0
                : CurrentPool - attemptedRealDamage;
            if (CurrentPool == 0) {
                CollapseGroup();
                return;
            }

            SynchronizeGroup(
                PoolChangeDirection.Damage,
                rosterChanged: false);
            return;
        }

        if (state.RequestedDamage < state.OldDamage) {
            int newDamage = state.Health.Damage;
            long actualHealing =
                (long)state.OldDamage - newDamage;
            if (actualHealing <= 0) {
                state.Member.LastSynchronizedRealHP =
                    state.Health.HitPointsLeft;
                VerifyInvariant();
                return;
            }

            long healedPool = checked(
                CurrentPool + actualHealing);
            CurrentPool = healedPool < MaximumPool
                ? healedPool
                : MaximumPool;
            SynchronizeGroup(
                PoolChangeDirection.Healing,
                rosterChanged: false);
        }
    }

    private void EndHealthMutation(
        HealthMutationState state) {
        if (!state.ExternalGuardEntered ||
            !ReferenceEquals(state.Owner, this)) {
            return;
        }

        if (ExternalMutationDepth > 0) {
            ExternalMutationDepth--;
        }
    }

    private void SynchronizeGroup(
        PoolChangeDirection direction,
        bool rosterChanged) {
        if (!IsOperational || Members.Count == 0) {
            return;
        }
        if (CurrentPool <= 0) {
            CollapseGroup();
            return;
        }

        long visibleTarget = CurrentPool >= Members.Count
            ? CurrentPool
            : Members.Count;
        if (visibleTarget > MaximumPool) {
            throw new InvalidOperationException(
                $"The Equalizer visible target exceeds pool capacity: " +
                $"Target={visibleTarget}, Maximum={MaximumPool}.");
        }

        PrepareAllocation(
            direction,
            visibleTarget);

        InternalSynchronizationDepth++;
        try {
            for (int index = 0;
                 index < Members.Count;
                 index++) {
                MemberState member = Members[index];
                if (!IsMemberSynchronizable(member)) {
                    throw new InvalidOperationException(
                        $"The Equalizer member became invalid during synchronization: " +
                        $"Unit={member.Unit?.CharacterName ?? "<null>"}.");
                }

                int targetHitPoints =
                    member.PlannedHitPoints;
                if (member.Health.HitPointsLeft !=
                    targetHitPoints) {
                    member.Health.SetHitPointsLeft(
                        targetHitPoints);
                }
                member.LastSynchronizedRealHP =
                    member.Health.HitPointsLeft;
            }
        } finally {
            InternalSynchronizationDepth--;
        }

        VerifyInvariant();
        UpdatePresentation(rosterChanged);
    }

    private void PrepareAllocation(
        PoolChangeDirection direction,
        long visibleTarget) {
        long lowerSum = 0;
        long upperSum = 0;
        long activeMaximumSum = 0;

        for (int index = 0;
             index < Members.Count;
             index++) {
            MemberState member = Members[index];
            int current = member.LastSynchronizedRealHP;
            if (current < 1) {
                current = 1;
            }
            if (current > member.RegisteredMaximumHitPoints) {
                current = member.RegisteredMaximumHitPoints;
            }

            if (direction == PoolChangeDirection.Damage) {
                member.AllocationLowerBound = 1;
                member.AllocationUpperBound = current;
            } else {
                member.AllocationLowerBound = current;
                member.AllocationUpperBound =
                    member.RegisteredMaximumHitPoints;
            }
            member.AllocationActive = true;
            member.AllocationRemainder = 0;
            member.RemainderAwarded = false;
            lowerSum = checked(
                lowerSum + member.AllocationLowerBound);
            upperSum = checked(
                upperSum + member.AllocationUpperBound);
            activeMaximumSum = checked(
                activeMaximumSum +
                member.RegisteredMaximumHitPoints);
        }

        if (visibleTarget < lowerSum ||
            visibleTarget > upperSum) {
            throw new InvalidOperationException(
                $"The Equalizer allocation is incompatible with monotonic synchronization: " +
                $"Direction={direction}, Target={visibleTarget}, " +
                $"Lower={lowerSum}, Upper={upperSum}.");
        }

        long fixedSum = 0;
        while (activeMaximumSum > 0) {
            long available = checked(
                visibleTarget - fixedSum);
            MemberState violation = null;
            int fixedValue = 0;

            for (int index = 0;
                 index < Members.Count;
                 index++) {
                MemberState member = Members[index];
                if (!member.AllocationActive) {
                    continue;
                }

                long weightedAvailable = checked(
                    available *
                    member.RegisteredMaximumHitPoints);
                long weightedLower = checked(
                    (long)member.AllocationLowerBound *
                    activeMaximumSum);
                if (weightedAvailable < weightedLower) {
                    violation = member;
                    fixedValue =
                        member.AllocationLowerBound;
                    break;
                }

                long weightedUpper = checked(
                    (long)member.AllocationUpperBound *
                    activeMaximumSum);
                if (weightedAvailable > weightedUpper) {
                    violation = member;
                    fixedValue =
                        member.AllocationUpperBound;
                    break;
                }
            }

            if (violation == null) {
                break;
            }

            violation.AllocationActive = false;
            violation.PlannedHitPoints = fixedValue;
            fixedSum = checked(
                fixedSum + fixedValue);
            activeMaximumSum -=
                violation.RegisteredMaximumHitPoints;
        }

        if (activeMaximumSum == 0) {
            if (fixedSum != visibleTarget) {
                throw new InvalidOperationException(
                    "The Equalizer allocation exhausted all members before reaching its target.");
            }
            return;
        }

        long remainingTarget = checked(
            visibleTarget - fixedSum);
        long allocated = fixedSum;
        for (int index = 0;
             index < Members.Count;
             index++) {
            MemberState member = Members[index];
            if (!member.AllocationActive) {
                continue;
            }

            long weightedTarget = checked(
                remainingTarget *
                member.RegisteredMaximumHitPoints);
            int planned = checked((int)(
                weightedTarget / activeMaximumSum));
            if (planned < member.AllocationLowerBound) {
                planned = member.AllocationLowerBound;
            }
            if (planned > member.AllocationUpperBound) {
                planned = member.AllocationUpperBound;
            }

            member.PlannedHitPoints = planned;
            member.AllocationRemainder =
                weightedTarget % activeMaximumSum;
            allocated = checked(allocated + planned);
        }

        long remainderPoints = checked(
            visibleTarget - allocated);
        if (remainderPoints < 0 ||
            remainderPoints > Members.Count) {
            throw new InvalidOperationException(
                $"The Equalizer produced an invalid integer remainder: {remainderPoints}.");
        }

        while (remainderPoints > 0) {
            MemberState selected = null;
            for (int index = 0;
                 index < Members.Count;
                 index++) {
                MemberState candidate = Members[index];
                if (!candidate.AllocationActive ||
                    candidate.RemainderAwarded ||
                    candidate.PlannedHitPoints >=
                        candidate.AllocationUpperBound) {
                    continue;
                }
                if (selected == null ||
                    candidate.AllocationRemainder >
                        selected.AllocationRemainder ||
                    (candidate.AllocationRemainder ==
                         selected.AllocationRemainder &&
                     candidate.RegistrationOrdinal <
                         selected.RegistrationOrdinal)) {
                    selected = candidate;
                }
            }

            if (selected == null) {
                throw new InvalidOperationException(
                    "The Equalizer could not distribute its integer remainder.");
            }

            selected.PlannedHitPoints++;
            selected.RemainderAwarded = true;
            remainderPoints--;
        }
    }

    private void VerifyInvariant() {
        if (!IsOperational || Members.Count == 0) {
            return;
        }

        long visibleSum = 0;
        for (int index = 0;
             index < Members.Count;
             index++) {
            MemberState member = Members[index];
            if (!IsMemberSynchronizable(member)) {
                throw new InvalidOperationException(
                    $"The Equalizer invariant found an invalid member: " +
                    $"Unit={member.Unit?.CharacterName ?? "<null>"}.");
            }
            if (member.Health.MaxHitPoints !=
                member.RegisteredMaximumHitPoints) {
                throw new InvalidOperationException(
                    $"The Equalizer detected a maximum-HP change during verification: " +
                    $"Unit={member.Unit.CharacterName}.");
            }
            if (member.Health.MinHitPoints > 1) {
                throw new InvalidOperationException(
                    $"The Equalizer detected MinHitPoints above 1 during verification: " +
                    $"Unit={member.Unit.CharacterName}, " +
                    $"MinHitPoints={member.Health.MinHitPoints}.");
            }

            int visibleHitPoints =
                member.Health.HitPointsLeft;
            if (visibleHitPoints !=
                member.PlannedHitPoints) {
                throw new InvalidOperationException(
                    $"The Equalizer assignment was clamped or changed: " +
                    $"Unit={member.Unit.CharacterName}, " +
                    $"Expected={member.PlannedHitPoints}, " +
                    $"Actual={visibleHitPoints}.");
            }
            visibleSum = checked(
                visibleSum + visibleHitPoints);
        }

        if (CurrentPool >= Members.Count) {
            if (visibleSum != CurrentPool) {
                throw new InvalidOperationException(
                    $"The Equalizer visible sum does not match the pool: " +
                    $"Visible={visibleSum}, Pool={CurrentPool}.");
            }
            return;
        }

        if (CurrentPool > 0) {
            for (int index = 0;
                 index < Members.Count;
                 index++) {
                if (Members[index].Health.HitPointsLeft != 1) {
                    throw new InvalidOperationException(
                        "The Equalizer 1-HP floor invariant failed.");
                }
            }
        }
    }

    private bool IsMemberSynchronizable(
        MemberState member) {
        BaseUnitEntity unit = member?.Unit;
        return unit != null &&
               member.Health != null &&
               unit is not StarshipEntity &&
               !unit.IsDisposed &&
               unit.IsInGame &&
               unit.IsInCombat &&
               unit.IsPlayerEnemy &&
               unit.LifeState != null &&
               !unit.LifeState.IsDead &&
               !unit.LifeState.IsFinallyDead &&
               ReferenceEquals(
                   unit.GetHealthOptional(),
                   member.Health);
    }

    private void UpdatePresentation(
        bool rosterChanged) {
        if (!IsOperational || CurrentPool <= 0) {
            return;
        }

        int remainingPercent =
            CalculateRemainingPercent();
        if (rosterChanged ||
            remainingPercent != DisplayedPercent) {
            string markerText =
                remainingPercent.ToString() + "%";
            Color32 markerColor =
                GetMarkerColor(remainingPercent);
            for (int index = 0;
                 index < Members.Count;
                 index++) {
                UnitMarker.SetMarker(
                    Members[index].Unit,
                    markerText,
                    markerColor);
            }
            DisplayedPercent = remainingPercent;
        }

        int bonusPercent =
            (100 - remainingPercent) / 2;
        if (rosterChanged ||
            bonusPercent != AppliedBonusPercent) {
            for (int index = 0;
                 index < Members.Count;
                 index++) {
                BaseUnitEntity unit =
                    Members[index].Unit;
                DamageControl.SetOutgoingDamageIncrease(
                    unit,
                    bonusPercent);
                DamageControl.SetIncomingDamageReduction(
                    unit,
                    bonusPercent);
            }
            AppliedBonusPercent = bonusPercent;
        }
    }

    private int CalculateRemainingPercent() {
        if (CurrentPool <= 0 || MaximumPool <= 0) {
            return 0;
        }

        long numerator = checked(
            CurrentPool * 100L +
            MaximumPool - 1L);
        long percent = numerator / MaximumPool;
        if (percent < 0) {
            return 0;
        }
        if (percent > 100) {
            return 100;
        }
        return (int)percent;
    }

    private static Color32 GetMarkerColor(
        int remainingPercent) {
        if (remainingPercent >= 76) {
            return ChaosColors.Green;
        }
        if (remainingPercent >= 51) {
            return ChaosColors.Yellow;
        }
        if (remainingPercent >= 26) {
            return ChaosColors.Orange;
        }
        return ChaosColors.Red;
    }

    private void RemoveInvalidMembers() {
        if (!IsOperational) {
            return;
        }

        bool removed = false;
        for (int index = Members.Count - 1;
             index >= 0;
             index--) {
            MemberState member = Members[index];
            BaseUnitEntity unit = member.Unit;
            bool externallyRemoved =
                unit == null ||
                unit.IsDisposed ||
                !unit.IsInGame ||
                !unit.IsInCombat ||
                !unit.IsPlayerEnemy ||
                unit.LifeState == null ||
                unit.LifeState.IsDead ||
                unit.LifeState.IsFinallyDead;
            if (!externallyRemoved) {
                if (!ReferenceEquals(
                        unit.GetHealthOptional(),
                        member.Health) ||
                    member.Health.MaxHitPoints !=
                        member.RegisteredMaximumHitPoints ||
                    member.Health.MinHitPoints > 1) {
                    throw new InvalidOperationException(
                        $"The Equalizer detected incompatible member health state: " +
                        $"Unit={unit.CharacterName}.");
                }
                continue;
            }

            RemoveMemberAt(
                index,
                adjustPool: true,
                logExternalRemoval: true);
            removed = true;
        }

        if (!removed) {
            return;
        }
        ContinueAfterRosterLoss();
    }

    private void RemoveMemberAt(
        int index,
        bool adjustPool,
        bool logExternalRemoval) {
        MemberState member = Members[index];
        member.UnsubscribeFromMaximumHitPoints();
        Members.RemoveAt(index);
        DamageControl.ClearPolicy(member.Unit);
        UnitMarker.ClearMarker(member.Unit);

        if (adjustPool) {
            MaximumPool -=
                member.RegisteredMaximumHitPoints;
            if (MaximumPool < 0) {
                MaximumPool = 0;
            }

            long contribution =
                member.LastSynchronizedRealHP;
            CurrentPool = contribution >= CurrentPool
                ? 0
                : CurrentPool - contribution;
            if (CurrentPool > MaximumPool) {
                CurrentPool = MaximumPool;
            }
        }

        if (logExternalRemoval &&
            !ExternalRemovalWarningLogged) {
            ExternalRemovalWarningLogged = true;
            Main.LogWarning(
                "The Equalizer removed an externally dead or unavailable member and recalculated the shared pool.");
        }
    }

    private void ContinueAfterRosterLoss() {
        if (!IsOperational) {
            return;
        }
        if (Members.Count == 0) {
            CurrentPool = 0;
            MaximumPool = 0;
            DisableAfterCompletion();
            return;
        }
        if (CurrentPool <= 0) {
            CurrentPool = 0;
            CollapseGroup();
            return;
        }

        SynchronizeGroup(
            PoolChangeDirection.Damage,
            rosterChanged: true);
    }

    private void CollapseGroup() {
        if (!IsOperational || CollapseDepth > 0) {
            return;
        }

        CurrentPool = 0;
        int collapseCount = Members.Count;
        EnsureWorkBufferCapacity(collapseCount);
        MemberState[] buffer = WorkBuffer;
        for (int index = 0;
             index < collapseCount;
             index++) {
            buffer[index] = Members[index];
        }

        CollapseDepth++;
        try {
            for (int index = 0;
                 index < collapseCount;
                 index++) {
                BaseUnitEntity unit = buffer[index].Unit;
                if (unit == null ||
                    unit.IsDisposed ||
                    unit.LifeState == null ||
                    unit.LifeState.IsDead ||
                    unit.LifeState.IsFinallyDead) {
                    continue;
                }

                GameHelper.KillUnit(
                    unit,
                    null,
                    0);
                UnitLifeController.ForceTickOnUnit(unit);
            }

            for (int index = 0;
                 index < collapseCount;
                 index++) {
                BaseUnitEntity unit = buffer[index].Unit;
                if (unit != null &&
                    !unit.IsDisposed &&
                    (unit.LifeState == null ||
                     (!unit.LifeState.IsDead &&
                      !unit.LifeState.IsFinallyDead))) {
                    throw new InvalidOperationException(
                        $"The Equalizer collapse left a surviving member: " +
                        $"Unit={unit.CharacterName}, " +
                        $"HitPoints={unit.GetHealthOptional()?.HitPointsLeft}, " +
                        $"LifeState={unit.LifeState?.State}.");
                }
            }
        } finally {
            for (int index = 0;
                 index < collapseCount;
                 index++) {
                buffer[index] = null;
            }
            CollapseDepth--;
        }

        DisableAfterCompletion();
    }

    private void DisableAfterCompletion() {
        if (!ReferenceEquals(ActiveInstance, this)) {
            return;
        }

        ActiveInstance = null;
        EncounterMechanicController
            .DisableActiveMechanicForCurrentCombat();
    }

    private void HandleMaximumHitPointsChanged(
        MemberState member,
        ModifiableValue value,
        int oldValue) {
        if (!IsOperational ||
            member == null ||
            FindMemberIndex(member.Unit) < 0 ||
            ReferenceEquals(value, member.Health.HitPoints) == false) {
            return;
        }
        if (member.Health.MaxHitPoints ==
            member.RegisteredMaximumHitPoints) {
            return;
        }

        Faulted = true;
        ActiveInstance = null;
        Main.LogWarning(
            $"The Equalizer detected a maximum-HP change and disabled itself for the current combat: " +
            $"Unit={member.Unit.CharacterName}, " +
            $"OldMaximum={oldValue}, " +
            $"NewMaximum={member.Health.MaxHitPoints}.");
        EncounterMechanicController
            .DisableActiveMechanicForCurrentCombat();
    }

    private void EnsureWorkBufferCapacity(
        int required) {
        if (required <= 0) {
            return;
        }
        if (WorkBuffer != null &&
            WorkBuffer.Length >= required) {
            return;
        }

        int capacity = WorkBuffer == null
            ? 4
            : WorkBuffer.Length;
        while (capacity < required) {
            capacity = checked(capacity * 2);
        }
        WorkBuffer = new MemberState[capacity];
    }

    private void CleanupOwnedState() {
        List<MemberState> members = Members;
        if (members != null) {
            for (int index = 0;
                 index < members.Count;
                 index++) {
                members[index]
                    .UnsubscribeFromMaximumHitPoints();
            }
            members.Clear();
        }

        if (WorkBuffer != null) {
            Array.Clear(
                WorkBuffer,
                0,
                WorkBuffer.Length);
        }

        Members = null;
        WorkBuffer = null;
        MaximumPool = 0;
        CurrentPool = 0;
        NextRegistrationOrdinal = 0;
        DisplayedPercent = -1;
        AppliedBonusPercent = -1;
        ExternalRemovalWarningLogged = false;
    }

    private void FailActive(
        string message,
        Exception exception) {
        if (Faulted) {
            return;
        }

        Faulted = true;
        ActiveInstance = null;
        if (!FailureLogged) {
            FailureLogged = true;
            Main.LogError(
                exception == null
                    ? message
                    : $"{message} {exception}");
        }

        try {
            EncounterMechanicController
                .DisableActiveMechanicForCurrentCombat();
        } catch (Exception disableException) {
            Main.LogError(
                $"The Equalizer could not complete emergency cleanup: {disableException}");
        }
    }

    private static void HandleHookFailure(
        EqualizerMechanic instance,
        string operation,
        Exception exception) {
        try {
            instance?.FailActive(
                $"The Equalizer hook failed during {operation}; native health processing was left intact.",
                exception);
        } catch (Exception nestedException) {
            try {
                ActiveInstance = null;
                Main.LogError(
                    $"The Equalizer hook cleanup failed during {operation}: {nestedException}");
            } catch {
                ActiveInstance = null;
            }
        }
    }

    private sealed class MemberState {
        private readonly EqualizerMechanic Owner;
        private readonly Action<ModifiableValue, int>
            MaximumHitPointsChangedHandler;
        private bool MaximumHitPointsSubscribed;

        internal BaseUnitEntity Unit { get; }
        internal PartHealth Health { get; }
        internal int RegisteredMaximumHitPoints { get; }
        internal int RegistrationOrdinal { get; }
        internal int LastSynchronizedRealHP;
        internal int AllocationLowerBound;
        internal int AllocationUpperBound;
        internal int PlannedHitPoints;
        internal long AllocationRemainder;
        internal bool AllocationActive;
        internal bool RemainderAwarded;

        internal MemberState(
            EqualizerMechanic owner,
            BaseUnitEntity unit,
            PartHealth health,
            int registeredMaximumHitPoints,
            int registrationOrdinal) {
            Owner = owner;
            Unit = unit;
            Health = health;
            RegisteredMaximumHitPoints =
                registeredMaximumHitPoints;
            RegistrationOrdinal =
                registrationOrdinal;
            LastSynchronizedRealHP =
                health.HitPointsLeft;
            MaximumHitPointsChangedHandler =
                HandleMaximumHitPointsChanged;
        }

        internal void SubscribeToMaximumHitPoints() {
            if (MaximumHitPointsSubscribed) {
                return;
            }

            Health.HitPoints.OnChanged +=
                MaximumHitPointsChangedHandler;
            MaximumHitPointsSubscribed = true;
        }

        internal void UnsubscribeFromMaximumHitPoints() {
            if (!MaximumHitPointsSubscribed) {
                return;
            }

            Health.HitPoints.OnChanged -=
                MaximumHitPointsChangedHandler;
            MaximumHitPointsSubscribed = false;
        }

        private void HandleMaximumHitPointsChanged(
            ModifiableValue value,
            int oldValue) {
            Owner.HandleMaximumHitPointsChanged(
                this,
                value,
                oldValue);
        }
    }

    private readonly struct HealthMutationState {
        internal EqualizerMechanic Owner { get; }
        internal MemberState Member { get; }
        internal PartHealth Health { get; }
        internal int OldDamage { get; }
        internal int OldHitPoints { get; }
        internal int RequestedDamage { get; }
        internal bool ExternalGuardEntered { get; }
        internal bool ShouldProcess =>
            Owner != null &&
            Member != null &&
            Health != null &&
            ExternalGuardEntered;

        internal HealthMutationState(
            EqualizerMechanic owner,
            MemberState member,
            PartHealth health,
            int oldDamage,
            int oldHitPoints,
            int requestedDamage,
            bool externalGuardEntered) {
            Owner = owner;
            Member = member;
            Health = health;
            OldDamage = oldDamage;
            OldHitPoints = oldHitPoints;
            RequestedDamage = requestedDamage;
            ExternalGuardEntered =
                externalGuardEntered;
        }
    }

    private enum PoolChangeDirection {
        Damage,
        Healing
    }

    [HarmonyPatch(
        typeof(PartHealth),
        nameof(PartHealth.SetDamage),
        typeof(int))]
    private static class PartHealthSetDamagePatch {
        [HarmonyPrefix]
        private static void Prefix(
            PartHealth __instance,
            int damage,
            out HealthMutationState __state) {
            __state = default;
            EqualizerMechanic active =
                ActiveInstance;
            if (active == null) {
                return;
            }

            try {
                __state = active.BeginHealthMutation(
                    __instance,
                    damage);
            } catch (Exception exception) {
                HandleHookFailure(
                    active,
                    "SetDamage prefix",
                    exception);
            }
        }

        [HarmonyPostfix]
        private static void Postfix(
            HealthMutationState __state) {
            if (!__state.ShouldProcess) {
                return;
            }

            try {
                __state.Owner
                    .HandleCompletedHealthMutation(
                        __state);
            } catch (Exception exception) {
                HandleHookFailure(
                    __state.Owner,
                    "SetDamage postfix",
                    exception);
            }
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(
            Exception __exception,
            HealthMutationState __state) {
            if (__state.ExternalGuardEntered) {
                try {
                    __state.Owner.EndHealthMutation(
                        __state);
                } catch (Exception exception) {
                    HandleHookFailure(
                        __state.Owner,
                        "SetDamage finalizer",
                        exception);
                }
            }
            return __exception;
        }
    }

    [HarmonyPatch(
        typeof(RuleDealDamage),
        nameof(RuleDealDamage.OnTrigger),
        typeof(RulebookEventContext))]
    private static class RuleDealDamageOnTriggerPatch {
        [HarmonyPrefix]
        private static void Prefix(
            RuleDealDamage __instance) {
            EqualizerMechanic active =
                ActiveInstance;
            if (active == null) {
                return;
            }

            try {
                active.EnsureRuleParticipantsRegistered(
                    __instance);
            } catch (Exception exception) {
                HandleHookFailure(
                    active,
                    "RuleDealDamage prefix",
                    exception);
            }
        }
    }
}
