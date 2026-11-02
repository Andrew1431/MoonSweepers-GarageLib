# SandPower

A sand compactor that presses moon sand into sand briquettes, and a briquette generator that burns them for power.
Built on [StructureLib](../../src/StructureLib/README.md), which must be installed too.

Sand briquettes are regolith sand pressed into dense bricks that burn hot in the generator's oxygen-fed kiln.

## What it adds

| | Numbers |
|---|---|
| **Sand briquette** (cargo) | 2 kg per unit, sells for $7 per unit. Cargo value 3 (see [the cargo registry](../../docs/cargo-registry.md)). |
| **Sand compactor** (Production, $800) | Built on the stone press. Holds 16 kg of sand, presses 4 kg per 8 s cycle at 12 kW. Mass is kept: 4 kg of sand becomes 4 kg of briquettes (2 units). The output slot holds 10 kg. |
| **Briquette generator** (Power, $1,500) | Built on the solar post. Holds 10 kg of briquettes (5 units) and burns one 2 kg briquette per 20 s cycle. Each burn adds 3 kWh to its 6 kWh battery (half of it), so two briquettes fill it; no burn starts while the battery is full. Supplies up to 10 kW to StructureLib machines within 24 m, wirelessly. |

The generator alone runs a compactor at 10 of its 12 kW (power 0.83, seen in game): a cycle takes 8 s at full power,
so at 0.83 about 9.6 s (derived, not timed). On a
vanilla solar grid the compactor draws from the grid's surplus first, then from generators in range, then from the
grid's batteries.

The generator uses the solar post's model rather than the battery bank's, because the battery bank prefab has no
production view (the part of a model that runs a machine).

## What works, and what does not yet

Checked in game (the StructureLib dev self-test, single player): both structures build, the compactor presses the
sand loaded into it into briquettes in its output slot, the generator burns 2 kg of loaded briquettes into charge, and
the compactor then runs on the generator's power alone. The self-test loads both machines through the game's crew load
call (`CrewProcessorLoad`); a player has no checked way to load them yet (see below).

Limits (StructureLib today):

- **The generator powers StructureLib machines only.** Vanilla machines on the same ground are not fed: the game's
  power grid totals cannot be written from a mod. Its "10 kW" goes to modded machines such as the compactor.
- **A generator's running power draw is ignored;** it never takes power from the grid.
- **No power line is drawn** to or from either structure.
- **A modded-only group gets its own grid.** Generators and machines that no vanilla power node reaches share a
  StructureLib-owned grid, reported as a negative grid id.
- **Loading by hand does not work.** The walker's load action refuses both machines ("Not a production machine").
- **Selling briquettes at the collector is unchecked.** The price is answered, but the deposit path is not verified.
- **Belt input is unsupported and untested.** Belting sand into the compactor or briquettes into the generator: the
  game's machine port path refuses modded machines.
- **Canister input is unsupported and untested.** Emptying a sand or briquette canister into either machine.

Untested (may work, nobody has tried it):

- **Outfeed belt.** Briquettes leaving the compactor on an outfeed belt. Whether a briquette canister on a belt keeps
  its cargo is unknown.
- **Building both from the build menu by hand** (the self-test builds them through the game's own build call).
- **Co-op.** Two players with SandPower; StructureLib refuses a join when the mods differ.

## Saves

The structures and any briquettes are kept in `structurelib.json` next to the save, not in the save itself. Remove
SandPower and they wait in that file until you reinstall it; the vanilla save stays clean.

## Changelog

- 0.1.0: first version.
