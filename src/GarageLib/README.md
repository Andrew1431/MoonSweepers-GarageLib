# GarageLib

BepInEx 6 IL2CPP library mod for Moon Sweepers. Other mods register vehicles, and the core sells them at garages, spawns them, saves them safely and hands custom controllers the driving.
Design notes and research: `../../research/VEHICLE_CORE_PLAN.md`. GUID `com.andrew1431.garagelib`.
`../../examples/ExampleVehicle/` is an example consumer with two test vehicles: a ride-on clone with a roof box and a custom upgrade, and a hover van with a custom controller.

## Using it from a vehicle mod

```csharp
[BepInDependency(GarageLib.Plugin.Guid)]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        MoonVehicles.Register(new VehicleDefinition
        {
            Id = "you.my-vehicle",          // saved; never change after release
            Name = "My Vehicle", Summary = "Garage card text", Price = 5000m,
            BaseVehicle = 4,                 // vanilla vehicle to borrow model/seats/picture/upgrade rules from
            Speed = 12f, StorageKg = 400,    // anything left unset copies the base
            Controller = typeof(MyController),   // optional, derives from VehicleController
            BuildModel = ctx => ...,             // optional; or DecorateModel to tweak the base model
            UpgradeKinds = new[] { MoonVehicleUpgrades.Kind.Speed },
            CustomUpgrades = { new CustomUpgrade { Id = "rotor", Title = "Rotor", Prices = new[] { 500m, 1500m } } },
        }, MyGuid);
    }
}
```

Register from `Load()`. The registry freezes on the first frame after plugins load.

## How it works

- **Indices.** Modded vehicles get indices from 22 up, sorted by id. Saves never contain these indices, so it doesn't matter if they shift between sessions (tested).
- **Garage listing.**
  - The core grows `MoonPrototypeCatalog.Vehicles` and `MoonWorld.GarageOffers`.
  - Static per-index lookups (prefab, seats, card picture, upgrade rules) are remapped to the base vehicle unless the definition overrides them.
  - `GarageVehicleMask` is a compile-time constant and can't be extended, but buying and spawning work without it.
- **Garage menu.** Up to 6 vehicles keep the vanilla layout. With more, the vehicles page becomes a scrolling list
  of fixed-size cards, 6 per shelf, and the upgrades page's vehicle strip scrolls sideways, 7 at a time. Selecting a card
  (keys, gamepad or click) scrolls it into view (`Patches/MenuLayout.cs`).
- **Save.**
  - When the game writes a save, modded owned vehicles and their hopper storage are taken out of the snapshot and written to `garagelib.json` in the save folder.
  - On load they are put back into the snapshot before the world restores it. The vanilla save never contains a modded vehicle.
  - If a vehicle's mod is missing, its record stays in the sidecar until the mod is reinstalled.
- **Upgrades.**
  - Vanilla kinds stay in the game's own per-vehicle array and are read through `MoonWorld.VehicleUpgradeLevel`, so mods that change upgrades apply to modded vehicles too.
  - Custom kinds go through `MoonVehicles.Upgrades` (`IUpgradeResolver`), which another mod can replace.
- **Controllers.**
  - A controller suppresses `MoonPrototypeVehicle.FixedUpdate` by default; `SuppressVanillaUpdate` is opt-in.
  - Vanilla `FixedUpdate` applies the moon's gravity (the rigidbody has `useGravity` off). A custom controller must apply radial gravity itself: the moon is a sphere centred on the origin.

## Build and test

```bash
dotnet build examples/ExampleVehicle -c Release -p:InstallToGame=true
```

That build copies both DLLs into the game's `BepInEx/plugins`. Dev builds (`DevBuild=true`, the default) add `[Dev]` config entries to `BepInEx/config/com.andrew1431.garagelib.cfg`:
- `AutoLoadSlot2`: load the cloned test save. It refuses any other slot.
- `AutoBuy`: comma-separated vehicle ids to buy.
- `AutoBringOut`, `AutoSave`, `AutoQuit`: the remaining steps of an unattended test run.
- `FillerVehicles`: register N dummy vehicles to stress the garage menu.
- `AutoOpenGarageMenu` + `DumpGarageMenu`: open the garage menu, log its UI tree and save screenshots of both pages to
  `BepInEx/cache/garagelib`.

Never test against slot-1, the primary save.

## Status (2026-10-31)

Tested on slot-2:
- Buy, spawn, bring out, save to the sidecar, reload and respawn.
- Index shift between sessions.
- Vehicle mod removed (the record stays in the sidecar).
- Custom controller attached with vanilla physics suppressed.

Not done yet:
- Custom upgrades in the garage upgrades page. The resolver and API exist, but there is no UI.
- Refund for orphaned vehicles.
- AssetBundle model helper and card icon rendering.
- Co-op registry handshake.
- Haven't tried buying through the garage UI by hand. Automated `TryBuyVehicle` works.
