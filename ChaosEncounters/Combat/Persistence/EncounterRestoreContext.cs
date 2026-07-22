using Kingmaker;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;

namespace ChaosEncounters.Combat.Persistence;

internal sealed class EncounterRestoreContext {
    private readonly EntityService EntityService;

    internal EncounterSession Session { get; }
    internal IReadOnlyList<BaseUnitEntity> LivingEnemies { get; }

    private EncounterRestoreContext(
        EncounterSession session,
        List<BaseUnitEntity> livingEnemies,
        EntityService entityService) {
        Session = session;
        LivingEnemies = livingEnemies;
        EntityService = entityService;
    }

    internal static bool TryCreate(
        EncounterSession session,
        out EncounterRestoreContext context,
        out string failureReason) {
        context = null;
        failureReason = null;
        if (session == null) {
            failureReason =
                "The loaded encounter session is unavailable.";
            return false;
        }

        Game game = Game.Instance;
        if (game?.State?.AllBaseAwakeUnitsForSure == null) {
            failureReason =
                "The loaded awake-unit collection is unavailable.";
            return false;
        }

        EntityService entityService = EntityService.Instance;
        if (entityService == null) {
            failureReason =
                "EntityService is unavailable during encounter restoration.";
            return false;
        }

        var livingEnemies = new List<BaseUnitEntity>();
        foreach (BaseUnitEntity candidate in
            game.State.AllBaseAwakeUnitsForSure) {
            if (IsValidLoadedEnemy(
                    candidate,
                    requireLiving: true) &&
                EncounterPersistenceValidation
                    .IsValidEntityId(candidate.UniqueId)) {
                livingEnemies.Add(candidate);
            }
        }

        context = new EncounterRestoreContext(
            session,
            livingEnemies,
            entityService);
        return true;
    }

    internal bool TryResolveEnemy(
        string id,
        bool requireLiving,
        out BaseUnitEntity unit) {
        unit = null;
        if (!EncounterPersistenceValidation
                .IsValidEntityId(id)) {
            return false;
        }

        BaseUnitEntity resolved =
            EntityService.GetEntity<BaseUnitEntity>(id);
        if (!IsValidLoadedEnemy(resolved, requireLiving) ||
            !string.Equals(
                resolved.UniqueId,
                id,
                StringComparison.Ordinal)) {
            return false;
        }

        unit = resolved;
        return true;
    }

    private static bool IsValidLoadedEnemy(
        BaseUnitEntity unit,
        bool requireLiving) {
        if (unit == null ||
            unit is StarshipEntity ||
            unit.IsDisposed ||
            !unit.IsInGame ||
            !unit.IsInCombat ||
            !unit.IsPlayerEnemy ||
            unit.LifeState == null) {
            return false;
        }

        return !requireLiving ||
               (!unit.LifeState.IsDead &&
                !unit.LifeState.IsFinallyDead);
    }
}
