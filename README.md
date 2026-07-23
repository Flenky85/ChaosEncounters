# Chaos Encounters

**Chaos Encounters** is a mod for *Warhammer 40,000: Rogue Trader* that randomizes combat encounters by adding a mechanic that changes how each battle must be approached.

The goal is to prevent every fight from being solved with the same strategy and force the player to adapt to the specific rules of each encounter.

In short: less routine, more chaos, and the occasional tactical plan sent straight to hell.

## How Encounters Work

When combat begins, Chaos Encounters captures the initial hostile roster and classifies the encounter from its enemy rank composition.

- **Common only:** no unique highest-ranked enemy exists, the strongest enemies are tied, or the encounter contains only Swarm-ranked enemies.
- **Common and Boss:** one enemy is the unique highest-ranked unit and its rank is Common, Hard, or Elite.
- **Boss only:** one enemy is the unique highest-ranked unit and its rank is MiniBoss, Boss, or ChapterBoss.

When Boss mechanics are available, the unique highest-ranked enemy becomes the encounter leader.

At the first valid combat round, one enabled and compatible mechanic is selected at random. Only one mechanic can be active per encounter.

The mechanic remains active until combat ends, it is manually disabled, or a runtime failure forces cleanup. Any modifiers, markers, and HUD elements created by the mod are then removed.

## Common Mechanics

### The Execution List

Every enemy is assigned a position on the Execution List. Position 1 has 0% damage reduction, position 2 has 20%, position 3 has 40%, position 4 has 60%, position 5 has 80%, and positions 6 or higher are immune with 100% damage reduction. When an enemy dies, every enemy behind it moves up one position and its damage reduction is updated accordingly, bringing each survivor one step closer to execution.

### Rising Vengeance

Every fallen enemy strengthens those left behind. When an enemy dies, all surviving enemies, including reinforcements, gain marks equal to the defeated unit's rank, from I to VI, accumulating up to 20. Each mark grants +1% damage dealt and 4% damage reduction, and each newly gained mark restores 5% of maximum health. At the end of every round, each enemy loses half of its marks, rounding the number lost down. Example: a rank II death grants 2 marks; a rank IV death grants 4 marks.

### The Equalizer

All enemies share a single health pool. Damage dealt to any enemy is redistributed across the group, prioritizing those with the highest remaining health percentage. No enemy can fall below 1 HP until the shared pool is depleted. When the pool reaches 0%, every remaining enemy dies.

## Boss Mechanics

### Tyrant's Aegis

All other enemies are immune to damage while the Boss remains alive. Kill the Boss to break their protection.

### Wall of Flesh

The Boss uses its followers as a living shield and remains invulnerable until they are all dead.

### The Elite Guard

The Boss and two chosen Guards form an elite defensive unit. The Boss and both Guards begin with 60% damage reduction, which falls to 30% after the first member of the group dies and to 0% after the second. The Guards deal 30% increased damage for the entire encounter.

### Nemesis Protocol

Each character and their pets are linked to one marked enemy. They deal full damage to their linked target, but only 20% damage to other enemies. When a linked target is lost, the protocol assigns another available unlinked enemy at random. If no valid target remains, the group becomes unlinked and deals full damage to all enemies.