using System.Collections.Generic;
using ChaosEncounters.UI;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;

namespace ChaosEncounters.Combat.Mechanics.Common;

internal sealed class LinkMechanic :
    IEncounterMechanic,
    IUnitCombatLifecycleAwareMechanic {
    private const string MechanicId = "Link";
    private const string MechanicDisplayName = "Links";
    private const string MechanicDescription =
        "Each party member is paired with one enemy. Matching L1–L6 markers identify each character, their pets, and their linked target.";
    private const int MinimumGroupCount = 1;
    private const int MaximumGroupCount = 6;
    private const int MinimumInitialEnemyCount = 6;
    private const string SlotOneMarkerText = "L1";
    private const string SlotTwoMarkerText = "L2";
    private const string SlotThreeMarkerText = "L3";
    private const string SlotFourMarkerText = "L4";
    private const string SlotFiveMarkerText = "L5";
    private const string SlotSixMarkerText = "L6";

    private static System.Random AssignmentRandom;

    private List<LinkGroup> Groups;
    private List<BaseUnitEntity> KnownEnemies;

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

        KnownEnemies = eligibleEnemies;
        Groups = groups;
        ApplyGroupMarkers(groups);
        EncounterHud.Show(
            DisplayName,
            Description);
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
        List<LinkGroup> groups = Groups;
        List<BaseUnitEntity> knownEnemies =
            KnownEnemies;
        if (groups == null ||
            knownEnemies == null ||
            unit == null) {
            return;
        }

        int enemyIndex = FindEnemyIndex(
            knownEnemies,
            unit);
        if (enemyIndex < 0) {
            return;
        }

        knownEnemies.RemoveAt(enemyIndex);
        UnitMarker.ClearMarker(unit);
        bool linkedEnemy = false;
        for (int index = 0;
             index < groups.Count;
             index++) {
            LinkGroup group = groups[index];
            if (ReferenceEquals(
                    group.LinkedEnemy,
                    unit)) {
                group.LinkedEnemy = null;
                linkedEnemy = true;
                break;
            }
        }

        if (linkedEnemy) {
            RebalanceLinks(groups, knownEnemies);
        }
    }

    public void HandleUnitJoinedCombat(
        BaseUnitEntity unit) {
    }

    public void HandleUnitLeftCombat(
        BaseUnitEntity unit) {
        List<LinkGroup> groups = Groups;
        List<BaseUnitEntity> knownEnemies =
            KnownEnemies;
        if (groups == null ||
            knownEnemies == null ||
            unit == null) {
            return;
        }

        for (int groupIndex = 0;
             groupIndex < groups.Count;
             groupIndex++) {
            LinkGroup group = groups[groupIndex];
            if (!ReferenceEquals(
                    group.Owner,
                    unit)) {
                continue;
            }

            BaseUnitEntity linkedEnemy =
                group.LinkedEnemy;
            UnitMarker.ClearMarker(group.Owner);
            for (int petIndex = 0;
                 petIndex < group.Pets.Count;
                 petIndex++) {
                UnitMarker.ClearMarker(
                    group.Pets[petIndex]);
            }
            if (linkedEnemy != null) {
                UnitMarker.ClearMarker(linkedEnemy);
                group.LinkedEnemy = null;
            }

            groups.RemoveAt(groupIndex);
            RebalanceLinks(groups, knownEnemies);
            return;
        }

        for (int groupIndex = 0;
             groupIndex < groups.Count;
             groupIndex++) {
            List<BaseUnitEntity> pets =
                groups[groupIndex].Pets;
            for (int petIndex = 0;
                 petIndex < pets.Count;
                 petIndex++) {
                if (!ReferenceEquals(
                        pets[petIndex],
                        unit)) {
                    continue;
                }

                pets.RemoveAt(petIndex);
                UnitMarker.ClearMarker(unit);
                return;
            }
        }

        int enemyIndex = FindEnemyIndex(
            knownEnemies,
            unit);
        if (enemyIndex < 0) {
            return;
        }

        knownEnemies.RemoveAt(enemyIndex);
        UnitMarker.ClearMarker(unit);
        for (int groupIndex = 0;
             groupIndex < groups.Count;
             groupIndex++) {
            LinkGroup group = groups[groupIndex];
            if (!ReferenceEquals(
                    group.LinkedEnemy,
                    unit)) {
                continue;
            }

            group.LinkedEnemy = null;
            RebalanceLinks(groups, knownEnemies);
            return;
        }

        return;
    }

    public void Deactivate(
        EncounterMechanicEndReason reason) {
        Groups = null;
        KnownEnemies = null;
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
        System.Random random = GetAssignmentRandom();

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

    private static void ApplyGroupMarkers(
        List<LinkGroup> groups) {
        for (int index = 0;
             index < groups.Count;
             index++) {
            SynchronizeGroupPresentation(
                groups[index]);
        }
    }

    private static void RebalanceLinks(
        List<LinkGroup> groups,
        List<BaseUnitEntity> knownEnemies) {
        int linkedGroupCount = 0;
        for (int index = 0;
             index < groups.Count;
             index++) {
            LinkGroup group = groups[index];
            if (group.LinkedEnemy == null) {
                int availableEnemyCount =
                    CountAvailableEnemies(
                        groups,
                        knownEnemies);
                if (availableEnemyCount > 0) {
                    int selectedOrdinal =
                        GetAssignmentRandom().Next(
                            availableEnemyCount);
                    group.LinkedEnemy =
                        ResolveAvailableEnemy(
                            groups,
                            knownEnemies,
                            selectedOrdinal);
                }

                SynchronizeGroupPresentation(group);
            }
            if (group.LinkedEnemy != null) {
                linkedGroupCount++;
            }
        }

        if (linkedGroupCount > 0) {
            EncounterHud.Show(
                MechanicDisplayName,
                MechanicDescription);
        } else {
            EncounterHud.Hide();
        }
    }

    private static int CountAvailableEnemies(
        List<LinkGroup> groups,
        List<BaseUnitEntity> knownEnemies) {
        int count = 0;
        for (int index = 0;
             index < knownEnemies.Count;
             index++) {
            BaseUnitEntity enemy = knownEnemies[index];
            if (IsEligibleInitialEnemy(enemy) &&
                !IsEnemyLinked(groups, enemy)) {
                count++;
            }
        }

        return count;
    }

    private static BaseUnitEntity ResolveAvailableEnemy(
        List<LinkGroup> groups,
        List<BaseUnitEntity> knownEnemies,
        int selectedOrdinal) {
        for (int index = 0;
             index < knownEnemies.Count;
             index++) {
            BaseUnitEntity enemy = knownEnemies[index];
            if (!IsEligibleInitialEnemy(enemy) ||
                IsEnemyLinked(groups, enemy)) {
                continue;
            }
            if (selectedOrdinal == 0) {
                return enemy;
            }

            selectedOrdinal--;
        }

        throw new InvalidOperationException(
            "Links could not resolve the selected available enemy.");
    }

    private static bool IsEnemyLinked(
        List<LinkGroup> groups,
        BaseUnitEntity enemy) {
        for (int index = 0;
             index < groups.Count;
             index++) {
            if (ReferenceEquals(
                    groups[index].LinkedEnemy,
                    enemy)) {
                return true;
            }
        }

        return false;
    }

    private static int FindEnemyIndex(
        List<BaseUnitEntity> enemies,
        BaseUnitEntity enemy) {
        for (int index = 0;
             index < enemies.Count;
             index++) {
            if (ReferenceEquals(
                    enemies[index],
                    enemy)) {
                return index;
            }
        }

        return -1;
    }

    private static System.Random GetAssignmentRandom() {
        System.Random random = AssignmentRandom;
        if (random == null) {
            random = new System.Random();
            AssignmentRandom = random;
        }

        return random;
    }

    private static void SynchronizeGroupPresentation(
        LinkGroup group) {
        BaseUnitEntity linkedEnemy =
            group.LinkedEnemy;
        if (linkedEnemy == null) {
            UnitMarker.ClearMarker(group.Owner);
            for (int index = 0;
                 index < group.Pets.Count;
                 index++) {
                UnitMarker.ClearMarker(
                    group.Pets[index]);
            }

            return;
        }

        string markerText =
            GetMarkerText(group.Slot);
        Color32 markerColor =
            GetMarkerColor(group.Slot);
        UnitMarker.SetMarker(
            group.Owner,
            markerText,
            markerColor);
        for (int index = 0;
             index < group.Pets.Count;
             index++) {
            UnitMarker.SetMarker(
                group.Pets[index],
                markerText,
                markerColor);
        }
        UnitMarker.SetMarker(
            linkedEnemy,
            markerText,
            markerColor);
    }

    private static string GetMarkerText(int slot) {
        switch (slot) {
            case 1:
                return SlotOneMarkerText;
            case 2:
                return SlotTwoMarkerText;
            case 3:
                return SlotThreeMarkerText;
            case 4:
                return SlotFourMarkerText;
            case 5:
                return SlotFiveMarkerText;
            case 6:
                return SlotSixMarkerText;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(slot),
                    slot,
                    "Links requires a group slot from 1 through 6.");
        }
    }

    private static Color32 GetMarkerColor(int slot) {
        switch (slot) {
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
            case 6:
                return ChaosColors.Violet;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(slot),
                    slot,
                    "Links requires a group slot from 1 through 6.");
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
