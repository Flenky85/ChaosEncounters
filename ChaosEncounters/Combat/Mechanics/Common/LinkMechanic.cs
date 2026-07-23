using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;

namespace ChaosEncounters.Combat.Mechanics.Common;

internal sealed class LinkMechanic :
    IEncounterMechanic,
    IUnitCombatLifecycleAwareMechanic {
    private const string MechanicId = "Link";
    private const string MechanicDisplayName = "Links";
    private const string MechanicDescription =
        "Party members and their pets form linked groups.";
    private const int MinimumGroupCount = 1;
    private const int MaximumGroupCount = 6;
    private const int MinimumInitialEnemyCount = 6;

    private static System.Random AssignmentRandom;

    private List<LinkGroup> Groups;

    public string Id => MechanicId;
    public string DisplayName => MechanicDisplayName;
    public string Description => MechanicDescription;

    public bool CanActivate(EncounterSession session) {
        if (session == null ||
            !session.SupportsEncounterType(
                EncounterType.Common)) {
            return false;
        }

        Game game = Game.Instance;
        if (game?.Player == null) {
            return false;
        }

        List<BaseUnitEntity> party = game.Player.Party;
        if (party == null) {
            return false;
        }

        int ownerCount = CountEligibleOwners(party);
        if (ownerCount < MinimumGroupCount ||
            ownerCount > MaximumGroupCount) {
            return false;
        }

        int enemyCount =
            CountEligibleInitialEnemies(session);
        return enemyCount >= MinimumInitialEnemyCount &&
               enemyCount >= ownerCount;
    }

    public void Activate(EncounterSession session) {
        if (session == null) {
            throw new InvalidOperationException(
                "Links requires an encounter session.");
        }
        if (Groups != null) {
            throw new InvalidOperationException(
                "Links is already active.");
        }
        if (!session.SupportsEncounterType(
                EncounterType.Common)) {
            throw new InvalidOperationException(
                "Links requires Common encounter eligibility.");
        }

        Game game = Game.Instance;
        if (game?.Player == null) {
            throw new InvalidOperationException(
                "Links requires an available player.");
        }

        List<BaseUnitEntity> party = game.Player.Party;
        List<BaseUnitEntity> partyAndPets =
            game.Player.PartyAndPets;
        if (party == null || partyAndPets == null) {
            throw new InvalidOperationException(
                "Links requires available party collections.");
        }

        List<LinkGroup> groups =
            BuildGroups(party, partyAndPets);
        List<BaseUnitEntity> eligibleEnemies =
            BuildEligibleInitialEnemies(session);
        int ownerCount = groups.Count;
        int enemyCount = eligibleEnemies.Count;
        if (ownerCount < MinimumGroupCount ||
            ownerCount > MaximumGroupCount ||
            enemyCount < MinimumInitialEnemyCount ||
            enemyCount < ownerCount) {
            throw new InvalidOperationException(
                "Links activation requirements changed before group construction completed.");
        }

        AssignLinkedEnemies(groups, eligibleEnemies);
        for (int index = 0;
             index < groups.Count;
             index++) {
            if (groups[index].LinkedEnemy == null) {
                throw new InvalidOperationException(
                    "Links could not assign an eligible initial enemy to every group.");
            }
        }

        Groups = groups;
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
    }

    public void HandleUnitJoinedCombat(
        BaseUnitEntity unit) {
    }

    public void HandleUnitLeftCombat(
        BaseUnitEntity unit) {
    }

    public void Deactivate(
        EncounterMechanicEndReason reason) {
        Groups = null;
    }

    private static int CountEligibleOwners(
        List<BaseUnitEntity> party) {
        int count = 0;
        for (int index = 0;
             index < party.Count;
             index++) {
            if (IsEligibleOwner(party[index])) {
                count++;
            }
        }

        return count;
    }

    private static int CountEligibleInitialEnemies(
        EncounterSession session) {
        int count = 0;
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            if (IsEligibleInitialEnemy(
                    session.InitialEnemies[index])) {
                count++;
            }
        }

        return count;
    }

    private static List<BaseUnitEntity>
        BuildEligibleInitialEnemies(
            EncounterSession session) {
        var eligibleEnemies =
            new List<BaseUnitEntity>(
                session.InitialEnemies.Count);
        for (int index = 0;
             index < session.InitialEnemies.Count;
             index++) {
            BaseUnitEntity enemy =
                session.InitialEnemies[index];
            if (IsEligibleInitialEnemy(enemy)) {
                eligibleEnemies.Add(enemy);
            }
        }

        return eligibleEnemies;
    }

    private static void AssignLinkedEnemies(
        List<LinkGroup> groups,
        List<BaseUnitEntity> eligibleEnemies) {
        System.Random random = AssignmentRandom;
        if (random == null) {
            random = new System.Random();
            AssignmentRandom = random;
        }

        for (int index = eligibleEnemies.Count - 1;
             index > 0;
             index--) {
            int swapIndex = random.Next(index + 1);
            BaseUnitEntity temporary =
                eligibleEnemies[index];
            eligibleEnemies[index] =
                eligibleEnemies[swapIndex];
            eligibleEnemies[swapIndex] = temporary;
        }

        for (int index = 0;
             index < groups.Count;
             index++) {
            groups[index].LinkedEnemy =
                eligibleEnemies[index];
        }
    }

    private static List<LinkGroup> BuildGroups(
        List<BaseUnitEntity> party,
        List<BaseUnitEntity> partyAndPets) {
        var groups = new List<LinkGroup>();
        for (int index = 0;
             index < party.Count;
             index++) {
            BaseUnitEntity owner = party[index];
            if (!IsEligibleOwner(owner)) {
                continue;
            }
            if (groups.Count == MaximumGroupCount) {
                throw new InvalidOperationException(
                    "Links supports at most six eligible party owners.");
            }

            groups.Add(
                new LinkGroup(
                    groups.Count + 1,
                    owner));
        }

        for (int index = 0;
             index < partyAndPets.Count;
             index++) {
            BaseUnitEntity pet = partyAndPets[index];
            if (!IsEligiblePet(pet)) {
                continue;
            }

            BaseUnitEntity master = pet.Master;
            if (master == null) {
                continue;
            }
            for (int groupIndex = 0;
                 groupIndex < groups.Count;
                 groupIndex++) {
                LinkGroup group = groups[groupIndex];
                if (ReferenceEquals(
                        master,
                        group.Owner)) {
                    group.Pets.Add(pet);
                    break;
                }
            }
        }

        return groups;
    }

    private static bool IsEligibleOwner(
        BaseUnitEntity owner) {
        return owner != null &&
               owner is not StarshipEntity &&
               !owner.IsDisposed &&
               owner.IsInGame &&
               owner.IsInCombat &&
               owner.LifeState != null &&
               owner.LifeState.IsConscious;
    }

    private static bool IsEligiblePet(
        BaseUnitEntity pet) {
        return pet != null &&
               pet is not StarshipEntity &&
               !pet.IsDisposed &&
               pet.IsInGame &&
               pet.IsInCombat &&
               pet.LifeState != null &&
               pet.LifeState.IsConscious &&
               pet.IsPet;
    }

    private static bool IsEligibleInitialEnemy(
        BaseUnitEntity enemy) {
        return enemy != null &&
               enemy is not StarshipEntity &&
               !enemy.IsDisposed &&
               enemy.IsInGame &&
               enemy.IsInCombat &&
               enemy.IsPlayerEnemy &&
               enemy.LifeState != null &&
               enemy.LifeState.IsConscious;
    }

    private sealed class LinkGroup {
        internal int Slot { get; }
        internal BaseUnitEntity Owner { get; }
        internal List<BaseUnitEntity> Pets { get; }
        internal BaseUnitEntity LinkedEnemy { get; set; }

        internal LinkGroup(
            int slot,
            BaseUnitEntity owner) {
            Slot = slot;
            Owner = owner;
            Pets = new List<BaseUnitEntity>();
        }
    }
}
