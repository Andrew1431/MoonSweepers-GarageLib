Describe 'dev.ps1' {
  BeforeEach { $script:origGameDir = $env:MOONSWEEPERS_DIR }
  AfterEach { if ($null -eq $script:origGameDir) { Remove-Item Env:MOONSWEEPERS_DIR -ErrorAction SilentlyContinue } else { $env:MOONSWEEPERS_DIR = $script:origGameDir } }
  It 'errors clearly without a game dir' { $env:MOONSWEEPERS_DIR = 'C:\definitely\missing'; { & "$PSScriptRoot\..\..\dev.ps1" -NoLaunch -WhatIf -ErrorAction Stop } | Should -Throw '*MOONSWEEPERS_DIR*' }
  It 'plans one build per project' { $env:MOONSWEEPERS_DIR = (Resolve-Path "$PSScriptRoot\..\..").Path; $out = & "$PSScriptRoot\..\..\dev.ps1" -NoLaunch -WhatIf; ($out -join "`n") | Should -Match 'src\\GarageLib\\GarageLib\.csproj'; ($out -join "`n") | Should -Match 'vehicles\\MsChoppa\\MsChoppa\.csproj' }
  It 'filters to -Only' { $env:MOONSWEEPERS_DIR = (Resolve-Path "$PSScriptRoot\..\..").Path; $out = & "$PSScriptRoot\..\..\dev.ps1" -Only MsChoppa -NoLaunch -WhatIf; ($out -join "`n") | Should -Not -Match 'GarageLib\.csproj' }
  It 'gives up tailing a missing log instead of hanging' {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("dev-tail-" + [guid]::NewGuid())
    New-Item -ItemType Directory $tmp | Out-Null
    $env:MOONSWEEPERS_DIR = $tmp
    try {
      $sw = [Diagnostics.Stopwatch]::StartNew()
      # -NoBuild: the empty folder has no BepInEx DLLs to build against.
      $out = & "$PSScriptRoot\..\..\dev.ps1" -NoBuild -NoLaunch -Tail -TailTimeoutSeconds 1 2>&1
      $sw.Stop()
    } finally {
      Remove-Item $tmp -Recurse -Force
    }
    $sw.Elapsed.TotalSeconds | Should -BeLessThan 30
    ($out -join "`n") | Should -Match 'LogOutput\.log'
  }
  It 'does not close the game in tail-only mode' {
    $env:MOONSWEEPERS_DIR = (Resolve-Path "$PSScriptRoot\..\..").Path
    $text = (& "$PSScriptRoot\..\..\dev.ps1" -NoBuild -NoLaunch -Tail -WhatIf) -join "`n"
    $text | Should -Not -Match 'taskkill'
    $text | Should -Not -Match 'Would close'
  }
  It 'tails on plugin names as well as folder names' {
    $env:MOONSWEEPERS_DIR = (Resolve-Path "$PSScriptRoot\..\..").Path
    $out = & "$PSScriptRoot\..\..\dev.ps1" -Only MsChoppa -NoBuild -NoLaunch -Tail -WhatIf
    $text = $out -join "`n"
    $text | Should -Match 'for: .*Ms Choppa'
    $filter = ($out | Where-Object { $_ -like 'Source filter: *' }) -replace '^Source filter: ', ''
    '[Info   :   Ms Choppa] x' | Should -Match $filter
    '[Message:   BepInEx] x' | Should -Match $filter
    '[Info   :MsChoppa] x' | Should -Match $filter
    '[Info   :   Someone Else] x' | Should -Not -Match $filter
  }
}
