$ErrorActionPreference = 'Stop'
$desktop = [Environment]::GetFolderPath('Desktop')
$target = Join-Path $PSScriptRoot 'Positioning Laser.exe'
$shortcutPath = Join-Path $desktop 'Positioning Laser.lnk'
if (-not (Test-Path -LiteralPath $target)) { throw 'Build the application first.' }
if (Test-Path -LiteralPath $shortcutPath) {
    $existing = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
    if ($existing.TargetPath -ne $target) { throw 'An unrelated shortcut already uses that name.' }
}
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $target
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.IconLocation = $target + ',0'
$shortcut.Description = 'Fixed 1% positioning laser switch. Opens OFF; no head movement.'
$shortcut.Save()
$verified = $shell.CreateShortcut($shortcutPath)
if ($verified.TargetPath -ne $target -or $verified.Arguments -ne '') { throw 'Shortcut verification failed.' }
Write-Output $shortcutPath
