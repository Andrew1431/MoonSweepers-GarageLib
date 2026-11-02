# Nexus Mods pages

One folder per Nexus page: `description.bbcode` (paste into the description editor, BBCode mode), and `page.md` with
the short fields (name, summary, category, requirements, permissions, file). Upload the matching zip from `dist/`
(`.\package.ps1` builds them). Keep these files in sync with the mod READMEs when a release changes behaviour.

Shared settings for every page:

- Game: Moon Sweepers. Adult content: no.
- Requirements, off-site: BepInEx 6.0.0-be.791 Unity.IL2CPP win-x64,
  https://builds.bepinex.dev/projects/bepinex_be/791/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.791%2Bb782da4.zip
- Requirements, Nexus: MoonSweepers BepInEx Patcher (Moon Sweepers mod 9).
- Permissions: your call. The source is public on GitHub, so open permissions with credit match it.
- Images: Nexus wants at least one; 16:9 in-game screenshots work best for the thumbnail. The Blender previews in
  `vehicles/<Name>/blender/preview_hero.png` are a stopgap.
- Not on Nexus: the installer (`installer/`) downloads from GitHub; keep it on GitHub and link it from the pages.
