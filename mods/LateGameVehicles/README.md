# Late Game Vehicles

Ten new sweepers for Moon Sweepers, for after you've bought the Mega vacuum rover. They are sold in the garage next to
the vanilla vehicles. Needs BepInEx, the ModEngine patcher and GarageLib.

| Line | Vehicles | Character |
|---|---|---|
| Buggies | Lunar buggy, Dune jumper | Small hopper, very fast. For fun more than profit. |
| Wide | Titan sweeper, Colossus vacuum, Regolith leviathan, Mare harvester | Slower, ever wider: up to a 16 m strip. |
| Fast | Sprint vacuum, Streak sweeper, Comet hoover, Orbital polisher | A bit wider than the Mega each step, much quicker. |

Each step up a line sweeps more per minute than the one before it. Balance numbers are in
`BepInEx/plugins/LateGameVehicles/lines.vehicles.json`; to change one for yourself, put a `*.vehicles.json` with just
that field in `BepInEx/config` (it wins over the pack's file).

Removing the mod keeps your save loadable: GarageLib keeps modded vehicles out of the vanilla save, and they come back
when you reinstall.
