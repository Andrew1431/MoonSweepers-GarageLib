# Ms Choppa

A boxy utility helicopter for Moon Sweepers, sold at any garage for $12,000. Built on [GarageLib](../../README.md);
the flight model is the shared [Rotorcraft](../../shared/Rotorcraft/README.md) package (Big Choppa's tuning).

## Controls

- W / S: collective (climb / sink)
- A / D: pedals (yaw)
- Mouse: tilt (pitch and roll)
- Hold Left Alt: free look
- Hold the game's Flip key (Z): right it when tipped over

Keys, gravity, camera distance and model scale are in `BepInEx/config/com.andrew1431.mschoppa.cfg`.

When a garage brings the choppa out it moves it to open ground nearby (garage bays are built for wheels).

## Files

- `ChoppaController.cs`: flight, rotors, rider hiding, chase camera.
- `ChoppaModel.cs`: swaps the van's renderers for the Blender mesh, with game materials and box colliders.
- `Helipad.cs`: finds a landing spot near the garage.
- `blender/ms_choppa.blend`: source model. Export to `Resources/ms_choppa.bin` with
  `shared/Rotorcraft/tools/export_mesh_model.py`.

## Changelog

- 0.1.0: first version.
