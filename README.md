# MoonSweepers-GarageLib

A suite of end-game enhancement mods for [Moon Sweepers](https://store.steampowered.com/): new vehicles, and over
time new buildings and mechanics. Every mod is its own optional package; the modpack bundles them all.

**GarageLib** is the shared vehicle library: it sells modded vehicles at the garage, spawns them, keeps them out of
the vanilla save (so uninstalling never breaks a save) and hands custom controllers the driving.

Maintainers: Andrew (Andrew1431) and Ryan (ryanhartwig), with equal say on what goes in and on merging.

| Package | What | Folder |
|---|---|---|
| GarageLib | The library. Required by every vehicle. | [`src/GarageLib`](src/GarageLib) |
| Ms Choppa | Flying utility helicopter, $12,000. Paused: not in the modpack or dev installs for now. | [`vehicles/MsChoppa`](vehicles/MsChoppa) |
| StructureLib | The structure library: modded buildings, machines, cargo and fuel power. Required by structure mods. | [`src/StructureLib`](src/StructureLib) |
| Belt Boxes | Compact box splitter and box merger: one belt into up to three, or up to three into one. | [`mods/BeltBoxes`](mods/BeltBoxes) |
| Modpack | GarageLib, Late Game Vehicles, StructureLib, Belt Boxes and the other mods in one zip. | [`modpack.json`](modpack.json) |

## Installing (players)

Easiest: from the newest **Modpack** release in [Releases](../../releases), download `MoonSweepersMods-Installer.zip`,
unzip it, close the game and run **Install or update.bat**. It installs BepInEx, walks you through the one manual step
(the ModEngine patcher is only on Nexus), installs the modpack and makes a **Moon Sweepers (modded)** desktop shortcut
that updates the mods and starts the game modded. Starting from Steam gives the plain game, like r2modman.
To remove everything, run **Uninstall mods.bat**. Details: [installer/README.md](installer/README.md).

Manual: install BepInEx 6 IL2CPP (be.791) and the MoonSweepers.ModEngine patcher (Nexus), launch the game once, then
unzip the **modpack** zip (or single packages such as GarageLib + LateGameVehicles) into the game folder.

Saves: modded vehicles are stored in `garagelib.json` next to the save, not in the save itself. Remove a vehicle
mod and its vehicles wait in that file until you reinstall it.

## Repo layout

```
src/GarageLib/          the library
src/StructureLib/       the structure library (buildings, machines, cargo, power)
vehicles/<Name>/        one folder per vehicle mod (own Plugin.cs, version, README, blend files)
mods/<Name>/            any other mod: buildings, mechanics, tweaks (same shape: Plugin.cs with Version, README)
mods/BeltBoxes/         box splitter and box merger (StructureLib routers)
shared/Rotorcraft/      helicopter flight model, compiled into vehicles that fly
examples/ExampleVehicle two test vehicles showing the API (not released)
examples/ExampleStructure test structures showing the StructureLib API (not released)
tests/StructureLib.Tests unit tests for StructureLib's game-free logic
templates/Vehicle|Mod|Lib/ what new-package.ps1 copies
research/               interop dumps and design notes from reverse engineering the game
```

## Making a package

See [CONTRIBUTING.md](CONTRIBUTING.md). Short version:

```powershell
.\new-package.ps1 -Kind vehicle -Name RoverX -Author yourname
.\dev.ps1 -Only RoverX -Tail   # close the game, build, install, launch, follow the log
```

## Releasing

`.\package.ps1` builds release DLLs and zips each package plus the modpack into `dist/`. `.\package.ps1 -Release`
also creates a GitHub release for every package version that doesn't have one yet. Each package is tagged on its own
(`garagelib-v0.1.0`, `mschoppa-v0.1.0`), and the modpack is tagged `modpack-v<version>` from `modpack.json`.
