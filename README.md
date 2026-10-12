# Celeste Over It

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for **Getting Over It with Bennett Foddy** that replaces Diogenes with **Madeline from Celeste**: Celeste's movement code, constants and feel, on Getting Over It's mountain.

Madeline's sprites, animations, hair and sound effects are loaded **at runtime from your own Celeste install**. This repository contains no game assets.

## Features
- **Celeste movement**: a port of Celeste's `Player` Normal / Dash / Climb states, running at 60 Hz in integer pixels with sub-pixel remainders like Monocle's `Actor`:
  - running, variable-height jumps, coyote time, the 0.08 s input buffer
  - half gravity at the apex, fast-fall, ducking
  - 8-way dash with freeze frames, super/hyper jumps and wavedashes
  - wall slide, wall jump, climb, climb-hop, climb-jump, stamina
  - upward and dash corner corrections
- **Real Celeste visuals**: animations from `Sprites.xml`, physics hair with bangs and per-frame offsets, the outline, dash-colour hair, tired flash and dash after-images.
- **Real Celeste sound**: FMOD events from Celeste's own banks (see [How it works](#how-it-works)).
- **Built for Getting Over It's terrain**:
  - She walks up slopes and over small bumps.
  - Climbing follows slanted rock faces, and she tilts to match the wall.
  - Rock steeper than 75°, and corners she's barely balanced on, make her slide off.
  - She can grip a wall for 3 seconds before she slips.
  - Very tall walls get Celeste wooden ledges.
- **Strawberries and dash-refill crystals**: 5 hidden strawberries, plus a **level editor (F2)** to place your own strawberries, crystals and platforms.
- **The real ending**: jump off the antenna into the space above it. Getting Over It turns gravity off there; Madeline keeps a little (`SpaceGravity`, 5%), so you hop from asteroid to asteroid. The asteroids pull on her the same way they pull on Diogenes, and green crystals let you chain dashes. Leave through the top to roll the credits.
- **Getting Over It still works around her**: its camera, autosave/continue, progress meter and narrator all keep working. The original player body is kept (kinematic) and follows Madeline.
- **Respawn**: F5 takes you to the last safe spot, Shift+F5 to the start. Getting stuck inside rock fixes itself automatically.

## Play it
Download the latest zip from [Releases](../../releases) (also on GameBanana) and follow `CelesteOverIt_README.txt` inside it. In short: extract everything into your Getting Over It folder, make sure Celeste is installed, and start the game.

**Controls:**
- Arrows/WASD to move, C/Space to jump, X/Shift to dash, Z/V to grab
- F5 to respawn, F2 for the level editor, F1 for help, F3 for debug info

All of this can be configured in `BepInEx/config/celesteoverit.madeline.cfg`.

## Build from source
No .NET SDK is needed. The scripts use the C# compiler that ships with Windows.

```powershell
# Getting Over It must already have BepInEx 5 (x64) installed
.\build.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\Getting Over It"
```

This compiles `src/*.cs` against the game's own Unity DLLs and installs the result to `BepInEx/plugins/CelesteOverIt`. Use `-NoInstall` to build into `bin/` only.

To make a release zip (with BepInEx and the player instructions included):

```powershell
.\package.ps1 -GameDir "...\Getting Over It" -BepInExZip "...\BepInEx_win_x64_5.4.23.2.zip"
```

### Source layout
| File | What it does |
|---|---|
| `src/Madeline.cs` | The Celeste physics port: states, timers, collisions, corner corrections, plus the slope, ledge-step and grip tweaks |
| `src/MadelineController.cs` | Unity side: 60 Hz stepping, rendering (sprite, hair, outline, trails, tilt), Getting Over It collision queries, player-body sync, respawn, camera zoom |
| `src/CelesteAssets.cs` | Decodes Celeste's `Gameplay` atlas (Monocle `.meta` + RLE `.data`) and builds the animations from `Sprites.xml` |
| `src/CelesteInput.cs` | Celeste-style buffered input |
| `src/CelesteAudio.cs` + `audiohost/` | Plays FMOD sound events through the helper process |
| `src/Ledges.cs` | Jump-through platforms: auto-generated wall ledges and level-editor platforms |
| `src/Level.cs` | Strawberries, refill crystals, the level file and the F2 editor |
| `src/Plugin.cs` | BepInEx entry point, config, and taking over from Diogenes |
| `src/SelfTest.cs`, `src/MapDump.cs`, `tools/` | Dev-only scripted movement test and map export (`[Dev]` config) |

## How it works
- **Taking over:** after Getting Over It finishes loading or restoring a save, the mod hides Diogenes and the hammer and freezes their joints. The `Player` rigidbody becomes kinematic and is moved to Madeline's position every physics step, so everything else in the game keeps tracking "the player".
- **Collision:** Madeline's 8×11 px hitbox is tested against the game's real `Collider2D`s through `Physics2D.OverlapBox`, on Celeste's pixel grid. Slope angles come from raycasts against the actual polygon edges.
- **Sound:** Celeste uses FMOD Studio 1.10. Celeste's FMOD DLLs are 32-bit (XNA build) or 64-bit (FNA build), while Getting Over It is 64-bit Unity. So the mod starts `CelesteAudioHost(64).exe`, a tiny helper process of the matching bitness that loads Celeste's own `fmodstudio.dll` and banks and plays events sent to it over stdin. It closes when the game closes.

## Troubleshooting
- **BepInEx doesn't load (no `BepInEx/LogOutput.log`):** check `GettingOverIt_Data/Managed` for `Copy1_*.dll` duplicates and move them out of that folder.
- **"Couldn't find Celeste":** set `CelesteDirectory` in the config.

## Credits and legal
Unofficial fan project, not affiliated with Maddy Makes Games / Extremely OK Games (Celeste) or Bennett Foddy (Getting Over It). You need to own both games. Celeste's movement constants and logic are re-implemented for interoperability. No copyrighted assets are distributed; everything is read from the player's own installation.

The mod's code is MIT-licensed (see `LICENSE`). Release zips bundle BepInEx, which is LGPL-2.1.
