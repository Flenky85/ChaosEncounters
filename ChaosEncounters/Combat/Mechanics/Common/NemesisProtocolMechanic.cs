using System.Collections.Generic;
using ChaosEncounters.Combat.Persistence;
using ChaosEncounters.UI;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;

namespace ChaosEncounters.Combat.Mechanics.Common;

internal sealed class NemesisProtocolMechanic :
    IEncounterMechanic,
    IUnitCombatLifecycleAwareMechanic,
    IPersistableEncounterMechanic {
    internal const string MechanicId = "NemesisProtocol";
    private const string MechanicDisplayName = "Nemesis Protocol";
    private const string MechanicDescription =
        "Each character and their pets are linked to one marked enemy. They deal full damage to their linked target, but only 20% damage to other enemies. When a linked target is lost, the protocol assigns another available unlinked enemy at random. If no valid target remains, the group becomes unlinked and deals full damage to all enemies.";
    private const int MinimumGroupCount = 1;
    private const int MaximumGroupCount = 6;
    private const int MinimumInitialEnemyCount = 6;
    private const int OffTargetDamageReductionPercent = 80;
    private const string SlotOneMarkerText = "L1";
    private const string SlotTwoMarkerText = "L2";
    private const string SlotThreeMarkerText = "L3";
    private const string SlotFourMarkerText = "L4";
    private const string SlotFiveMarkerText = "L5";
    private const string SlotSixMarkerText = "L6";

    private static System.Random AssignmentRandom;

    private List<NemesisProtocolGroup> Groups;
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
                "Nemesis Protocol requires an encounter session.");
        }
        if (Groups != null) {
            throw new InvalidOperationException(
                "Nemesis Protocol is already active.");
        }
        if (!session.SupportsEncounterType(
                EncounterType.Common)) {
            throw new InvalidOperationException(
                "Nemesis Protocol requires Common encounter eligibility.");
        }

        Game game = Game.Instance;
        if (game?.Player == null) {
            throw new InvalidOperationException(
                "Nemesis Protocol requires an available player.");
        }

        List<BaseUnitEntity> party = game.Player.Party;
        List<BaseUnitEntity> partyAndPets =
            game.Player.PartyAndPets;
        if (party == null || partyAndPets == null) {
            throw new InvalidOperationException(
                "Nemesis Protocol requires available party collections.");
        }

        List<NemesisProtocolGroup> groups =
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
                "Nemesis Protocol activation requirements changed before group construction completed.");
        }

        AssignLinkedEnemies(groups, eligibleEnemies);
        for (int index = 0;
             index < groups.Count;
             index++) {
            if (groups[index].LinkedEnemy == null) {
                throw new InvalidOperationException(
                    "Nemesis Protocol could not assign an eligible initial enemy to every group.");
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
        List<NemesisProtocolGroup> groups = Groups;
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
            NemesisProtocolGroup group = groups[index];
            if (ReferenceEquals(
                    group.LinkedEnemy,
                    unit)) {
                group.LinkedEnemy = null;
                linkedEnemy = true;
                break;
            }
        }

        if (linkedEnemy) {
            RebalanceNemesisAssignments(
                groups,
                knownEnemies);
        }
    }

    public void HandleUnitJoinedCombat(
        BaseUnitEntity unit) {
        List<NemesisProtocolGroup> groups = Groups;
        List<BaseUnitEntity> knownEnemies =
            KnownEnemies;
        if (groups == null ||
            knownEnemies == null ||
            unit == null) {
            return;
        }

        if (IsEligiblePet(unit) &&
            unit.Master != null) {
            for (int groupIndex = 0;
                 groupIndex < groups.Count;
                 groupIndex++) {
                NemesisProtocolGroup group =
                    groups[groupIndex];
                if (!ReferenceEquals(
                        group.Owner,
                        unit.Master)) {
                    continue;
                }

                for (int petIndex = 0;
                     petIndex < group.Pets.Count;
                     petIndex++) {
                    if (ReferenceEquals(
                            group.Pets[petIndex],
                            unit)) {
                        return;
                    }
                }

                group.Pets.Add(unit);
                SynchronizeGroupState(group);
                return;
            }

            return;
        }

        if (IsEligibleInitialEnemy(unit)) {
            if (FindEnemyIndex(
                    knownEnemies,
                    unit) >= 0) {
                return;
            }

            knownEnemies.Add(unit);
            RebalanceNemesisAssignments(
                groups,
                knownEnemies);
            return;
        }
    }

    public void HandleUnitLeftCombat(
        BaseUnitEntity unit) {
        List<NemesisProtocolGroup> groups = Groups;
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
            NemesisProtocolGroup group =
                groups[groupIndex];
            if (!ReferenceEquals(
                    group.Owner,
                    unit)) {
                continue;
            }

            BaseUnitEntity linkedEnemy =
                group.LinkedEnemy;
            UnitMarker.ClearMarker(group.Owner);
            DamageControl.ClearOffTargetDamageReduction(
                group.Owner);
            for (int petIndex = 0;
                 petIndex < group.Pets.Count;
                 petIndex++) {
                UnitMarker.ClearMarker(
                    group.Pets[petIndex]);
                DamageControl
                    .ClearOffTargetDamageReduction(
                        group.Pets[petIndex]);
            }
            if (linkedEnemy != null) {
                UnitMarker.ClearMarker(linkedEnemy);
                group.LinkedEnemy = null;
            }

            groups.RemoveAt(groupIndex);
            RebalanceNemesisAssignments(
                groups,
                knownEnemies);
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
                DamageControl
                    .ClearOffTargetDamageReduction(unit);
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
            NemesisProtocolGroup group =
                groups[groupIndex];
            if (!ReferenceEquals(
                    group.LinkedEnemy,
                    unit)) {
                continue;
            }

            group.LinkedEnemy = null;
            RebalanceNemesisAssignments(
                groups,
                knownEnemies);
            return;
        }

        return;
    }

    bool IPersistableEncounterMechanic.TryCaptureSaveData(
        EncounterMechanicSaveData saveData,
        out string failureReason) {
        failureReason = null;
        if (saveData == null) {
            failureReason =
                "The Nemesis Protocol save-data container is unavailable.";
            return false;
        }
        if (saveData.NemesisProtocol != null) {
            failureReason =
                "The Nemesis Protocol save-data container is already populated.";
            return false;
        }

        List<NemesisProtocolGroup> groups =
            Groups;
        List<BaseUnitEntity> knownEnemies =
            KnownEnemies;
        if (groups == null || knownEnemies == null) {
            failureReason =
                "Nemesis Protocol is not initialized for save capture.";
            return false;
        }
        if (groups.Count > MaximumGroupCount) {
            failureReason =
                $"The Nemesis Protocol group count exceeds {MaximumGroupCount}.";
            return false;
        }

        var knownEnemyIds = new HashSet<string>(
            StringComparer.Ordinal);
        for (int index = 0;
             index < knownEnemies.Count;
             index++) {
            BaseUnitEntity enemy = knownEnemies[index];
            string enemyId = enemy?.UniqueId;
            if (!IsEligibleInitialEnemy(enemy) ||
                !EncounterPersistenceValidation
                    .IsValidEntityId(enemyId)) {
                failureReason =
                    $"The Nemesis Protocol known enemy at index {index} is not a valid living combat enemy.";
                return false;
            }
            if (!knownEnemyIds.Add(enemyId)) {
                failureReason =
                    $"The Nemesis Protocol known enemy at index {index} has a duplicate persistent ID.";
                return false;
            }
        }

        var savedGroups =
            new List<NemesisProtocolGroupSaveData>(
            groups.Count);
        var uniqueSlots = new HashSet<int>();
        var uniqueOwnerIds = new HashSet<string>(
            StringComparer.Ordinal);
        var uniqueTargetIds = new HashSet<string>(
            StringComparer.Ordinal);
        for (int index = 0;
             index < groups.Count;
             index++) {
            NemesisProtocolGroup group =
                groups[index];
            if (group == null ||
                group.Slot < MinimumGroupCount ||
                group.Slot > MaximumGroupCount ||
                !uniqueSlots.Add(group.Slot)) {
                failureReason =
                    $"The Nemesis Protocol group at index {index} has an invalid or duplicate slot.";
                return false;
            }

            BaseUnitEntity owner = group.Owner;
            string ownerId = owner?.UniqueId;
            if (owner == null ||
                !EncounterPersistenceValidation
                    .IsValidEntityId(ownerId) ||
                !uniqueOwnerIds.Add(ownerId)) {
                failureReason =
                    $"The Nemesis Protocol group at index {index} has an invalid or duplicate owner.";
                return false;
            }

            string linkedEnemyId = null;
            BaseUnitEntity linkedEnemy =
                group.LinkedEnemy;
            if (linkedEnemy != null) {
                linkedEnemyId = linkedEnemy.UniqueId;
                if (!IsEligibleInitialEnemy(linkedEnemy) ||
                    FindEnemyIndex(
                        knownEnemies,
                        linkedEnemy) < 0 ||
                    !EncounterPersistenceValidation
                        .IsValidEntityId(linkedEnemyId) ||
                    !uniqueTargetIds.Add(linkedEnemyId)) {
                    failureReason =
                        $"The Nemesis Protocol group at index {index} has an invalid, unknown, or duplicate linked enemy.";
                    return false;
                }
            }

            savedGroups.Add(
                new NemesisProtocolGroupSaveData {
                    Slot = group.Slot,
                    OwnerId = ownerId,
                    LinkedEnemyId = linkedEnemyId
                });
        }

        saveData.NemesisProtocol =
            new NemesisProtocolSaveRecipe {
            Groups = savedGroups
        };
        return true;
    }

    bool IPersistableEncounterMechanic.TryRestoreFromSave(
        EncounterRestoreContext context,
        EncounterMechanicSaveData saveData,
        out string failureReason) {
        failureReason = null;
        if (Groups != null || KnownEnemies != null) {
            failureReason =
                "Nemesis Protocol is already active.";
            return false;
        }
        if (context == null) {
            failureReason =
                "The Nemesis Protocol restore context is unavailable.";
            return false;
        }

        NemesisProtocolSaveRecipe currentRecipe =
            saveData?.NemesisProtocol;
        NemesisProtocolSaveRecipe legacyRecipe =
            saveData?.LegacyLink;
        if (currentRecipe != null &&
            legacyRecipe != null) {
            failureReason =
                "The Nemesis Protocol save data contains both current and legacy recipes.";
            return false;
        }

        NemesisProtocolSaveRecipe recipe =
            currentRecipe ?? legacyRecipe;
        if (recipe == null) {
            failureReason =
                "The Nemesis Protocol save recipe is missing.";
            return false;
        }
        List<NemesisProtocolGroupSaveData> savedGroups =
            recipe.Groups;
        if (savedGroups == null) {
            failureReason =
                "The Nemesis Protocol saved group list is missing.";
            return false;
        }
        if (savedGroups.Count > MaximumGroupCount) {
            failureReason =
                $"The Nemesis Protocol saved group count exceeds {MaximumGroupCount}.";
            return false;
        }

        var uniqueSlots = new HashSet<int>();
        var uniqueOwnerIds = new HashSet<string>(
            StringComparer.Ordinal);
        var uniqueTargetIds = new HashSet<string>(
            StringComparer.Ordinal);
        for (int index = 0;
             index < savedGroups.Count;
             index++) {
            NemesisProtocolGroupSaveData entry =
                savedGroups[index];
            if (entry == null ||
                entry.Slot < MinimumGroupCount ||
                entry.Slot > MaximumGroupCount ||
                !uniqueSlots.Add(entry.Slot)) {
                failureReason =
                    $"The Nemesis Protocol saved group at index {index} has an invalid or duplicate slot.";
                return false;
            }
            if (!EncounterPersistenceValidation
                    .IsValidEntityId(entry.OwnerId) ||
                !uniqueOwnerIds.Add(entry.OwnerId)) {
                failureReason =
                    $"The Nemesis Protocol saved group at index {index} has an invalid or duplicate owner ID.";
                return false;
            }
            if (entry.LinkedEnemyId != null &&
                (!EncounterPersistenceValidation
                    .IsValidEntityId(
                        entry.LinkedEnemyId) ||
                 !uniqueTargetIds.Add(
                     entry.LinkedEnemyId))) {
                failureReason =
                    $"The Nemesis Protocol saved group at index {index} has an invalid or duplicate linked-enemy ID.";
                return false;
            }
        }

        Game game = Game.Instance;
        List<BaseUnitEntity> party =
            game?.Player?.Party;
        List<BaseUnitEntity> partyAndPets =
            game?.Player?.PartyAndPets;
        if (party == null || partyAndPets == null) {
            failureReason =
                "Nemesis Protocol requires available party collections during restoration.";
            return false;
        }

        var restoredKnownEnemies =
            new List<BaseUnitEntity>(
                context.LivingEnemies.Count);
        var knownEnemyIds = new HashSet<string>(
            StringComparer.Ordinal);
        for (int index = 0;
             index < context.LivingEnemies.Count;
             index++) {
            BaseUnitEntity enemy =
                context.LivingEnemies[index];
            if (!IsEligibleInitialEnemy(enemy)) {
                continue;
            }

            string enemyId = enemy.UniqueId;
            if (!EncounterPersistenceValidation
                    .IsValidEntityId(enemyId) ||
                !knownEnemyIds.Add(enemyId)) {
                failureReason =
                    $"The Nemesis Protocol loaded enemy at index {index} has an invalid or duplicate persistent ID.";
                return false;
            }
            restoredKnownEnemies.Add(enemy);
        }

        var restoredGroups =
            new List<NemesisProtocolGroup>(
            savedGroups.Count);
        for (int index = 0;
             index < savedGroups.Count;
             index++) {
            NemesisProtocolGroupSaveData entry =
                savedGroups[index];
            if (!TryResolveOwner(
                    party,
                    entry.OwnerId,
                    out BaseUnitEntity owner)) {
                failureReason =
                    $"The Nemesis Protocol owner for saved group index {index} could not be restored.";
                return false;
            }

            var group = new NemesisProtocolGroup(
                entry.Slot,
                owner);
            if (entry.LinkedEnemyId != null) {
                if (!context.TryResolveEnemy(
                        entry.LinkedEnemyId,
                        requireLiving: true,
                        out BaseUnitEntity linkedEnemy) ||
                    FindEnemyIndex(
                        restoredKnownEnemies,
                        linkedEnemy) < 0) {
                    failureReason =
                        $"The Nemesis Protocol target for saved group index {index} could not be restored.";
                    return false;
                }
                group.LinkedEnemy = linkedEnemy;
            }
            restoredGroups.Add(group);
        }

        for (int petIndex = 0;
             petIndex < partyAndPets.Count;
             petIndex++) {
            BaseUnitEntity pet =
                partyAndPets[petIndex];
            if (!IsEligiblePet(pet) ||
                pet.Master == null) {
                continue;
            }

            for (int groupIndex = 0;
                 groupIndex < restoredGroups.Count;
                 groupIndex++) {
                NemesisProtocolGroup group =
                    restoredGroups[groupIndex];
                if (!ReferenceEquals(
                        group.Owner,
                        pet.Master)) {
                    continue;
                }
                if (FindPetIndex(
                        group.Pets,
                        pet) < 0) {
                    group.Pets.Add(pet);
                }
                break;
            }
        }

        KnownEnemies = restoredKnownEnemies;
        Groups = restoredGroups;
        int linkedGroupCount = 0;
        for (int index = 0;
             index < restoredGroups.Count;
             index++) {
            NemesisProtocolGroup group =
                restoredGroups[index];
            SynchronizeGroupState(group);
            if (group.LinkedEnemy != null) {
                linkedGroupCount++;
            }
        }

        if (linkedGroupCount > 0) {
            EncounterHud.Show(
                DisplayName,
                Description);
        } else {
            EncounterHud.Hide();
        }
        return true;
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
        List<NemesisProtocolGroup> groups,
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
        List<NemesisProtocolGroup> groups) {
        for (int index = 0;
             index < groups.Count;
             index++) {
            SynchronizeGroupState(
                groups[index]);
        }
    }

    private static void RebalanceNemesisAssignments(
        List<NemesisProtocolGroup> groups,
        List<BaseUnitEntity> knownEnemies) {
        int linkedGroupCount = 0;
        for (int index = 0;
             index < groups.Count;
             index++) {
            NemesisProtocolGroup group =
                groups[index];
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

                SynchronizeGroupState(group);
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
        List<NemesisProtocolGroup> groups,
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
        List<NemesisProtocolGroup> groups,
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
            "Nemesis Protocol could not resolve the selected available enemy.");
    }

    private static bool IsEnemyLinked(
        List<NemesisProtocolGroup> groups,
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

    private static int FindPetIndex(
        List<BaseUnitEntity> pets,
        BaseUnitEntity pet) {
        for (int index = 0;
             index < pets.Count;
             index++) {
            if (ReferenceEquals(
                    pets[index],
                    pet)) {
                return index;
            }
        }

        return -1;
    }

    private static bool TryResolveOwner(
        List<BaseUnitEntity> party,
        string ownerId,
        out BaseUnitEntity owner) {
        owner = null;
        for (int index = 0;
             index < party.Count;
             index++) {
            BaseUnitEntity candidate = party[index];
            if (IsEligibleOwner(candidate) &&
                string.Equals(
                    candidate.UniqueId,
                    ownerId,
                    StringComparison.Ordinal)) {
                owner = candidate;
                return true;
            }
        }

        return false;
    }

    private static System.Random GetAssignmentRandom() {
        System.Random random = AssignmentRandom;
        if (random == null) {
            random = new System.Random();
            AssignmentRandom = random;
        }

        return random;
    }

    private static void SynchronizeGroupState(
        NemesisProtocolGroup group) {
        BaseUnitEntity linkedEnemy =
            group.LinkedEnemy;
        if (linkedEnemy == null) {
            UnitMarker.ClearMarker(group.Owner);
            DamageControl.ClearOffTargetDamageReduction(
                group.Owner);
            for (int index = 0;
                 index < group.Pets.Count;
                 index++) {
                UnitMarker.ClearMarker(
                    group.Pets[index]);
                DamageControl
                    .ClearOffTargetDamageReduction(
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
        DamageControl.SetOffTargetDamageReduction(
            group.Owner,
            linkedEnemy,
            OffTargetDamageReductionPercent);
        for (int index = 0;
             index < group.Pets.Count;
             index++) {
            UnitMarker.SetMarker(
                group.Pets[index],
                markerText,
                markerColor);
            DamageControl.SetOffTargetDamageReduction(
                group.Pets[index],
                linkedEnemy,
                OffTargetDamageReductionPercent);
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
                    "Nemesis Protocol requires a group slot from 1 through 6.");
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
                    "Nemesis Protocol requires a group slot from 1 through 6.");
        }
    }

    private static List<NemesisProtocolGroup> BuildGroups(
        List<BaseUnitEntity> party,
        List<BaseUnitEntity> partyAndPets) {
        var groups =
            new List<NemesisProtocolGroup>();
        for (int index = 0;
             index < party.Count;
             index++) {
            BaseUnitEntity owner = party[index];
            if (!IsEligibleOwner(owner)) {
                continue;
            }
            if (groups.Count == MaximumGroupCount) {
                throw new InvalidOperationException(
                    "Nemesis Protocol supports at most six eligible party owners.");
            }

            groups.Add(
                new NemesisProtocolGroup(
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
                NemesisProtocolGroup group =
                    groups[groupIndex];
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

    private sealed class NemesisProtocolGroup {
        internal int Slot { get; }
        internal BaseUnitEntity Owner { get; }
        internal List<BaseUnitEntity> Pets { get; }
        internal BaseUnitEntity LinkedEnemy { get; set; }

        internal NemesisProtocolGroup(
            int slot,
            BaseUnitEntity owner) {
            Slot = slot;
            Owner = owner;
            Pets = new List<BaseUnitEntity>();
        }
    }
}
