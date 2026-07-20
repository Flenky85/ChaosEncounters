using ChaosEncounters.UI;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;

namespace ChaosEncounters.Combat.Mechanics.Common;

internal sealed class ExecutionListMechanic :
    IEncounterMechanic {
    private const string MechanicId = "ExecutionList";
    private const string HudTitle = "The Execution List";
    private const string HudDescription =
        "Every enemy is assigned a position on the Execution List. Position 1 has 0% damage reduction, position 2 has 20%, position 3 has 40%, position 4 has 60%, position 5 has 80%, and positions 6 or higher are immune with 100% damage reduction. When an enemy dies, every enemy behind it moves up one position and its damage reduction is updated accordingly, bringing each survivor one step closer to execution.";

    private static System.Random OrderRandom;

    private BaseUnitEntity[] OrderedEnemies;
    private int ActiveEnemyCount;

    public string Id => MechanicId;

    public bool CanActivate(EncounterSession session) {
        if (session == null ||
            session.Type != EncounterType.Common) {
            return false;
        }

        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity candidate =
                session.InitialEnemies[index];
            if (candidate != null &&
                !candidate.IsDisposed &&
                !candidate.LifeState.IsDead) {
                return true;
            }
        }

        return false;
    }

    public void Activate(EncounterSession session) {
        if (session == null) {
            throw new InvalidOperationException(
                "The Execution List requires an encounter session.");
        }
        if (OrderedEnemies != null) {
            throw new InvalidOperationException(
                "The Execution List is already active.");
        }
        if (session.Type != EncounterType.Common) {
            throw new InvalidOperationException(
                "The Execution List requires a Common encounter.");
        }

        int livingEnemyCount = 0;
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity candidate =
                session.InitialEnemies[index];
            if (candidate != null &&
                !candidate.IsDisposed &&
                !candidate.LifeState.IsDead) {
                livingEnemyCount++;
            }
        }
        if (livingEnemyCount == 0) {
            throw new InvalidOperationException(
                "The Execution List requires at least one living initial enemy.");
        }

        var orderedEnemies =
            new BaseUnitEntity[livingEnemyCount];
        int orderedIndex = 0;
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity candidate =
                session.InitialEnemies[index];
            if (candidate == null ||
                candidate.IsDisposed ||
                candidate.LifeState.IsDead) {
                continue;
            }

            orderedEnemies[orderedIndex] = candidate;
            orderedIndex++;
        }

        if (livingEnemyCount > 1) {
            System.Random random = OrderRandom;
            if (random == null) {
                random = new System.Random();
                OrderRandom = random;
            }

            for (int index = livingEnemyCount - 1;
                 index > 0;
                 index--) {
                int swapIndex = random.Next(index + 1);
                BaseUnitEntity temporary =
                    orderedEnemies[index];
                orderedEnemies[index] =
                    orderedEnemies[swapIndex];
                orderedEnemies[swapIndex] = temporary;
            }
        }

        OrderedEnemies = orderedEnemies;
        ActiveEnemyCount = livingEnemyCount;

        for (int index = 0;
             index < ActiveEnemyCount;
             index++) {
            ApplyPosition(
                OrderedEnemies[index],
                index + 1);
        }

        EncounterHud.Show(
            HudTitle,
            HudDescription);
        Main.LogInfo(
            $"The Execution List activated: EnemyCount={ActiveEnemyCount}");
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
        if (OrderedEnemies == null ||
            unit == null ||
            ActiveEnemyCount <= 0) {
            return;
        }

        int deadIndex = -1;
        for (int index = 0;
             index < ActiveEnemyCount;
             index++) {
            if (ReferenceEquals(
                    OrderedEnemies[index],
                    unit)) {
                deadIndex = index;
                break;
            }
        }
        if (deadIndex < 0) {
            return;
        }

        int fallenPosition = deadIndex + 1;
        DamageControl.ClearIncomingDamageReduction(unit);
        UnitMarker.ClearMarker(unit);

        int oldActiveCount = ActiveEnemyCount;
        for (int index = deadIndex;
             index < oldActiveCount - 1;
             index++) {
            BaseUnitEntity shiftedEnemy =
                OrderedEnemies[index + 1];
            OrderedEnemies[index] = shiftedEnemy;
            ApplyPosition(
                shiftedEnemy,
                index + 1);
        }

        ActiveEnemyCount = oldActiveCount - 1;
        OrderedEnemies[ActiveEnemyCount] = null;

        Main.LogInfo(
            $"The Execution List advanced: FallenPosition={fallenPosition} " +
            $"RemainingEnemies={ActiveEnemyCount}");
        if (ActiveEnemyCount == 0) {
            EncounterHud.Hide();
        }
    }

    public void Deactivate(
        EncounterMechanicEndReason reason) {
        OrderedEnemies = null;
        ActiveEnemyCount = 0;
    }

    private static void ApplyPosition(
        BaseUnitEntity unit,
        int position) {
        int incomingReduction =
            GetIncomingReduction(position);
        Color32 markerColor = GetMarkerColor(position);

        DamageControl.SetIncomingDamageReduction(
            unit,
            incomingReduction);
        UnitMarker.SetMarker(
            unit,
            position.ToString(),
            markerColor);
    }

    private static int GetIncomingReduction(int position) {
        if (position >= 6) {
            return 100;
        }

        return (position - 1) * 20;
    }

    private static Color32 GetMarkerColor(int position) {
        switch (position) {
            case 1:
                return ChaosColors.Red;
            case 2:
                return ChaosColors.Orange;
            case 3:
                return ChaosColors.Yellow;
            case 4:
                return ChaosColors.Green;
            case 5:
                return ChaosColors.Blue;
            default:
                return ChaosColors.Grey;
        }
    }
}
