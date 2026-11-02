Describe 'dev.ps1' {
  It 'errors clearly without a game dir' { $env:MOONSWEEPERS_DIR = 'C:\definitely\missing'; { & "$PSScriptRoot\..\..\dev.ps1" -NoLaunch -WhatIf -ErrorAction Stop } | Should -Throw '*MOONSWEEPERS_DIR*'; Remove-Item Env:MOONSWEEPERS_DIR }
  It 'plans one build per project' { $env:MOONSWEEPERS_DIR = (Resolve-Path "$PSScriptRoot\..\..").Path; $out = & "$PSScriptRoot\..\..\dev.ps1" -NoLaunch -WhatIf; Remove-Item Env:MOONSWEEPERS_DIR; ($out -join "`n") | Should -Match 'src\\GarageLib\\GarageLib\.csproj'; ($out -join "`n") | Should -Match 'vehicles\\MsChoppa\\MsChoppa\.csproj' }
  It 'filters to -Only' { $env:MOONSWEEPERS_DIR = (Resolve-Path "$PSScriptRoot\..\..").Path; $out = & "$PSScriptRoot\..\..\dev.ps1" -Only MsChoppa -NoLaunch -WhatIf; Remove-Item Env:MOONSWEEPERS_DIR; ($out -join "`n") | Should -Not -Match 'GarageLib\.csproj' }
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
      Remove-Item Env:MOONSWEEPERS_DIR
      Remove-Item $tmp -Recurse -Force
    }
    $sw.Elapsed.TotalSeconds | Should -BeLessThan 30
    ($out -join "`n") | Should -Match 'LogOutput\.log'
  }
}
