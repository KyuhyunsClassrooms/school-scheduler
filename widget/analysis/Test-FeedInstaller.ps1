$ErrorActionPreference = 'Stop'
$widgetRoot = Split-Path -Parent $PSScriptRoot
$reportRoot = Join-Path $PSScriptRoot 'feed-installer-smoke'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
$testRoot = Join-Path $env:TEMP ('SchoolCalendar-Feed-Test-' + [Guid]::NewGuid().ToString('N'))
$userData = Join-Path $env:LOCALAPPDATA 'SchoolScheduler\calendar-data.json'
$dataHash = if (Test-Path -LiteralPath $userData) { (Get-FileHash -LiteralPath $userData).Hash } else { '' }
$explorer = @(Get-Process explorer).Id -join ','
$compiler = Join-Path $widgetRoot 'tools\InnoSetupPortable\tools\ISCC.exe'
$version = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $widgetRoot 'bin\SchoolWidget.exe')).Version.ToString(3)
& $compiler /Q "/DAppVersion=$version" '/DPackageAppId=SchoolSchedulerFeedInstallerTest' "/O$reportRoot" '/FFeed-Test-Setup' (Join-Path $widgetRoot 'installer\SchoolScheduler.iss')
if ($LASTEXITCODE -ne 0) { throw 'Test installer compilation failed' }
$setup = Join-Path $reportRoot 'Feed-Test-Setup.exe'
$common = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $testRoot + '"'),'/GROUP=SchoolSchedulerFeedInstallerTest','/TASKS=""')
$first = Start-Process -FilePath $setup -ArgumentList ($common + ('/LOG="' + (Join-Path $reportRoot 'install.log') + '"')) -PassThru -Wait -WindowStyle Hidden
if ($first.ExitCode -ne 0) { throw "Install exit $($first.ExitCode)" }
$installedExe = Join-Path $testRoot 'bin\SchoolWidget.exe'
if ([Reflection.AssemblyName]::GetAssemblyName($installedExe).Version.ToString(3) -ne $version) { throw 'Wrong payload version' }
$app = Start-Process -FilePath $installedExe -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2
if ($app.HasExited) { throw 'Installed application exited unexpectedly' }
$update = Start-Process -FilePath $setup -ArgumentList ($common + ('/LOG="' + (Join-Path $reportRoot 'update.log') + '"')) -PassThru -Wait -WindowStyle Hidden
if ($update.ExitCode -ne 0 -or -not $app.WaitForExit(5000)) { throw 'Running application update failed' }
$app = Start-Process -FilePath $installedExe -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2
$uninstall = Start-Process -FilePath (Join-Path $testRoot 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $reportRoot 'uninstall.log') + '"')) -PassThru -Wait -WindowStyle Hidden
if ($uninstall.ExitCode -ne 0 -or -not $app.WaitForExit(5000)) { throw 'Running application uninstall failed' }
if (Test-Path -LiteralPath $installedExe) { throw 'Application remains after uninstall' }
if ($dataHash -and (Get-FileHash -LiteralPath $userData).Hash -ne $dataHash) { throw 'Personal data changed during installation test' }
if ((@(Get-Process explorer).Id -join ',') -ne $explorer) { throw 'Explorer changed during installation test' }
$result = [ordered]@{ Version=$version; InstallExit=$first.ExitCode; RunningUpdateExit=$update.ExitCode; RunningUninstallExit=$uninstall.ExitCode; PersonalDataUnchanged=$true; ExplorerUnchanged=$true; Result='PASS' }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reportRoot 'result.json') -Encoding utf8
$result | ConvertTo-Json
