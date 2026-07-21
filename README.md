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

Enemies are placed in a random ordered list. Damage reduction increases with position: 0%, 20%, 40%, 60%, 80%, and 100% from position six onward. When an enemy dies, all later enemies advance. Reinforcements are appended to the end.

### Rising Vengeance

Each enemy death grants all surviving enemies marks based on the defeated unit's rank. Every mark provides +1% damage, 4% damage reduction, and restores 5% maximum health when gained. Marks are capped at 20 and half are removed at the end of each round.

### The Equalizer

All enemies share one combined health pool. Damage is redistributed across the group, prioritizing units with the highest remaining health percentage. No member can fall below 1 HP while pool health remains; when the pool is depleted, all remaining members die. Reinforcements are added to the pool.

## Boss Mechanics

### Tyrant's Aegis

All enemies except the Boss are immune to damage while the Boss remains alive. Reinforcements receive the same protection. Killing the Boss removes the effect.

### Wall of Flesh

The Boss is immune to damage while any subordinate remains alive. Killing every subordinate removes the protection. New reinforcements can restore it while the Boss is still alive.

### The Elite Guard

The Boss and two randomly selected initial enemies form a defensive group. All three begin with 60% damage reduction, reduced to 30% after the first group member dies and removed after the second. Both Guards deal 30% increased damage for the entire encounter.
