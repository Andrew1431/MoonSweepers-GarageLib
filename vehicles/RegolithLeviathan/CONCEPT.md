# Regolith leviathan (`andrew1431.regolith-leviathan`)

Wide line tier 3. 11.28 m sweep strip, 3.4 m/s, 47,000 kg hopper, $94,000.

## Pitch

A slow, tall crawler on two long rubber tracks, with a hopper as big as a shipping container (about 5.9 m tall, 10.6 m
long). The cab sits high on a pedestal in the front-left corner, with a railed walkway and a ladder. Beside it, an
enclosed white conveyor runs up from the centre pickup roller into a head house on the hopper roof, so you can see the
dust going in. Its party trick is the front: a centre roller plus two staggered wing rollers on white booms, with a disc
brush at each corner, covering exactly 11.28 m. Next to the 1.8 m astronaut it reads as a step up from a truck to a
building on tracks, while keeping the vanilla white, black and orange toy look.

## Parts

- Root empty `RegolithLeviathan_Root`, nose at -Y, everything parented to it.
- Two stadium-shaped black tracks with top cleats, white idler and road-wheel hubs, white fenders with orange stripes.
- Black chassis deck, front beam with three orange lamps, rear bumper with tail lamps.
- Hopper (4.6 x 5.4 x 3.55 m): white with ribs, orange lid, black roof hatch, black rear dump door with steel hinges,
  orange side stripes, cyan load gauge on the left rear.
- Raised cab: black pedestal and floor, white body and roof with orange roof band, large dark glass front, sides and
  back. Black seat and dash inside, headlights, cyan light bar, two orange beacons, antenna. Railed walkway and steel
  ladder on the left.
- Covered conveyor: black trough, white segmented cover with black bands and orange stripes, black mouth at the pickup,
  white head house on the hopper roof with orange band and cyan lamp.
- Brushes: 4.2 m centre pickup roller in a black hood with an orange stripe, two 3.62 m wing rollers on white booms
  (black hinge, steel hydraulic ram in a black sleeve, black hangers, orange tip beacons), and two 0.62 m corner disc
  brushes on black arms. The outer housings reach exactly ±5.64 m.

## Numbers

Export: 204 nodes, 7 materials, **17,380 triangles** (`regolith_leviathan.bin`).

## To improve

- The tracks are a solid bevelled box. Visible links at the curved ends, or a cutout that shows the road wheels, would
  sell the crawler better.
- The booms are fixed. Splitting each wing into its own child object with a pivot at the hinge would let the game fold
  them up for transport.
- The right side, away from the cab, is a large blank hopper wall. A service door or a second walkway would break it up.
- The cab has no door. Adding one toward the walkway would also make it read clearly as the entry point.
- The front deck stacks three black boxes. A sloped white nose cowl over the front beam would make it friendlier.
