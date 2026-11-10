# Roaming Fish Previews

An Animal Crossing-inspired fishing overhaul for **STORY OF SEASONS: Grand
Bazaar** on PC. It adds a finite, location-accurate population of visible fish
shadows without changing the game's inventory or save files.

## Features

- Builds independent schools from the game's real `FishingPossible` water cells
- Uses the native date, weather, wind, rod, bait, and habitat lottery
- Scales each pond's finite population from its connected water area
- Loads nearby pools early and retains their state when visuals unload
- Lets fast fishing gradually drain a pond, followed by slow staggered recovery
- Makes the nearest eligible shadow pursue the bobber and become the actual catch
- Adds 1-3 visible test nibbles before the final native bite
- Keeps off-target casts waiting and safely cancelable, with no phantom catch animation
- Scatters nearby fish when one notices a hook
- Scatters shoreline fish when the player runs, while walking remains quiet
- Applies a head-anchored, speed- and turn-responsive spine bend to writable meshes

The shadows communicate size/model class rather than revealing the exact
species, matching the game's own shared fish-shadow assets.

## Requirements

- Steam version of STORY OF SEASONS: Grand Bazaar
- A current BepInEx 6 Unity IL2CPP x64 build
- The game's generated BepInEx interop assemblies

The old `BepInEx 6.0.0-pre.1` release is not suitable for this Unity 6 game.

## Installation

1. Close the game.
2. Install and initialize BepInEx 6 Unity IL2CPP if it is not already installed.
3. Download `RoamingFishPreviews-v0.15.0.zip` from Releases.
4. Extract the ZIP into the game directory so the included `BepInEx` folder
   merges with the existing one.
5. Start the game and confirm `Roaming Fish Previews 0.15.0 loaded` appears in
   `BepInEx/LogOutput.log`.

Removing `BepInEx/plugins/RoamingFishPreviews` and the corresponding config file
fully uninstalls the mod. It does not modify saves or original game assets.

## Configuration

The supplied preset creates 4-12 fish per connected pool, keeps at most 18
visuals loaded, and restores one missing fish every 120 seconds. Important
settings include:

- `StrictFiniteMode`: only a displayed fish can produce a bite
- `SelectionRadius`: bobber targeting distance
- `BodyFlex`: base tail-wave amplitude
- `RunScatterRadius`: shoreline running disturbance range
- `RespawnSeconds`: staggered population recovery time

The config is generated at
`BepInEx/config/com.vic.sosgb.roamingfishpreviews.cfg`.

## Optional full writable-mesh flex

Four native mesh classes can normally be cloned directly. One shipped mesh has
Unity Read/Write disabled. The repository and releases intentionally exclude
geometry extracted from the game.

Owners of the game can generate the five small writable payloads locally:

```powershell
python -m pip install -r tools/requirements-extraction.txt
python tools/extract_writable_fish_meshes.py --game "C:\Program Files (x86)\Steam\steamapps\common\STORY OF SEASONS Grand Bazaar" --output "C:\Program Files (x86)\Steam\steamapps\common\STORY OF SEASONS Grand Bazaar\BepInEx\plugins\RoamingFishPreviews\meshes"
```

Without these optional files, the fishing overhaul remains functional; only
CPU-side body flex on the non-readable native class is unavailable.

## Building

Point `GameDirectory` at a BepInEx-enabled installation and build with .NET 6:

```powershell
dotnet build src/RoamingFishPreviews/RoamingFishPreviews.csproj -c Release -p:GameDirectory="C:\Program Files (x86)\Steam\steamapps\common\STORY OF SEASONS Grand Bazaar"
```

To produce the distributable ZIP:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-release.ps1
```

See [reverse-engineering findings](docs/FISH_SHADOW_FINDINGS.md) for the native
fishing lifecycle, asset structure, location logic, and catch synchronization.

## Disclaimer

This is an unofficial fan-made mod. STORY OF SEASONS: Grand Bazaar and its
assets belong to their respective owners. This project is not affiliated with
or endorsed by Marvelous.
