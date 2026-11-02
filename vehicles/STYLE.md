# Vehicle style guide

How modded vehicles should look so they sit next to the vanilla ones. Taken from the game's store screenshots and the
materials the game builds its vehicles with (`MoonPrototypeModels`: shell, dark, orange, glass, metal, cyan).

## The vanilla look

- **Clean white panels** (slightly warm white) on every body, cab and hopper. Edges are bevelled, never razor sharp.
- **Black plastic** for everything low and functional: bumpers, skirts, wheel arches, grilles, brush housings, seats.
- **Orange accents**: one or two livery stripes along the body, fenders or lids, and *all brushes are orange*.
- **Dark tinted glass**, large windows on cabs.
- **Chunky black tyres** with light hubs. Wheels are big relative to the body.
- **Brushes**: round flat disc brushes (orange bristles, black hub, tilted forward) at the front corners, or full-width
  orange roller brushes in a black hood. The Mega vacuum rover runs three disc brushes under its nose.
- Small orange lamps, cyan accent lights, a black antenna now and then. Little decals, no busy textures.
- Shapes are semi-realistic utility machines (municipal street sweepers, airport tugs, lunar rovers), friendly and a bit
  toy-like. Not sci-fi spaceships, not rust and grime.

Vanilla sweepers for scale: Push sweeper ~1.2 m long; Ride-on ~2.2 m; Lunar sweeper ~2.6 m; Mega vacuum rover is a
street-sweeper truck ~5 m long, ~2.4 m wide, ~2.8 m tall. An astronaut is 1.8 m.

## Rules for our models

- Use `shared/blender/kit.py` (materials, parts, preview renders, export). Use only its materials: `MS_BodyWhite`,
  `MS_BlackPlastic`, `MS_OrangeMetal`, `MS_LampOrange`, `MS_Glass`, `MS_Steel`, `MS_Cyan`. The loader maps these
  names to the game's own materials, so anything else renders as a flat fallback colour.
- Metres, Z up, **nose at -Y**, wheels on the ground at Z = 0.
- **The brush span equals the vehicle's `width` stat** (the sweep strip, from `data/lines.vehicles.json`). A 16 m
  sweeper needs 16 m of brushes: fold-out booms, outrigger arms with disc brushes, gangs of rollers.
- The hopper should look big enough for its `storageKg` relative to the vanilla ones (Mega: 3000 kg box behind the cab).
- Speed reads in the silhouette: fast vehicles are low, long and wedge shaped with big rear wheels; wide vehicles are
  tall, slow, heavy, with lots of wheels or tracks.
- Keep it under ~20k triangles. One root empty named `<Name>_Root`; everything else parented under it.
- A seat or cab must exist where the driver sits (players ride these).

## Lines

- **Wide line** (Titan sweeper → Mare harvester): each tier visibly wider and heavier. More disc brushes, longer
  booms, bigger hoppers, more axles. By tier 4 it should be absurd: a crawling sweeping factory.
- **Fast line** (Sprint vacuum → Orbital polisher): each tier lower, sleeker, faster looking. Narrow intake, spoilers,
  exposed big rear wheels. By tier 4 it should look like a land-speed record car that happens to vacuum.
- **Lunar buggy line** (Lunar buggy, Dune jumper): open-frame fun runabouts. Roll cage, balloon tyres, tiny hopper.
