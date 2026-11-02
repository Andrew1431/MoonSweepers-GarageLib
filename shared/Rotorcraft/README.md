# Rotorcraft (shared source package)

Game-agnostic helicopter pieces extracted from Big Choppa (`~/code/big-choppa`), meant to be compiled into any
BepInEx 6 IL2CPP Unity mod. It is shared as source because every game has its own interop `UnityEngine` assemblies, so one
compiled DLL can't serve several games.

- `RotorcraftFlight.cs` contains the airframe model: lift along the rotor axis, per-axis drag, rate-commanded attitude, auto-level, weathervane.
  The caller supplies local up and gravity, so it works on planets and moons as well as flat worlds.
- `RotorcraftPilot.cs` turns input into collective and rates the way Big Choppa does: W/S collective that springs back to hover, pedals, and mouse cyclic.
- `MeshModel.cs` reads models written by `tools/export_mesh_model.py`. Materials come from a caller factory, so each game can use its own shaders.

## Using it

```xml
<Compile Include="..\..\shared\Rotorcraft\*.cs" LinkBase="Rotorcraft" />
```

Export a model from Blender with:

```bash
blender --background model.blend --python shared/blender/export_mesh_model.py -- RootObject out.bin
```

Embed the output as an `EmbeddedResource`.

Big Choppa itself still has its own copies. Moving it onto this package is a separate change.
