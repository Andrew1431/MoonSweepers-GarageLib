# Titan sweeper (`andrew1431.titan-sweeper`, Wide line tier 1)

A grown-up Mega vacuum rover: a three-axle municipal sweeper truck (~5.9 m long incl. brushes, 2.5 m body, 3.1 m to
the hopper lid) with a flat-fronted white cab and a hopper box that stands taller than the cab. Its gimmick is a pair
of white hydraulic outrigger arms that swing out ahead of the bumper, each carrying a big 1.2 m orange disc brush, so
together with the Mega's three nose brushes it sweeps a 5.37 m strip, more than twice the body width. It reads as the
first step of the Wide line: same friendly street-sweeper language as the Mega, but heavier (tandem rear axle, six
chunky tyres), a 7500 kg hopper roughly 2.5x the Mega's box, and brushes poking well past its own wheels.

## Parts
- Black chassis rails, side skirts, cab steps, front and rear bumpers
- Two-tier white cab: large dark windscreen and side windows, lower door windows, orange belt stripe, mirrors on arms,
  black-trimmed beacon bar with two orange beacons and a cyan strip; driver seat and steering wheel inside
- Grille, orange headlights, cyan bumper lamps
- Hopper 2.5 x 3.3 x 1.95 m: white lid with four orange ribs, black lip and base band, twin orange side stripes,
  side access hatches, black rear tipping door with orange chevrons, steel tipping rams, tail and corner lamps,
  steel rear ladder to the lid
- Black suction hose from the brush deck up into the hopper side (orange collar), steel exhaust stack, antenna
- Six wheels (front axle + tandem rear), 0.52 m radius, white hubs, tread blocks; black arches with orange top stripes
- Brush deck: three 1.0 m orange disc brushes with black motor pods under the nose
- Outriggers: black mounts, white boom arms, steel hydraulic rams, black motor pods with orange lamps, 1.2 m orange
  disc brushes; outer brush edges at exactly +-2.685 m (span = `width` stat, read from `data/lines.vehicles.json`)

## Triangles
18,068 (export: 210 nodes, 7 materials).

## Would improve
- Fold the outrigger booms along the cab sides as a separate parked pose/state, if the loader ever supports it.
- Slant the windscreen and give the cab a slight rake so it reads less like a box from the front.
- The hopper side is still a large flat panel; a pressed-rib pattern or a decal would break it up.
- Brushes use the kit's forward tilt; the rear edge sits just above ground, worth checking against in-game ground contact.
- Tread blocks are unbevelled to stay under budget; a lower-poly bevelled tread would look softer.
