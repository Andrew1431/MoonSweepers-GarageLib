# Moon Vehicle Core — plan

A BepInEx library mod that lets other mods register vehicles that appear in the game's garage, can be bought, spawn,
drive, save and survive the mod being removed. Ms Choppa would be its first consumer.

Findings come from the interop dump (`research/dump_*.txt`) and a runtime spike (`research/spike`) run against the
cloned save in slot-2 (2026-10-31). Game: Unity 6000.6, IL2CPP metadata v108, all gameplay types in namespace
`MoonCleaners` in `Assembly-CSharp`.

## Loader stack (required by every mod)

- BepInEx 6.0.0-be.791 IL2CPP (exact build, b782da4).
- `MoonSweepers.ModEngine` preloader patcher (Nexus mod 9). It builds the interop assemblies from the live runtime,
  because Cpp2IL stops at metadata v106. Expected log noise: the Cpp2IL "got 108" error.
- `steam_appid.txt` (5335510) in the game folder makes launching the exe directly work for dev runs.

## How vehicles work in vanilla

**Identity is an `int` index**, everywhere: catalog, garage offers, owned vehicles, save file, network messages.

| Piece | Where | Notes |
|---|---|---|
| Definitions | `MoonPrototypeCatalog.Vehicles` (static `Item[]`, 22 entries) | `Item`: name, hint, work, width, depth, reach, speed, turn, capacity. Indices 2–19 are unused prototypes with no prefab. |
| Per-index stats | `MoonPrototypeCatalog.AuthoredPrefab(i)`, `StorageKg(i)`, `Heft(i)`, `MoonVehicleSeats.Count/Layout(i)`, `MoonWorld.GarageSlug(i)` | Static methods keyed by index; patchable. |
| What is for sale | `MoonWorld.GarageOffers` (static `GarageOffer[]`: vehicle, price, summary), `MoonWorld.GarageOfferFor(i)` | Vanilla sells 0 push sweeper $300, 1 ride-on $1,000, 20 lunar sweeper $2,500, 21 mega vacuum rover $6,000, 4 crew van $2,000. |
| Which vehicles a garage takes | `MoonWorld.GarageVehicleMask` = 3145747 = bits 0,1,4,20,21 | **A 32-bit mask**, so it can't name indices ≥ 32. This is the main design constraint. |
| Garage text | `MoonWorld.GarageNote` | Hard-coded list of the five vehicles and prices. |
| Buy UI | `MoonGarageMenu` (`Build`, `BuildBays`, `Row(List<GarageOffer>)`, `Render(int vehicle)` → card sprite, `Pick/Click`) | Vehicles are bought at a garage building, not in the upgrade shop. |
| Buying | `MoonWorld.TryBuyVehicle(player, garage, vehicle, out msg)` → `CrewVehicleBought` / `LocalVehicleBought` event | Co-op: `MoonCrewSession.OnVehicleBought(garage, vehicle)`. |
| Ownership | `MoonWorld.ownedVehicles: Dictionary<string, MoonOwnedVehicle>` | `MoonOwnedVehicle`: garage, vehicle, owner, inWorld, position, rotation, holder, odometer, `upgrades` (7 bytes). Key/storage id = `garage/<garage>/<vehicle>/<owner>`. |
| Spawn | `MoonWorld.SpawnHeld` / `BringOut` → `MoonPrototypeModels.Vehicle(index, parent)` → `MoonPrototypeVehicle.Initialize(owner, index, storage)` | The authored vehicles load `Resources` prefabs `Moon/Vehicles/<Name>` (`MoonAuthoredVehicleView`). The prototypes get built out of primitives. `MoonPrototypeVehicle` is the ground-vehicle controller (struts, drift, flip, cleaning, cargo). |
| Save | `MoonOwnedVehicleSave.Write/Read/ReadOne` (versions 33/34), part of `MoonLooseDustSnapshot` | On load, each `ReadOne` is followed by `GarageOfferFor(vehicle)`, which looks like validation. |
| Upgrades | `MoonWorld.TryUpgradeVehicle`, `TryGetVehicleUpgradePrice`, `MoonGarageMenu.UpgradeDetails` | Per index, 7 upgrade levels. |
| Co-op | `MoonCrewSession` (`Protocol` byte, `WriteVehicle/ReadVehicle`, `GiveVehicle/TookVehicle`, `HandleOwnedVehicles`) | Vehicle ints go over the wire as-is. |

The upgrade shop also has `Category.Vehicles` / `OfferKind.Vehicle`. I haven't yet checked what it shows.

## Library API (draft)

```csharp
MoonVehicles.Register(new VehicleDefinition {
    Id = "andrew1431.choppa",            // stable string id; this is what gets saved
    Name = "Utility choppa", Summary = "...", Hint = "W/S collective | ...",
    Price = 15000m,
    Work = MoonPrototypeCatalog.Work.Transport,
    Speed = 20f, Turn = 60f, CargoCapsules = 4, StorageKg = 400, Heft = 1f,
    Seats = new[] { /* foot positions + facing */ },
    BuildModel = parent => ...,          // GameObject from an AssetBundle or code
    CardIcon = () => sprite,             // optional; default renders the model
    Controller = typeof(ChoppaController) // optional; replaces MoonPrototypeVehicle driving
});
```

Plus events (`Spawned`, `Stored`, `Bought`) and a query API (`IsModded(int index)`, `Definition(int index)`).

## How the core does it

1. **Index allocation.** Map each string id to a runtime index once per save, and persist the map in a sidecar file
   in the slot folder (`MoonSessions/slot-N/<identity>/moonvehiclecore.json`) so indices stay the same for that save.
   - Preferred: a high range (1000+), patching every place that reads `GarageVehicleMask` so it also accepts registered
     indices. That keeps clear of vehicles the developer adds later.
   - Fallback if the mask checks are too scattered: indices 22–31 (10 slots). These fit the mask, but a game update
     that adds vehicle 22 would collide with them.
2. **Catalog injection.** After the game's static init, grow `MoonPrototypeCatalog.Vehicles` with our `Item`s. Prefix
   or postfix `AuthoredPrefab`, `StorageKg`, `Heft`, `MoonVehicleSeats.Count/Layout`, `GarageSlug` for modded indices.
3. **Garage listing.** Append `GarageOffer`s to `MoonWorld.GarageOffers`, patch `GarageOfferFor`, and patch the mask
   checks. Append a line to `GarageNote`. Patch `MoonGarageMenu.Render` to return the definition's card sprite.
4. **Model.** Prefix `MoonPrototypeModels.Vehicle(index, parent)` so modded indices get `BuildModel(parent)` and the
   original is skipped.
5. **Controller.** By default, modded vehicles use `MoonPrototypeVehicle` with their stats, so a simple wheeled
   vehicle needs no code. If `Controller` is set, the core disables `MoonPrototypeVehicle`'s driving
   (Update/FixedUpdate prefixes return false for that instance) and adds the custom component. It keeps the game's
   enter/exit, seats, cargo and storage working. The choppa plugs in big-choppa's flight model here.
6. **Save safety (most important).** Prefix `MoonOwnedVehicleSave.Write` to remove modded entries from the snapshot,
   and write them to the sidecar instead. Postfix `RestoreOwnedVehicles` to add them back. The vanilla save then never
   contains an unknown index, so uninstalling the core or a vehicle mod leaves a clean save. If a definition is missing
   on load (vehicle mod removed, core still installed), keep the record as "orphaned". Show it in the fleet page with a
   refund, and never delete it silently.
7. **Co-op.** Use a core handshake, keeping it separate from `MoonCrewSession.Protocol`. The host sends its registry
   (id, index, mod version). A client missing a vehicle mod gets a clear message, plus a placeholder box model so the
   world stays consistent. The host is authoritative for purchases, as in vanilla.

## Milestones

- **M0 – Research spike (done).** Loader works, data model mapped, slot-2 loads under automation.
- **M1 – One injected test vehicle.** Clone the ride-on sweeper's stats with a plain box model, at index 22. It should
  appear in the garage and be buyable, then spawn and drive. This answers whether growing the arrays works and where
  the mask is read.
- **M2 – Save sidecar.** Strip and restore on save and load. Test: buy, save, quit, remove the mod, load (vanilla is
  clean), reinstall (vehicle is back).
- **M3 – Public API and models.** `VehicleDefinition`, AssetBundle model loading, card icon rendering, seats.
- **M4 – Custom controller hook.** Ms Choppa flies using big-choppa's flight model.
- **M5 – Co-op.** Handshake, registry sync, purchase/spawn relay, late joiners.
- **M6 – Release.** Nexus page (the community is on Nexus), example vehicle mod, docs, dependency note
  (BepInEx be.791 + ModEngine patcher).

## Open questions / risks

- Where `GarageVehicleMask` is read. The answer decides the 1000+ range vs 22–31 (M1).
- What vanilla does with an unknown vehicle index on load: drop, crash, or keep. Sidecar stripping makes this moot for
  our saves, but it matters if someone removes the core mid-session.
- Whether the static array properties (`Vehicles`, `GarageOffers`) have usable setters through interop, or need the
  backing static field written directly.
- Upgrades for modded vehicles: what the 7 upgrade bytes mean per vehicle, and how prices come from
  `TryGetVehicleUpgradePrice`.
- Game updates: index shifts, renamed members. The core should check member presence at startup and disable itself
  cleanly with a log line rather than half-patch.
- Existing Nexus mods that add shop items (Titan Broom, Mass Driver) may already have solved parts of this. Their
  DLLs are worth a look, but downloading them needs a Nexus login.
