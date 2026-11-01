$ErrorActionPreference = 'Stop'
$compiler = 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$appFiles = @((Join-Path $PSScriptRoot 'Controller.cs'), (Join-Path $PSScriptRoot 'App.cs'))
& $compiler /nologo /target:winexe /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll "/win32icon:$PSScriptRoot/positioning.ico" "/out:$PSScriptRoot/Positioning Laser.exe" @appFiles
if ($LASTEXITCODE -ne 0) { throw 'Application build failed' }
& $compiler /nologo /target:exe /r:System.Core.dll "/out:$PSScriptRoot/Protocol Tests.exe" (Join-Path $PSScriptRoot 'Controller.cs') (Join-Path $PSScriptRoot 'Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
& "$PSScriptRoot/Protocol Tests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Protocol tests failed' }
