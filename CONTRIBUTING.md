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

The `.slnx` solution needs the .NET SDK 9.0.200 or newer. With an older SDK (8 works for every project), build
one project at a time instead: `dotnet build mods\SandPower\SandPower.csproj -c Release`.

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
- **Models from Blender:** `src/GarageLib/MeshModel.cs` loads a mesh exported with
  `shared/blender/export_mesh_model.py` from an embedded resource and maps Blender material names onto the
  game's materials. Ms Choppa is the worked example.
- **Bump `Version`** in your `Plugin.cs` and add a changelog line to your README when you change something players
  will get.

## New structure mod (StructureLib)

Buildings, machines, cargo and power sources go in `mods/<Name>/` on top of
[StructureLib](src/StructureLib/README.md). [`mods/SandPower`](mods/SandPower) is the worked example; copy its shape:

- `<Name>.csproj` with `AssemblyName` = the folder name and a `ProjectReference` to
  `$(RepoRoot)src\StructureLib\StructureLib.csproj` with `Private="false"`.
- `Plugin.cs` with `public const string Guid` (`com.yourname.<name>`) and `public const string Version`,
  `[BepInDependency(StructureLib.Plugin.Guid)]`, and every `MoonStructures.Register` / `RegisterCargo` call in
  `Load()`. `package.ps1` picks the folder up from the `Version` constant.
- A `README.md` with the numbers players see and a changelog.
- Add the project to `GarageLib.slnx` (folder `/mods/`) and the package name to `modpack.json` to ship it in the
  modpack.

Things to know:

- **Ids are forever.** The plugin GUID, every structure id (`yourname.structure`) and every cargo id and value can't
  change after a release: saves and co-op peers refer to them.
- **Claim cargo values.** A new cargo needs a byte value in 3..255 claimed in
  [`docs/cargo-registry.md`](docs/cargo-registry.md) before you use it; two mods with the same value are both dropped.
- **Start from a vanilla structure.** `BaseStructure` lends the model, footprint and build rules. A machine needs a
  donor with a production view (the stone press, the pressure chamber; for a generator, the solar post).
- **Know the limits.** The [StructureLib status table](src/StructureLib/README.md#status) lists what works, what is
  unsupported (walker loading, fuel generators feeding vanilla machines) and what is untested (belts, co-op).
- **Test with the dev self-test.** Dev builds of StructureLib add a `[Dev]` section to
  `BepInEx/config/com.ryanhartwig.structurelib.cfg`: `AutoLoadSlot`, `AutoBuild` (structure ids or vanilla kinds),
  `AutoFeed` (load cargo into a built machine) and `SelfTest`, which logs `[selftest] PASS` / `FAIL` lines.

## Testing

- Saves live in `%USERPROFILE%\AppData\LocalLow\DragDropGames\MoonSweepers\MoonSessions\slot-N`. GarageLib keeps
  modded vehicles in `garagelib.json` inside the slot folder. Copying a slot to test on is a good habit, not a rule.
- Logs: `BepInEx/LogOutput.log`. GarageLib logs every step it takes with a vehicle (`[Info :GarageLib]`).
- Dev builds (the default) add a `[Dev]` section to `BepInEx/config/com.andrew1431.garagelib.cfg` with helpers to
  auto-load slot 2 and auto-buy vehicles by id.

## Pushing

Maintainers push straight to `main`; pull first, and pull again before you push. PRs are welcome but optional.
Claim the work on the [work-order board](https://claude.ai/artifact/Wam9isLuKoJkHGHSi3pVJx) first so two people (or
two AI agents) don't edit the same files; [CLAUDE.md](CLAUDE.md) has the protocol. Ask Andrew for Editor access.

One mod per commit (or PR) keeps history readable. Don't commit `bin/`, `obj/`, `dist/` or `*.blend1`. Large binaries
(blend files, exported meshes) are fine while they stay under a few MB each.
