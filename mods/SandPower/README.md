# SandPower

A sand compactor that presses moon sand into sand briquettes, and a briquette generator that burns them for power.
Built on [StructureLib](../../src/StructureLib/README.md), which must be installed too.

Sand briquettes are regolith sand pressed into dense bricks that burn hot in the generator's oxygen-fed kiln.

## What it adds

| | Numbers |
|---|---|
| **Sand briquette** (cargo) | 2 kg per unit, sells for $7 per unit. Cargo value 3 (see [the cargo registry](../../docs/cargo-registry.md)). Shown as a sand-coloured brick (its own model, `sand_briquette.bin`) on belts, on the ground and in the hand, in place of the game's dust capsule. |
| **Sand compactor** (Production, $800) | Built on the stone press's footprint, with its own model. Holds 16 kg of sand, presses 4 kg per 8 s cycle at 12 kW. Mass is kept: 4 kg of sand becomes 4 kg of briquettes (2 units). The output slot holds 10 kg. |
| **Briquette generator** (Power, $1,500) | Built on the solar post's footprint, with its own model. Holds 10 kg of briquettes (5 units) and burns one 2 kg briquette per 20 s cycle. Each burn adds 3 kWh to its 6 kWh battery (half of it), so two briquettes fill it; no burn starts while the battery is full. Supplies up to 10 kW to machines within 24 m, wirelessly: StructureLib machines and vanilla ones (a vanilla machine draws what its own solar grid leaves unmet), with a power arc drawn to each while the build tool is held. Its look-at panel shows its own output, battery, fuel and what it powers. |

The generator alone runs a compactor at 10 of its 12 kW (power 0.83, seen in game): a cycle takes 8 s at full power,
so at 0.83 about 9.6 s (derived, not timed). On a
vanilla solar grid the compactor draws from the grid's surplus first, then from generators in range, then from the
grid's batteries.

The generator is built on the solar post rather than the battery bank, because the battery bank prefab has no
production view (the part of a model that runs a machine).

## Belting it

Both machines take belts at their ports, like the game's own machines. Belts flow along each machine's long axis:
the input is at one end, the output at the other, so put the machine that feeds another on that machine's input side.

| Machine | Port (as the game shows it) | Where (structure-local, metres) | Takes / gives |
|---|---|---|---|
| Sand compactor | `Sand in` ("Sand compactor input") | (−4.10, 0.85, 0.00), the black hopper | Takes **moon sand** only, into its 16 kg sand slot. Any other canister waits at the end of the belt. |
| Sand compactor | `Briquettes out` ("Sand compactor output") | (3.90, 0.85, 0.00), the steel chute | Gives **sand briquettes**, one 1 kg canister about every 0.7 s while the belt's entrance is clear. |
| Briquette generator | `Briquettes in` ("Briquette generator input") | (−0.90, 0.85, 0.00), the black hopper over the firebox | Takes **sand briquettes** only, into its 10 kg slot. |

A full chain: a pressure chamber's sand output belted into the compactor's `Sand in`, and the compactor's
`Briquettes out` belted into the generator's `Briquettes in`. Nothing is loaded by hand. Checked in game (StructureLib's
self-test `sandpower-chain`, single player): 12 kg of dust loaded into the chamber, all 12 kg of sand rode into the
compactor, its briquettes rode into the generator, and the generator charged:

| From the chamber's dust load | Seconds |
|---|---|
| First sand canister on the belt | 12.0 |
| First sand into the compactor | 14.7 |
| First compactor cycle done | 24.8 |
| First briquette emitted | 25.1 |
| First briquette into the generator | 27.0 |
| First generator charge (3 kWh) | 47.7 |

The chain ran beside a vanilla solar tracker (the compactor at full power from the tracker's grid). The belts, the
briquette canisters on them and both machines' contents came back after a save and reload.

## The models

Both machines have their own models (built with `shared/blender`, scripts in [`blender/`](https://github.com/Andrew1431/MoonSweepers-GarageLib/tree/structurelib/mods/SandPower/blender)), shown in place of
the stone press and the solar post; the base structure's colliders and footprint stay.

- **Sand compactor** (`sand_compactor.bin`): a white press housing on a black skid, sand hopper at the input end,
  briquette chute at the output end.
  ![Sand compactor](https://raw.githubusercontent.com/Andrew1431/MoonSweepers-GarageLib/structurelib/mods/SandPower/blender/preview_compactor_hero.png)
  Views: [hero](https://raw.githubusercontent.com/Andrew1431/MoonSweepers-GarageLib/structurelib/mods/SandPower/blender/preview_compactor_hero.png), [side](https://raw.githubusercontent.com/Andrew1431/MoonSweepers-GarageLib/structurelib/mods/SandPower/blender/preview_compactor_side.png),
  [front](https://raw.githubusercontent.com/Andrew1431/MoonSweepers-GarageLib/structurelib/mods/SandPower/blender/preview_compactor_front.png), [rear](https://raw.githubusercontent.com/Andrew1431/MoonSweepers-GarageLib/structurelib/mods/SandPower/blender/preview_compactor_rear.png).
- **Briquette generator** (`briquette_generator.bin`): a briquette hopper over a firebox with glowing windows, a white
  boiler drum, a steel radiator stack and an exhaust stack with an orange heat band.
  ![Briquette generator](https://raw.githubusercontent.com/Andrew1431/MoonSweepers-GarageLib/structurelib/mods/SandPower/blender/preview_generator_hero.png)
  Views: [hero](https://raw.githubusercontent.com/Andrew1431/MoonSweepers-GarageLib/structurelib/mods/SandPower/blender/preview_generator_hero.png), [side](https://raw.githubusercontent.com/Andrew1431/MoonSweepers-GarageLib/structurelib/mods/SandPower/blender/preview_generator_side.png),
  [front](https://raw.githubusercontent.com/Andrew1431/MoonSweepers-GarageLib/structurelib/mods/SandPower/blender/preview_generator_front.png), [rear](https://raw.githubusercontent.com/Andrew1431/MoonSweepers-GarageLib/structurelib/mods/SandPower/blender/preview_generator_rear.png).

The `.bin` files ship next to `SandPower.dll`; the belt ports are read from them.

## What works, and what does not yet

Checked in game (the StructureLib dev self-test, single player): both structures build with their models, their ports
are the models' ports, the belt chain above runs and survives a save and reload, the compactor presses sand into
briquettes, and the generator burns briquettes into charge and powers the compactor on its own. Loading through the
game's crew load call (`CrewProcessorLoad`) works too.

Limits (StructureLib today):

- **Loading by hand does not work.** The walker's load action refuses both machines ("Not a production machine"), and
  emptying a carried canister into them is untested. Belt them instead.
- **Outputs are 1 kg canisters.** The game emits 1 kg per canister for any cargo, so a 2 kg briquette leaves the
  compactor as two 1 kg canisters; nothing is lost.
- **The models start on stand-in materials.** The game's own materials come from the player's equipment, which does not
  exist yet when a world loads, so for the first seconds both models (most visibly the generator's glow and steel) wear
  tinted stand-ins, which then become the game's materials in place.
- **No animated effects.** The stone press's moving parts and the solar post's lights are hidden with their meshes; the
  models are static.
- **The generator feeds machines within its 24 m range only.** A vanilla machine farther away on the same solar grid is not fed: the game's
  power grid totals cannot be written from a mod, so the supply is per machine, not per grid. A fed vanilla machine's status reads "Powered by Briquette generator".
- **A generator's running power draw is ignored;** it never takes power from the grid.
- **Briquettes are bricks up close only.** Within about 100 m a briquette canister shows the brick model; beyond that
  the game draws canisters instanced, and those are the plain dust capsule. The brick is a look: the capsule's
  colliders stay underneath it, so a briquette takes the same room on a belt and in the hand as any canister.
- **A modded-only group gets its own grid.** Generators and machines that no vanilla power node reaches share a
  StructureLib-owned grid, reported as a negative grid id.
- **Selling briquettes at the collector is unchecked.** The price is answered, but the deposit path is not verified.

## Checking it by hand

Not covered by the self-test; worth a look before a release:

- **Belts placed by hand.** Lay a belt with the belt tool from a pressure chamber's output onto the compactor's sand
  hopper, and from the compactor's chute onto the generator's hopper. The self-test builds belts through the game's own
  call, not the belt tool, so whether the tool snaps to these ports is unchecked.
- **The models at player scale and in the real lighting:** does the compactor read as a press and the generator as a
  kiln next to a walker; do the generator's glow and the steel parts look right once the stand-in materials have become
  the game's own; is anything floating or sunk.
- **Building both from the build menu by hand** (the real ghost and the remove tool). The build menu's own placement
  call is checked in StructureLib's self-test with the example lamp.
- **Canister input.** Empty a sand or briquette canister into either machine by hand.
- **Carrying a briquette canister.** Pick up a briquette canister (or load it on a vehicle), save, and inspect the
  save: the canister should be in `structurelib.json`, and the game should not refuse the save.
- **Co-op.** Two players with SandPower (StructureLib refuses a join when the mods differ), belts and all.

## Saves

The structures, their contents and any briquette canisters (on belts too) are kept in `structurelib.json` next to the
save, not in the save itself. Remove SandPower and they wait in that file until you reinstall it; the vanilla save
stays clean.

## Changelog

- 0.1.0: first version: the compactor and the generator with their own models and belt ports; the generator feeds vanilla machines in range, draws power arcs and shows its own look-at panel; briquettes are sand bricks on belts, on the ground and in the hand (their own model through StructureLib's `CanisterModel`; they were tinted dust capsules before).
