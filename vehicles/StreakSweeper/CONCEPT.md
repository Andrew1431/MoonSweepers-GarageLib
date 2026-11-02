# Streak sweeper (`andrew1431.streak-sweeper`)

Fast line, tier 2. Follows the Sprint vacuum. 4.32 m sweep strip, 7.4 m/s, 13,150 kg hopper, $37,500.

## Pitch

The Streak sweeper is what happens when the Sprint vacuum enters a desert rally. It is a long white wedge that ramps up
from a low nose to a small wraparound-glass cab, then drops onto two huge exposed rear wheels under a tall rear wing.
The gimmick is the brush: a front splitter pushes a 4.32 m orange roller bar, almost twice the body's width, like a
rally plough, while the dust is carried in the two side pods. It reads as Fast line tier 2 because it is clearly faster
looking than a compact sweeper (lower, longer, winged, big rear wheels). It is also still a rugged off-roader, with a
roof light bar, snorkel, spare tyre and mud flaps, which leaves the later tiers room to get lower and more like a
land-speed car.

## Parts

- `StreakSweeper_Root` empty; all parts parented under it. Nose at -Y, wheels on Z = 0.
- Wedge body (white) with a long nose ramp and twin orange racing stripes, black chassis skirt, white rear deck.
- Low cab: white base, dark-glass wraparound canopy, white roof cap with an orange stripe, black driver seat inside.
- Roof light bar with four orange lamps, black snorkel, black antenna with an orange tip lamp.
- Side pods holding the hopper: white wedges with orange lids, orange side stripes, black sills, intakes and vents.
- Wheels: small steering fronts (r 0.42 m), big exposed rear drivers (r 0.62 m, 0.5 m wide), chunky treads, white hubs.
- Black fenders on stays (front and rear quarter), black mud flaps with orange stripes on brackets.
- Rear wing on black swan-neck pylons, black endplates, orange trailing-edge stripe.
- Spare tyre lying flat on the rear deck (rally-raid cue).
- Tail: black diffuser and panel, orange tail lamps, cyan running light, black hopper dump chute.
- Front: black splitter with an orange lip, orange headlights, a cyan nose light, and a 4.32 m orange roller brush
  (`kit.roller_brush`, length = width stat) in a black hood with a black beam, struts, diagonal braces and orange tip lamps.

Size: about 5.2 m long including the brush bar, 4.4 m wide at the bar (2.6 m body and wheels), 2.2 m to the antenna tip,
1.9 m to the wing.

## Export

`streak_sweeper.bin`: 144 nodes, 6 materials, **16,956 triangles**.

## Would improve

- The 13,150 kg hopper is only suggested by the two pods. A real sense of volume would need a hidden tank under the
  rear deck, or pods that run past the rear wheels.
- The wing endplates and fenders are plain boxes. Curved arches, and a swept wing profile, would read sleeker.
- The pod intakes are mostly hidden behind the front wheels. A visible duct from the splitter to each pod would sell
  the vacuum gimmick.
- The roller hood plus beam is a little heavy in side view. A slimmer single beam would suit the Fast line better.
- The wheel treads use most of the triangle budget (14 boxes per wheel). Fewer, larger blocks would free room for detail.
