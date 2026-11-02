# Belt Boxes

A compact box splitter and box merger for belts: one belt in, up to three out, or up to three in, one out. Any cargo
passes, vanilla or modded. Built on [StructureLib](../../src/StructureLib/README.md) (its routers), which must be
installed too.

By ryanhartwig.

## What it adds

| | Numbers |
|---|---|
| **Box splitter** (Logistics, $25) | A 1.4 m square box. One belt in, up to three belts out (forward, left, right). Each canister goes to the next output in turn; an output that has no belt or whose belt entrance is blocked is skipped. When no output can take a canister it waits at the end of the input belt, and the belt queues behind it. |
| **Box merger** (Logistics, $25) | The same box. Up to three belts in (left, back, right), one belt out. It serves the inputs in turn among those with a canister waiting; when the output belt is missing or blocked, everything waits. |

Note in the build menu:

- Box splitter: "Splits one belt into up to three, taking turns. Skips an output that is blocked or has no belt."
- Box merger: "Merges up to three belts into one, taking turns."

The vanilla splitter costs $5; these cost a little more as a convenience: they take side belts, which the vanilla Y
does not. Canisters keep their cargo, kilograms, capacity and owner through a box. The rate is bounded by the output
belt's entrance spacing.

## Ports

Flow runs along the box's +X axis (the way the game's own machines run). Left and right are as seen looking along +X,
standing up. Positions are structure-local, in metres, on the footprint edge at belt height. The turn order is the
order below.

| Box | Port | Where (x, y, z) | Belt flows |
|---|---|---|---|
| Box splitter | `In` (input) | (−0.70, 0.85, 0.00), the back face | +X, into the box |
| Box splitter | `Forward` (output, 1st) | (0.70, 0.85, 0.00), the front face | +X, away from the box |
| Box splitter | `Left` (output, 2nd) | (0.00, 0.85, 0.70), the left face | +Z, away from the box |
| Box splitter | `Right` (output, 3rd) | (0.00, 0.85, −0.70), the right face | −Z, away from the box |
| Box merger | `Left` (input, 1st) | (0.00, 0.85, 0.70), the left face | −Z, into the box |
| Box merger | `Back` (input, 2nd) | (−0.70, 0.85, 0.00), the back face | +X, into the box |
| Box merger | `Right` (input, 3rd) | (0.00, 0.85, −0.70), the right face | +Z, into the box |
| Box merger | `Out` (output) | (0.70, 0.85, 0.00), the front face | +X, away from the box |

## Two differences from the vanilla splitter and merger

- **No cut-in.** StructureLib does not implement dropping a box onto an existing belt to split it. Place the box
  free-standing and join belts to its ports. (The game may still offer cut-in on a placed box; see "Not checked
  yet" below.)
- **No visible crossing.** A canister does not ride across the box: it vanishes at the end of the input belt and a new
  canister (with a new id) appears at the start of the output belt in the same frame.

## Laying one out

- The straight path, splitter `In` → `Forward` and merger `Back` → `Out`, has no turning limit: boxes in a line along
  +X join with short straight belts (1.3 m between boxes worked in game).
- Side belts need room to swing. A U-turn from a side output to the next box's side input, with 1.3 m between the
  boxes, is refused by the game as "Turn too tight: give it more room." (StructureLib's spike planned belts of 6.86 m
  and 5.56 m there; both were routable but never valid). Give a side belt several metres to turn, or turn the box it
  feeds so the belt runs straight out of the side port.
- Unported faces take nothing.
- The turn is kept in memory only: it starts again from the first port after a load. Building or removing belts and
  structures elsewhere keeps it.

## Co-op

As for any StructureLib belt input: the host decides where canisters go. Clients are untested. StructureLib refuses a
join when the two players' mods differ, so both players need Belt Boxes.

## What works, and what does not yet

Checked in game (StructureLib's dev self-test with its example grinder and burner, single player):
- both boxes register with four ports each and build;
- belts were joined at the splitter's `In` and `Forward` and at the merger's `Back` and `Out`;
- grit was routed through both boxes, from a grinder into a burner (12 canisters reached the burner in the run);
- any cargo passes: a sand canister was routed through both boxes too, and then waited at the burner, which takes only
  grit.

Not checked yet, worth a look before a release:

- **The side ports** (`Left`, `Right` on both boxes): no belt was joined to them in game.
- **Belts placed by hand** with the belt tool onto each port at the 0.7 m port spacing. That includes whether a belt
  can leave or enter every ported face straight.
- **What the game does with a placed box.** The game decides whether a structure is a router (`MoonWorld.Routes`) from
  its definition's ports, so it treats any clone with three or four ports, these boxes included, as a router. The belt
  tool may offer cut-in on a placed box, and the other `Routes`-gated paths in the game are untested. StructureLib's
  own hop answers first for a box's input belts, which is what the run above exercised.
- **Building from the build menu by hand** (the real ghost and the remove tool).
- **Co-op** with two players.

## The models

Both boxes are shown as the vanilla floodlight for now. Their own models, `box_splitter.bin` and `box_merger.bin`
(built from the scripts in [`blender/`](blender)), already sit in this folder and ship next to `BeltBoxes.dll`. The
next version switches the boxes to them and adds preview images.

## Saves

The boxes are kept in `structurelib.json` next to the save, not in the save itself; their belts are vanilla belts and
the canisters ride the belts. Remove Belt Boxes and the boxes wait in that file until you reinstall it.

## Changelog

- 0.1.0: first version: the box splitter and box merger, with declared ports.
