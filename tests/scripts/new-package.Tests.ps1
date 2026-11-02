Describe 'new-package' {
  BeforeAll {
    $repo = (Resolve-Path "$PSScriptRoot\..\..").Path
    $tmp = Join-Path $env:TEMP "np-$(Get-Random)"
    # Copy only what new-package.ps1 reads; the full repo is large (blend files under vehicles/).
    New-Item -ItemType Directory -Force "$tmp\vehicles\MsChoppa" | Out-Null
    Copy-Item "$repo\new-package.ps1", "$repo\new-vehicle.ps1", "$repo\GarageLib.slnx" $tmp -ErrorAction SilentlyContinue
    Copy-Item "$repo\templates" $tmp -Recurse
    Copy-Item "$repo\vehicles\MsChoppa\MsChoppa.csproj" "$tmp\vehicles\MsChoppa"
  }
  AfterAll { Remove-Item $tmp -Recurse -Force }
  It 'scaffolds a mod with placeholders replaced' { & "$tmp\new-package.ps1" -Kind mod -Name TestMod -Author tester | Should -Match 'Created'; (Get-Content "$tmp\mods\TestMod\Plugin.cs" -Raw) | Should -Match 'com\.tester\.testmod'; (Get-Content "$tmp\mods\TestMod\Plugin.cs" -Raw) | Should -Not -Match '__NAME__'; (Get-Content "$tmp\GarageLib.slnx" -Raw) | Should -Match 'mods/TestMod/TestMod\.csproj' }
  It 'scaffolds a lib under src with a Version' { & "$tmp\new-package.ps1" -Kind lib -Name TestLib -Author tester | Out-Null; (Get-Content "$tmp\src\TestLib\Plugin.cs" -Raw) | Should -Match 'const string Version' }
  It 'refuses a name that exists under another kind' { { & "$tmp\new-package.ps1" -Kind mod -Name MsChoppa -Author tester -ErrorAction Stop } | Should -Throw '*already exists*' }
  It 'keeps the new-vehicle wrapper working' { & "$tmp\new-vehicle.ps1" -Name TestRover -Author tester -Title "Test Rover" | Should -Match 'Created vehicles'; Test-Path "$tmp\vehicles\TestRover\TestRover.csproj" | Should -BeTrue; (Get-Content "$tmp\vehicles\TestRover\Plugin.cs" -Raw) | Should -Match 'Test Rover' }
}
