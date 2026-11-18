# Credits and attribution

BTD6 Mod Menu is a combined/adapted work. The projects below provided code, design or the
underlying APIs used here. Please support them.

## Required dependency

### BTD6 Mod Helper

- **Author:** gurrenm3, doombubbles and contributors
- **Repository:** https://github.com/gurrenm3/BTD-Mod-Helper
- **License:** see repository
- This mod will not load or run without Mod Helper. Its UI components, settings system,
  `TimeHelper`, IL2CPP helpers and Harmony integration are used throughout.

## Based on

### btd6-unlocker

- **Author:** Interesting
- **Repository:** https://github.com/Interesting-exe/btd6-unlocker
- **License:** MIT
- **Copyright:** Copyright (c) 2022 Interesting#4036
- The original project provided the insta-monkey, monkey-knowledge, monkey-money/XP, trophy and
  trophy-store-item features. This menu keeps those features and extends them with
  per-tower quantities, new towers, powers, a GUI, sandbox controls and more.

The original MIT notice follows:

```text
MIT License

Copyright (c) 2022 Interesting#4036

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Includes / adapts

### InGameHeroSwitch

- **Author:** doombubbles
- **Repository:** https://github.com/doombubbles/InGameHeroSwitch
- **License:** GPL-3.0
- The in-match hero switching logic (current hero through the bridge, hero inventory reset,
  shop refresh) is adapted from this mod.

### FasterForward

- **Author:** doombubbles
- **Repository:** https://github.com/doombubbles/FasterForward
- **License:** GPL-3.0
- The custom game-speed control uses the same `TimeHelper.OverrideFastForwardTimeScale` approach
  as this mod.

### Unlimited5thTiers

- **Author:** doombubbles
- **Repository:** https://github.com/doombubbles/Unlimited5thTiers
- **License:** GPL-3.0
- The unlimited 5th-tier tower patch is adapted from this mod's
  `TowerInventory.SetTowerTierRestrictions` postfix.

## License of this project

Because this project includes/adapts GPL-3.0 code from the doombubbles projects listed above, the
combined project is distributed under **GPL-3.0**. See [LICENSE](LICENSE).
