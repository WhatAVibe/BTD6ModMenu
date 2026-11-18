<h1 align="center">BTD6 Mod Menu</h1>

<p align="center">
  <a href="https://github.com/WhatAVibe/BTD6ModMenu/releases/latest/download/BTD6ModMenu.dll">
    <img src="https://img.shields.io/badge/Download-BTD6ModMenu.dll-2ea44f?style=for-the-badge&logo=github" alt="Download BTD6ModMenu.dll">
  </a>
  <img src="https://img.shields.io/badge/version-1.0.0-blue?style=for-the-badge" alt="Version 1.0.0">
  <a href="https://github.com/gurrenm3/BTD-Mod-Helper">
    <img src="https://img.shields.io/badge/requires-BTD6%20Mod%20Helper-yellow?style=for-the-badge" alt="Requires BTD6 Mod Helper">
  </a>
  <img src="https://img.shields.io/badge/license-GPL--3.0-lightgrey?style=for-the-badge" alt="License GPL-3.0">
</p>

**BTD6 Mod Menu** is a sandbox-first cheat menu for *Bloons TD 6*. It started as an updated
version of the classic **btd6-unlocker**, then grew into an in-game menu with insta monkeys,
powers, account unlocks, resource editing, per-tower stat editing, match cheats, hero switching
and more.

The menu is built for single-player sandboxing. Open it in game with **Insert** (you can rebind
the key in Mod Helper's settings).

> **Heads up:** modding a Bloons TD 6 profile can get your account flagged for online features.
> Use a separate account if that matters to you, and back up your profile before making big changes.

## What's inside

### Sandbox / match cheats

Everything in this section is designed for messing around in a match:

- **Infinite cash** and **infinite lives / god mode**
- **No ability cooldowns**
- **Freeze towers** (pause all weapons)
- **Place anywhere** – land, water, track and normally blocked spots
- **Ignore the normal tower count limit**
- **Unlimited 5th tiers** – build as many of the same T5 tower as you want
- **Free purchases** – free tower/power charges and 0% discounts for the match
- **Damage multiplier** from 1x up to 9999x
- **Game speed** up to 100x (uses the game's fast-forward multiplier, so turn the in-game
  fast-forward button on)
- **Round controls** – skip forward/back, or jump straight to a round
- **Spawn bloons**, clear all bloons, win the game, trigger a defeat, or sell every tower
- **Full hero switching** from the complete hero list, not just the ones already placed

### Insta Monkeys

- Gives instas for **every base tower**, including newer towers such as **Skywarden**,
  **Mermonkey** and **Desperado**. Towers are discovered from the game's tower set, so future
  towers should show up without a code change.
- There are **64 valid insta variants per tower** (0-0-0 plus every legal crosspath).
- Choose a default quantity, override it per tower, then use:
  - **Give** to add to every variant,
  - **Set Var** to set every variant to a value,
  - **Exact** to put one exact total into a single 0-0-0 insta.
- Per-tower overrides are saved between sessions.

### Powers

- Gives **all powers**, including event and Pro powers.
- Configurable quantity per power, plus per-power Give/Set buttons.
- Duplicate `...Pro Power` entries are filtered out so you only see the real power.
- **Unlock All Powers Pro** maxes XP and tiers for every power.

### Unlocks and resources

- Monkey money, tower XP, trophies and all monkey knowledge
- Every trophy store item
- All towers, heroes, upgrades and paragons
- Knowledge refund
- Manual profile save

### Tower editor

In the **Towers** tab, hit **Edit** on any placed tower:

- **Range** x2 / x5 / x10
- **Damage** x2 / x5 / x10
- **Attack speed** x2 / x5
- **Pierce** x2 / x5
- **Pops** presets (1K / 1M / 10M / 100M)
- **Paragon Level** presets (only shown for paragon towers)
- **Reset Buffs**

Buffs are stored per tower, shown as a tag in the tower list (for example `[R2 D5 S2 P2]`), and
are re-applied automatically after an upgrade.

## Install

1. Install **[MelonLoader](https://melonloader.co/)**.
2. Install **[BTD6 Mod Helper](https://github.com/gurrenm3/BTD-Mod-Helper/wiki/Install-Guide)**.
3. Download **BTD6ModMenu.dll** with the button above.
4. Drop the DLL into `BloonsTD6/Mods/`.
5. Launch the game, click through the modded-client warning, and press **Insert** in the menu or
   in a match.

## Hotkeys

| Key | Action |
| --- | --- |
| **Insert** | Open / close the mod menu |
| F1 | Give all insta monkeys |
| F2 | Unlock all monkey knowledge |
| F3 | Add monkey money + tower XP |
| F4 | Add trophies |
| F5 | Unlock all trophy store items |
| F6 | Give all powers |
| F7 | Unlock all Powers Pro tiers |
| F8 | Free — not used by this mod |

All hotkeys and quantities are also available in **Mods → BTD6 Mod Menu** under Mod Helper's
settings.

## Build from source

The project targets **.NET 6** and references the game's generated IL2CPP assemblies.

```bash
dotnet build BTD6ModMenu.csproj
```

By default it looks for the game at:

```text
C:\Program Files (x86)\Steam\steamapps\common\BloonsTD6
```

Override that with `GameDir` or the `BTD6_DIR` environment variable:

```bash
dotnet build BTD6ModMenu.csproj -p:GameDir="D:\SteamLibrary\steamapps\common\BloonsTD6"
```

Output: `bin\Debug\BTD6ModMenu.dll`.

## Notes and limits

- **Unlimited 5th tiers** is applied when a match is created. Toggle it before starting a match.
- **Game speed** only changes the fast-forward multiplier. Turn on the in-game fast-forward
  button for it to take effect.
- **Range** is capped at 30 to keep the range-circle calculation from stuttering on very large
  values.
- **Damage** editing walks the tower's damage models. A few towers use unusual damage behaviors
  and may not respond to the per-tower damage button; the global Damage Multiplier is the
  fallback.
- The **hero switch** uses the current match's hero inventory, so it works during a match rather
  than changing your pre-match hero selection.
- This mod is for single-player sandboxing. Do not use it in public co-op or on leaderboards.

## Credits

This project would not exist without these:

- **[BTD6 Mod Helper](https://github.com/gurrenm3/BTD-Mod-Helper)** by gurrenm3 and doombubbles —
  **required** for this mod to load and for the UI components it uses.
- **[btd6-unlocker](https://github.com/Interesting-exe/btd6-unlocker)** by Interesting — the
  original project this menu is based on. The insta monkey, knowledge, money/XP, trophy and
  trophy-item features started there.
- **[InGameHeroSwitch](https://github.com/doombubbles/InGameHeroSwitch)** by doombubbles — the
  in-match hero switching approach.
- **[FasterForward](https://github.com/doombubbles/FasterForward)** by doombubbles — the
  `TimeHelper.OverrideFastForwardTimeScale` game-speed approach.
- **[Unlimited5thTiers](https://github.com/doombubbles/Unlimited5thTiers)** by doombubbles — the
  tier-5 capacity patch.

More detail is in [CREDITS.md](CREDITS.md). Because this project incorporates GPL-3.0 code from
the doombubbles mods above, the combined work is licensed under **GPL-3.0**. The original
btd6-unlocker code is MIT; its notice is preserved in the credits.

## License

[GNU General Public License v3.0](LICENSE)

Bloons TD 6 is a trademark of Ninja Kiwi. This project is unofficial and is not affiliated with
or endorsed by Ninja Kiwi.
