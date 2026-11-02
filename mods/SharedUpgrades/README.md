# Shared Upgrades

Optional BepInEx mod for Moon Sweepers. Vehicle upgrades are shared by the whole team instead of being bought again
for every player and every vehicle. GUID `com.andrew1431.sharedupgrades`. It doesn't need GarageLib, but works with
GarageLib's modded vehicles.

## Rules

- There is one level per upgrade (Speed, Hopper, Turbo, Cruise, Suction, Unload, Lights). Every owned vehicle that can
  take an upgrade has that level, up to the upgrade's vanilla maximum. Hopper, Suction and Unload stay sweeper-only.
- **Turning it on in an existing save:** each upgrade becomes the highest level anyone has on any vehicle.
- **Buying:** buy an upgrade on any vehicle in a garage's upgrades page. It raises the shared level, and every vehicle
  gets it straight away, including vehicles bought later (GarageLib vehicles too).
- **Turning it off or uninstalling:** everyone keeps the levels their vehicles have. The mod writes nothing new into
  the save; it only fills in the game's own per-vehicle levels, never above the vanilla maximum, so the save loads in
  an unmodded game.

## Price

A shared level costs the upgrade's base price × the **highest upgrade price scale among the team's owned vehicles that
can take that upgrade**, so it stays meaningful as the fleet gets bigger.

| Vehicle | Vanilla price scale |
|---|---|
| Push sweeper | 0.5 |
| Ride-on sweeper | 1 |
| Crew transport | 2 |
| Lunar sweeper | 2.5 |
| Mega vacuum rover | 3 |

Example: with a push sweeper and a lunar sweeper, Speed level 1 costs 250 × 2.5 = $625 once, instead of $125 + $625
for the two vehicles separately. Buy a mega vacuum rover and the next levels cost × 3.

Base prices per level: Speed 250 / 600 / 1500, Hopper 200 / 500 / 1200, Turbo 400 / 1200, Cruise 500,
Suction 400 / 900 / 2000, Unload 150 / 400, Lights 100 / 300. Modded vehicles use their own scale.

## Config

`BepInEx/config/com.andrew1431.sharedupgrades.cfg`:

| Setting | Default | Meaning |
|---|---|---|
| `General.Enabled` | `true` | `false`: the mod changes nothing (it still logs the levels). |
| `Pricing.ScaleMode` | `Highest` | `Highest`: as above. `Vehicle`: vanilla, the scale of the vehicle you buy on. `Fixed`: `FixedScale`. |
| `Pricing.FixedScale` | `2.5` | Scale for `ScaleMode = Fixed`. |
| `Pricing.PriceMultiplier` | `1` | Multiplies every shared price, e.g. `2` to make sharing cost more. |

## Co-op

The host decides. The host's game owns the save, so with the mod on the host every vehicle in the session shares
levels, whether the other players have the mod or not. The host advertises the mod in the Steam lobby; a client with
the mod uses shared levels and prices only when the host has it too, and plays vanilla otherwise. A client without
the mod pays vanilla prices for its own vehicle, and the host then shares that level.

Not tested in co-op yet: whether the host's raised levels reach vanilla clients' own vehicles, and whether a client's
purchase reaches the host before the next sync.

## Changelog

- 0.1.0: first version.
