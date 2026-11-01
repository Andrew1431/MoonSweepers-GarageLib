# Contributing a vehicle

## One-time setup

1. Windows, the .NET 8 SDK (or newer) and Moon Sweepers on Steam.
2. Install BepInEx 6 IL2CPP be.791 and the MoonSweepers.ModEngine patcher, and launch the game once so
   `BepInEx/interop` gets generated. The build compiles against those DLLs.
3. If the game isn't in `C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers`, either set the
   `MOONSWEEPERS_DIR` environment variable or create `Directory.Build.local.props` (gitignored) in the repo root:

   ```xml
   <Project><PropertyGroup><GameDir>D:\SteamLibrary\steamapps\common\Moon Sweepers</GameDir></PropertyGroup></Project>
   ```

4. Build everything once: `dotnet build GarageLib.slnx -c Release`. Each build copies the DLLs into
   `BepInEx/plugins/<Name>/`, so you only need to launch the game afterwards (close it first: Windows locks the DLLs).

## New vehicle

```powershell
.\new-vehicle.ps1 -Name RoverX -Author yourname -Title "Rover X"
```

That creates `vehicles/RoverX/` with a `Plugin.cs` that registers a vehicle, and adds it to the solution.
Things to know:

- **Ids are forever.** `Guid` (`com.yourname.roverx`) and `VehicleId` (`yourname.rover-x`) can't change after a
  release: players' configs and saves refer to them.
- **Start from a vanilla vehicle.** `BaseVehicle` lends the model, seats, card picture and upgrade rules. Override only
  what you need: stats, `DecorateModel` to add parts, `BuildModel` to replace the model, `Controller` for custom
  driving. The full list is in [`VehicleDefinition.cs`](src/GarageLib/VehicleDefinition.cs) and
  [the library README](src/GarageLib/README.md).
- **Card pictures are automatic.** GarageLib renders your model for the garage card. Set `CardIconPng` if you want
  a hand-made one.
- **Custom controllers apply gravity themselves.** The moon is a sphere centred on the origin, so "up" is
  `position.normalized`, and gravity is `MoonSettings.gravity`. See `examples/ExampleVehicle` (hover) and
  `vehicles/MsChoppa` (flight).
- **Models from Blender:** `shared/Rotorcraft/MeshModel.cs` loads a mesh exported with
  `shared/Rotorcraft/tools/export_mesh_model.py` from an embedded resource and maps Blender material names onto the
  game's materials. Ms Choppa is the worked example.
- **Bump `Version`** in your `Plugin.cs` and add a changelog line to your README when you change something players
  will get.

## Testing

- **Never test on your main save.** Copy a slot first: `%USERPROFILE%\AppData\LocalLow\DragDropGames\MoonSweepers\MoonSessions\slot-1`
  → `slot-2`, and play on slot 2. GarageLib keeps modded vehicles in `garagelib.json` inside the slot folder.
- Logs: `BepInEx/LogOutput.log`. GarageLib logs every step it takes with a vehicle (`[Info :GarageLib]`).
- Dev builds (the default) add a `[Dev]` section to `BepInEx/config/com.andrew1431.garagelib.cfg` with helpers to
  auto-load slot 2 and auto-buy vehicles by id.

## Pull requests

Claim the work on the [work-order board](https://claude.ai/artifact/Wam9isLuKoJkHGHSi3pVJx) first so two people (or
two AI agents) don't edit the same files; [CLAUDE.md](CLAUDE.md) has the protocol. Ask Andrew for Editor access.

One vehicle per PR is easiest to review. Don't commit `bin/`, `obj/`, `dist/` or `*.blend1`. Large binaries
(blend files, exported meshes) are fine while they stay under a few MB each.
