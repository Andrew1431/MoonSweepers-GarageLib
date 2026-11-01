# Dev Tooling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One scaffold script for every package kind and one dev-loop script, so building, installing, launching and reading the log is a single command on a machine where Steam launching does not work.

**Architecture:** Two PowerShell 7 scripts at the repo root plus three template folders. `new-package.ps1` generalises `new-vehicle.ps1` (which stays as a one-line wrapper for one release). `dev.ps1` closes the game, builds the requested projects one by one (SDK 8 cannot open the `.slnx`), launches `MoonSweepers.exe` directly and tails the BepInEx log filtered to the repo's log sources.

**Tech Stack:** PowerShell 7, dotnet CLI, Pester 5 for script tests (`Install-Module Pester -Scope CurrentUser` if missing).

**Spec:** `docs/superpowers/specs/2026-11-01-structurelib-design.md`, section "Tooling".

## Global Constraints

- Hot paths on the board: `new-vehicle.ps1`, `templates/**`, `CONTRIBUTING.md`, `README.md`, `GarageLib.slnx`. Claim exact files; if the board is unreachable, say so in the hand-off.
- `global.json` is not added. Game path from `MOONSWEEPERS_DIR`, else `C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers`, else `Directory.Build.local.props`' `GameDir` (read with a regex, not MSBuild).
- `new-package.ps1` must not run `dotnet sln add` (needs SDK 9); it inserts the `<Project Path="..."/>` line into `GarageLib.slnx` under the matching `<Folder>` by text edit, creating the folder element if missing.
- Templates use the same placeholders as today: `__NAME__`, `__TITLE__`, `__AUTHOR__`, `__LOWER__`, `__KEBAB__`.
- Commit at the end of each task; push only when Ryan asks. Commit messages end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

## Review Focus

1. **A `-Name` that already exists under another kind** (e.g. `vehicles/Foo` exists and `-Kind mod -Name Foo` is requested) must refuse, since assembly names collide in `BepInEx/plugins`. Test in Task 1.
2. **`dev.ps1` with the game running from Steam** must still close it (`taskkill` by image name) and must not hang on `tail` when the log is absent; test in Task 2 with a missing log path.
3. **`new-package.ps1 -Kind lib`** must produce a project that builds with no `Plugin.cs` `Version` for `package.ps1` to pick up only if the lib is meant to ship; the lib template includes a `Plugin.cs` with `Version` because libraries are released packages. Test in Task 1.
4. **A repo checkout on a machine without the game** must get a clear error from `dev.ps1` naming `MOONSWEEPERS_DIR`, not a stack trace. Test in Task 2.
5. **`new-vehicle.ps1` wrapper** must keep accepting its old arguments verbatim. Test in Task 1.

---

### Task 1: `new-package.ps1` and templates

**Files:**
- Create: `new-package.ps1`, `templates/Mod/Plugin.cs`, `templates/Mod/README.md`, `templates/Mod/__NAME__.csproj`, `templates/Lib/Plugin.cs`, `templates/Lib/README.md`, `templates/Lib/__NAME__.csproj`
- Modify: `new-vehicle.ps1` (becomes `& "$PSScriptRoot\new-package.ps1" -Kind vehicle @PSBoundParameters`), `CONTRIBUTING.md` ("New vehicle" → "New package")
- Test: `tests/scripts/new-package.Tests.ps1`

**Interfaces:**
- Produces: `new-package.ps1 -Kind vehicle|mod|lib -Name <PascalCase> -Author <lowercase> [-Title <text>]` creating `vehicles/<Name>`, `mods/<Name>` or `src/<Name>` from `templates/Vehicle|Mod|Lib`, adding the project to `GarageLib.slnx`, printing `Created <path>. Build it with: dotnet build <path> -c Release`. Exit code 1 with a one-line reason on any refusal.

- [ ] **Step 1: Write the failing Pester tests**

```powershell
Describe 'new-package' {
  BeforeAll { $repo = (Resolve-Path "$PSScriptRoot\..\..").Path; $tmp = Join-Path $env:TEMP "np-$(Get-Random)"; Copy-Item $repo $tmp -Recurse -Exclude bin,obj,dist,.git }
  AfterAll { Remove-Item $tmp -Recurse -Force }
  It 'scaffolds a mod with placeholders replaced' { & "$tmp\new-package.ps1" -Kind mod -Name TestMod -Author tester | Should -Match 'Created'; (Get-Content "$tmp\mods\TestMod\Plugin.cs" -Raw) | Should -Match 'com\.tester\.testmod'; (Get-Content "$tmp\mods\TestMod\Plugin.cs" -Raw) | Should -Not -Match '__NAME__'; (Get-Content "$tmp\GarageLib.slnx" -Raw) | Should -Match 'mods/TestMod/TestMod\.csproj' }
  It 'scaffolds a lib under src with a Version' { & "$tmp\new-package.ps1" -Kind lib -Name TestLib -Author tester | Out-Null; (Get-Content "$tmp\src\TestLib\Plugin.cs" -Raw) | Should -Match 'const string Version' }
  It 'refuses a name that exists under another kind' { { & "$tmp\new-package.ps1" -Kind mod -Name MsChoppa -Author tester -ErrorAction Stop } | Should -Throw '*already exists*' }
  It 'keeps the new-vehicle wrapper working' { & "$tmp\new-vehicle.ps1" -Name TestRover -Author tester | Should -Match 'Created vehicles'; Test-Path "$tmp\vehicles\TestRover\TestRover.csproj" | Should -BeTrue }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `Invoke-Pester tests/scripts/new-package.Tests.ps1 -Output Detailed`. Expected: 4 failed (script not found).

- [ ] **Step 3: Write the templates and `new-package.ps1`; rewrite `new-vehicle.ps1` as the wrapper**

Mod template `Plugin.cs`: `BasePlugin` with `Guid = "com.__AUTHOR__.__LOWER__"`, `Version = "0.1.0"`, a `Load()` that logs loaded, and a comment pointing at StructureLib and GarageLib for registering content. Lib template: the same plus a `public const string Guid` others can `[BepInDependency]` on and a `README.md` section "Using it from a mod". Mod csproj: no project references by default, commented `ProjectReference` lines for both libraries.

- [ ] **Step 4: Run tests to verify they pass; build a scaffolded mod for real**

Run: `Invoke-Pester tests/scripts/new-package.Tests.ps1 -Output Detailed`. Expected: 4 passed. Then in the real repo: `.\new-package.ps1 -Kind mod -Name ScratchMod -Author ryanhartwig; dotnet build mods\ScratchMod -c Release -p:InstallToGame=false` → Build succeeded; then delete `mods/ScratchMod` and revert its `.slnx` line.

- [ ] **Step 5: Commit**

```bash
git add new-package.ps1 new-vehicle.ps1 templates CONTRIBUTING.md tests/scripts
git commit -m "tooling: new-package.ps1 scaffolds vehicles, mods and libraries; new-vehicle.ps1 wraps it"
```

---

### Task 2: `dev.ps1`

**Files:**
- Create: `dev.ps1`
- Modify: `CONTRIBUTING.md` (one "Dev loop" paragraph), `README.md` ("Making a vehicle" → "Making a package", mention `dev.ps1`)
- Test: `tests/scripts/dev.Tests.ps1`

**Interfaces:**
- Produces: `dev.ps1 [-Only <Name>[,<Name>]] [-NoLaunch] [-Tail] [-Release]`: resolves the game dir (error text `Set MOONSWEEPERS_DIR to your Moon Sweepers folder` if none found), `taskkill /IM MoonSweepers.exe /F` if running, builds every project under `src/`, `vehicles/`, `mods/`, `examples/` (or `-Only`) with `dotnet build <proj> -c Release` (`-p:DevBuild=false` with `-Release`), stops on the first failure, launches `MoonSweepers.exe` from the game dir unless `-NoLaunch`, and with `-Tail` follows `BepInEx\LogOutput.log` showing lines whose source is one of the built assembly names or `BepInEx`, waiting for the file to appear for up to 60 s. `-WhatIf` prints the plan without doing anything (used by tests).

- [ ] **Step 1: Write the failing Pester tests**

```powershell
Describe 'dev.ps1' {
  It 'errors clearly without a game dir' { $env:MOONSWEEPERS_DIR = 'C:\definitely\missing'; { & "$PSScriptRoot\..\..\dev.ps1" -NoLaunch -WhatIf -ErrorAction Stop } | Should -Throw '*MOONSWEEPERS_DIR*'; Remove-Item Env:MOONSWEEPERS_DIR }
  It 'plans one build per project' { $env:MOONSWEEPERS_DIR = (Resolve-Path "$PSScriptRoot\..\..").Path; $out = & "$PSScriptRoot\..\..\dev.ps1" -NoLaunch -WhatIf; Remove-Item Env:MOONSWEEPERS_DIR; ($out -join "`n") | Should -Match 'src\\GarageLib\\GarageLib\.csproj'; ($out -join "`n") | Should -Match 'vehicles\\MsChoppa\\MsChoppa\.csproj' }
  It 'filters to -Only' { $env:MOONSWEEPERS_DIR = (Resolve-Path "$PSScriptRoot\..\..").Path; $out = & "$PSScriptRoot\..\..\dev.ps1" -Only MsChoppa -NoLaunch -WhatIf; Remove-Item Env:MOONSWEEPERS_DIR; ($out -join "`n") | Should -Not -Match 'GarageLib\.csproj' }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `Invoke-Pester tests/scripts/dev.Tests.ps1 -Output Detailed`. Expected: 3 failed.

- [ ] **Step 3: Write `dev.ps1`**

Game-dir resolution order and error text as in Interfaces. Tail: `Get-Content -Wait -Tail 0` piped through a `Where-Object` on the source regex; stop on Ctrl+C.

- [ ] **Step 4: Run tests to verify they pass; run the real loop once**

Run: `Invoke-Pester tests/scripts/dev.Tests.ps1 -Output Detailed`. Expected: 3 passed. Then `.\dev.ps1 -Only GarageLib -Tail` with the game closed: expected a `GarageLib 0.1.0 loaded` line in the tail within a minute.

- [ ] **Step 5: Commit**

```bash
git add dev.ps1 CONTRIBUTING.md README.md tests/scripts
git commit -m "tooling: dev.ps1 builds, installs, launches the game directly and tails the log"
```
