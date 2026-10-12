# Run on an isolated Windows CI account, not on a user's existing installation.
$ErrorActionPreference = 'Stop'
$setup = (Resolve-Path "$PSScriptRoot/../artifacts/AIpet-Setup-win-x64.exe").Path
$source = (Resolve-Path "$PSScriptRoot/../artifacts/win-x64/AIpet.exe").Path
$target = Join-Path $env:LOCALAPPDATA 'Programs\AIpet'
$settingsRoot = Join-Path $env:APPDATA 'AIpet'
$settings = Join-Path $settingsRoot 'settings.json'
$registryKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{4F55BDF9-B918-4231-9C02-25C17F960CD2}_is1'
if ((Test-Path $target) -or (Test-Path $settingsRoot) -or (Test-Path $registryKey)) {
    throw 'Installer tests require a clean Windows account.'
}
function Run-Setup {
    param([string]$LogName)
    $logPath = Join-Path $env:RUNNER_TEMP $LogName
    $process = Start-Process $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$logPath`"") -PassThru -Wait
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
try {
    Run-Setup 'AIpet-install.log'
    Assert-Installed
    New-Item -ItemType Directory $settingsRoot | Out-Null
    $saved = '{"petName":"업데이트 테스트","petScale":125,"bubbleAlwaysShown":true}'
    [IO.File]::WriteAllText($settings, $saved)
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
    $uninstall = Start-Process "$target\unins000.exe" -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -PassThru -Wait
    if ($uninstall.ExitCode -ne 0 -or (Test-Path "$target\AIpet.exe") -or (Test-Path $registryKey)) { throw 'Uninstall did not remove the installed app.' }
    if ([IO.File]::ReadAllText($settings) -ne $saved) { throw 'Uninstall removed user settings.' }
    Write-Output 'Installer checks passed: fresh install, old binary replacement, running app upgrade, single registration, settings preservation, uninstall.'
}
finally {
    if ($pet -and !$pet.HasExited) { Stop-Process -Id $pet.Id -Force -ErrorAction SilentlyContinue }
}
