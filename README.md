# BetterSewerKeys

BetterSewerKeys transforms the sewer key system from a single-use master key into a strategic per-entrance unlocking system. Instead of one key unlocking all sewer entrances, each entrance now requires its own key.

## What Changed?

### Vanilla Behavior
- Using a single sewer key unlocks **all** sewer entrances globally
- One key = access to the entire sewer network

### With BetterSewerKeys
- Each sewer entrance requires its own key
- Keys unlock only **one** entrance at a time
- You must strategically choose which entrances to unlock
- Multiple methods to obtain keys: buying, pickpocketing, or finding them in the world

## Features

### Per-Entrance Unlocking
- Each sewer entrance has its own unique key
- Keys are consumed when used and unlock only the specific entrance you interact with
- Once an entrance is unlocked, it remains open permanently (no key needed for future access)

### Multiple Key Acquisition Methods

#### 1. **Buying from Jen**
- Purchase keys from Jen (NPC) if you have sufficient relationship status
- Each purchase gives you a key for the next locked entrance
- Keys can be bought sequentially until all entrances are unlocked

#### 2. **Pickpocketing**
- Random NPCs are assigned as key possessors for different entrances
- Use your pickpocketing skills to steal keys from these NPCs

#### 3. **World Pickups**
- Random sewer keys spawn at various locations throughout the world
- Keys relocate to new positions each day
- The pickup system continues until all entrances are unlocked

### Save System Integration
- Full integration with the game's save system via S1API
- Unlock states persist across saves
- Automatic migration from vanilla saves (if you had previously unlocked sewers, all entrances will be unlocked in the migrated save)
- Key distribution and pickup locations are tracked per save

## Installation

1. Ensure you have MelonLoader installed for Schedule I
2. Place the `BetterSewerKeys_Il2cpp.dll` or `BetterSewerKeys_Mono.dll` file in your `Mods/` folder
3. Launch the game - the mod will automatically initialize

## Requirements

- Schedule I (ScheduleOne)
- MelonLoader
- S1API (for save system integration)

## Notes

- The mod uses the same sewer key item as vanilla - the difference is how they're consumed (one entrance vs. all)
- Once an entrance is unlocked, you can access it freely without needing another key
- All entrances must be unlocked individually - there's no "master key" anymore
- The random world key pickup will continue spawning until all entrances are unlocked