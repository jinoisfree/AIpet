# Run on an isolated Windows CI account, not on a user's existing installation.
$ErrorActionPreference = 'Stop'
$setup = (Resolve-Path "$PSScriptRoot/../artifacts/AIpet-Setup-win-x64.exe").Path
$source = (Resolve-Path "$PSScriptRoot/../artifacts/win-x64/AIpet.exe").Path
$target = Join-Path $env:LOCALAPPDATA 'Programs\AIpet'
$settingsRoot = Join-Path $env:APPDATA 'AIpet'
$settings = Join-Path $settingsRoot 'settings.json'
$dataRoot = Join-Path $env:LOCALAPPDATA 'AIpet'
$locations = Join-Path $dataRoot 'locations.json'
$installLog = Join-Path $dataRoot 'install.log'
$registryKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{4F55BDF9-B918-4231-9C02-25C17F960CD2}_is1'
if ((Test-Path $target) -or (Test-Path $settingsRoot) -or (Test-Path $registryKey) -or (Test-Path $dataRoot)) {
    throw 'Installer tests require a clean Windows account.'
}
function Run-Setup {
    param([string]$LogName, [switch]$AppUpdate)
    $logPath = Join-Path $env:RUNNER_TEMP $LogName
    Write-Output "START: $LogName"
    $setupArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$logPath`"")
    if ($AppUpdate) { $setupArguments += '/AIPETUPDATE=1' }
    $process = Start-Process $setup -ArgumentList $setupArguments -PassThru
    if (!$process.WaitForExit(120000)) { try { $process.Kill($true) } catch { }; throw "Setup timeout: $LogName" }
    $process.Refresh()
    Write-Output "END: $LogName exit=$($process.ExitCode)"
    if ($process.ExitCode -ne 0) { Get-Content $logPath; throw "Setup failed: $($process.ExitCode)" }
}
function Assert-Installed {
    if ((Get-FileHash "$target\AIpet.exe").Hash -ne (Get-FileHash $source).Hash) { throw 'Installed binary does not match the new build.' }
    if (!(Test-Path $registryKey)) { throw 'Missing uninstall registration.' }
    $entries = @(Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' |
        Get-ItemProperty | Where-Object DisplayName -eq 'AIpet')
    if ($entries.Count -ne 1) { throw 'Expected one installed AIpet entry.' }
    if (!(Test-Path (Join-Path ([Environment]::GetFolderPath('Programs')) 'AIpet.lnk'))) { throw 'Missing start menu shortcut.' }
}
function Assert-Identity([string]$Path, [string]$Label) {
    $v = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    Write-Output "$Label version: ProductName=$($v.ProductName); OriginalFilename=$($v.OriginalFilename); InternalName=$($v.InternalName); CompanyName=$($v.CompanyName); FileVersion=$($v.FileVersion)"
    if ($v.ProductName -ne 'AIpet' -or (($v.OriginalFilename -cnotin @('AIpet.dll','AIpet.exe','AIpet')) -and ($v.InternalName -cnotin @('AIpet.dll','AIpet.exe','AIpet')))) { throw "$Label identity is not supported." }
}
function Start-Copy([string]$Path) {
    $process = Start-Process $Path -PassThru
    Start-Sleep -Seconds 4
    if ($process.HasExited) { throw 'Copy exited before the check.' }
    return $process
}
function Assert-Settings {
    if ([IO.File]::ReadAllText($settings) -ne $saved) { throw 'Cleanup changed settings.' }
}
$fixture = Join-Path $env:RUNNER_TEMP ('aipet-copies-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $fixture | Out-Null
Assert-Identity $source 'New build'
$archive = Join-Path $fixture 'previous.zip'
Write-Output 'START: download published preview.1'
Invoke-WebRequest -TimeoutSec 90 'https://github.com/jinoisfree/AIpet/releases/download/windows-v1.0.11-preview.1/AIpet-1.0.11-preview.1-win-x64.zip' -OutFile $archive
Write-Output 'END: download published preview.1'
Expand-Archive $archive (Join-Path $fixture 'previous')
Assert-Identity (Join-Path $fixture 'previous/AIpet.exe') 'Published preview.1'
try {
    Run-Setup 'AIpet-install.log'
    Assert-Installed
    New-Item -ItemType Directory $settingsRoot | Out-Null
    $saved = '{"petName":"업데이트 테스트","petScale":125,"bubbleAlwaysShown":true}'
    [IO.File]::WriteAllText($settings, $saved)
    # Keep the installer fixture independent of any future published release.
    New-Item -ItemType Directory -Force $dataRoot | Out-Null
    $updates = Join-Path $dataRoot 'updates.json'
    $updatePreferences = '{"automatic":false}'
    [IO.File]::WriteAllText($updates, $updatePreferences)
    # Simulate an older binary without version metadata; same installer must replace it.
    [IO.File]::WriteAllText("$target\AIpet.exe", 'old build')
    Run-Setup 'AIpet-upgrade.log'
    Assert-Installed
    if ([IO.File]::ReadAllText($settings) -ne $saved) { throw 'Upgrade changed settings.' }
    # Upgrade while the actual WPF app holds the executable open (Restart Manager).
    $pet = Start-Process "$target\AIpet.exe" -PassThru
    Start-Sleep -Seconds 5
    if ($pet.HasExited) { throw 'AIpet exited before the running-app upgrade check.' }
    Run-Setup 'AIpet-running-upgrade.log'
    if (!$pet.WaitForExit(10000)) { throw 'Upgrade did not stop the previous AIpet process.' }
    Assert-Installed
    if ([IO.File]::ReadAllText($settings) -ne $saved) { throw 'Running upgrade changed settings.' }
    # A verified portable copy is running outside the installation directory.
    $liveDir = Join-Path $fixture 'live'
    New-Item -ItemType Directory $liveDir | Out-Null
    $liveExe = Join-Path $liveDir 'AIpet.exe'
    Copy-Item $source $liveExe
    $portable = Start-Copy $liveExe
    Run-Setup 'AIpet-portable-upgrade.log'
    if (!$portable.WaitForExit(10000) -or (Test-Path $liveExe)) { throw 'Running portable copy was not retired.' }
    if (!(Test-Path $liveDir)) { throw 'Cleanup deleted a directory.' }
    Assert-Installed; Assert-Settings
    Write-Output 'PASS: running portable copy stopped and executable deleted; directory preserved.'

    # Unrelated executable with the same name must survive installation.
    $foreignDir = Join-Path $fixture 'unrelated'
    New-Item -ItemType Directory $foreignDir | Out-Null
    $foreignExe = Join-Path $foreignDir 'AIpet.exe'
    Copy-Item "$env:SystemRoot\System32\cmd.exe" $foreignExe
    $foreignHash = (Get-FileHash $foreignExe).Hash
    $foreign = Start-Process $foreignExe -ArgumentList '/d /c "ping -n 120 127.0.0.1 > nul"' -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 2
    if ($foreign.HasExited) { throw 'Unrelated fixture exited too soon.' }
    Run-Setup 'AIpet-unrelated-upgrade.log'
    $foreign.Refresh()
    if ($foreign.HasExited -or !(Test-Path $foreignExe) -or (Get-FileHash $foreignExe).Hash -ne $foreignHash) { throw 'Unrelated process or file was changed.' }
    Assert-Installed; Assert-Settings
    Write-Output 'PASS: unrelated same-name process and file preserved.'

    # A previously used, currently stopped copy is found only through locations.json.
    $stoppedDir = Join-Path $fixture 'stopped'
    New-Item -ItemType Directory $stoppedDir | Out-Null
    $stoppedExe = Join-Path $stoppedDir 'AIpet.exe'
    Copy-Item $source $stoppedExe
    $stopped = Start-Copy $stoppedExe
    $known = @([IO.File]::ReadAllText($locations) | ConvertFrom-Json)
    if (!($known | Where-Object { $_.EndsWith('stopped\AIpet.exe', [StringComparison]::OrdinalIgnoreCase) })) { throw 'Copy location was not registered.' }
    Stop-Process -Id $stopped.Id -Force
    if (!$stopped.WaitForExit(10000)) { throw 'Stopped fixture did not exit.' }
    Run-Setup 'AIpet-stopped-upgrade.log'
    if (Test-Path $stoppedExe) { throw 'Registered stopped copy was not deleted.' }
    if (!(Test-Path $stoppedDir)) { throw 'Stopped copy directory was deleted.' }
    Assert-Installed; Assert-Settings
    if (!(Test-Path $installLog) -or [IO.File]::ReadAllText($installLog).IndexOf($env:USERPROFILE, [StringComparison]::OrdinalIgnoreCase) -ge 0) { throw 'Install log missing or contains profile path.' }
    Write-Output 'PASS: registered stopped copy deleted, settings preserved, install.log contains no profile path.'

    # ZIP takeover stops an installed instance, but never deletes its executable.
    $pet = Start-Copy "$target\AIpet.exe"
    Copy-Item $source $liveExe
    $portable = Start-Copy $liveExe
    if (!$pet.WaitForExit(10000) -or !(Test-Path "$target\AIpet.exe")) { throw 'ZIP takeover failed or deleted the installed executable.' }
    $duplicate = Start-Process $liveExe -PassThru
    if (!$duplicate.WaitForExit(10000)) { throw 'Same-copy launch did not exit.' }
    $portable.Refresh()
    if ($portable.HasExited) { throw 'Same-copy launch replaced the current instance.' }
    Stop-Process -Id $portable.Id -Force
    if (!$portable.WaitForExit(10000)) { throw 'Portable fixture did not exit.' }
    Write-Output 'PASS: ZIP takeover and same-copy no-op; no files deleted.'
    # The updater's silent-install flag must replace a running app and launch the new one.
    $pet = Start-Copy "$target\AIpet.exe"
    Run-Setup 'AIpet-auto-update.log' -AppUpdate
    if (!$pet.WaitForExit(10000)) { throw 'Auto update did not stop the previous process.' }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $restarted = @(Get-Process -Name AIpet -ErrorAction SilentlyContinue | Where-Object {
            try { $_.MainModule.FileName -eq "$target\AIpet.exe" -and $_.Id -ne $pet.Id } catch { $false }
        })
        if ($restarted.Count -eq 0) { Start-Sleep -Milliseconds 250 }
    } while ($restarted.Count -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($restarted.Count -ne 1) { throw 'Auto update did not launch exactly one installed app.' }
    Start-Sleep -Seconds 3
    $restarted[0].Refresh()
    if ($restarted[0].HasExited) { throw 'Updated app exited immediately after restart.' }
    Assert-Installed; Assert-Settings
    if ([IO.File]::ReadAllText($updates) -ne $updatePreferences) { throw 'Auto update changed update preferences.' }
    Stop-Process -Id $restarted[0].Id -Force
    if (!$restarted[0].WaitForExit(10000)) { throw 'Updated app did not stop for uninstall.' }
    Write-Output 'PASS: silent auto update replaces, restarts, and preserves settings and update preferences.'
    $uninstall = Start-Process "$target\unins000.exe" -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -PassThru -Wait
    if ($uninstall.ExitCode -ne 0 -or (Test-Path "$target\AIpet.exe") -or (Test-Path $registryKey)) { throw 'Uninstall did not remove the installed app.' }
    if ([IO.File]::ReadAllText($settings) -ne $saved) { throw 'Uninstall removed user settings.' }
    Write-Output 'Installer checks passed: fresh install, old binary replacement, running app upgrade, single registration, settings preservation, uninstall.'
}
finally {
    foreach ($fixtureProcess in (@($pet, $portable, $stopped, $foreign, $duplicate) + @($restarted))) {
        if ($fixtureProcess -and !$fixtureProcess.HasExited) { try { $fixtureProcess.Kill($true); $fixtureProcess.WaitForExit(10000) | Out-Null } catch { } }
    }
}
