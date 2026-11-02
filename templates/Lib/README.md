# __TITLE__

A library for Moon Sweepers mods.

By __AUTHOR__.

## Using it from a mod

Reference the project and depend on its plugin so BepInEx loads it first:

```xml
<ProjectReference Include="$(RepoRoot)src\__NAME__\__NAME__.csproj" Private="false" />
```

```csharp
[BepInPlugin(Guid, "My Mod", Version)]
[BepInDependency(__NAME__.Plugin.Guid)]
public class Plugin : BasePlugin { ... }
```

## Changelog

- 0.1.0: first version.
