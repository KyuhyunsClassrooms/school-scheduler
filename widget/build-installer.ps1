param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$widgetRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $widgetRoot
$compilerRoot = Join-Path $widgetRoot 'tools\InnoSetupPortable'
$compiler = Join-Path $compilerRoot 'tools\ISCC.exe'
$exe = Join-Path $widgetRoot 'bin\SchoolWidget.exe'
$wasRunning = $false

function Assert-Publisher([string]$Path, [string]$Publisher) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch $Publisher) {
        throw "Invalid publisher signature: $Path"
    }
}

try {
    if (-not $SkipBuild) {
        $running = @(Get-CimInstance Win32_Process -Filter "Name='SchoolWidget.exe'" |
            Where-Object { $_.ExecutablePath -eq $exe })
        $wasRunning = $running.Count -gt 0
        if ($wasRunning) {
            & $exe --quit
            foreach ($processInfo in $running) {
                $process = Get-Process -Id $processInfo.ProcessId -ErrorAction SilentlyContinue
                if ($process -and -not $process.WaitForExit(10000)) { throw 'Quit the running widget before building.' }
            }
        }
        & (Join-Path $widgetRoot 'build.cmd')
        if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (-not (Test-Path -LiteralPath $compiler)) {
        $downloadRoot = Join-Path $widgetRoot 'tools\downloads'
        New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
        $compilerPackage = Join-Path $downloadRoot 'tools.innosetup.6.7.3.nupkg'
        Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/tools.innosetup/6.7.3/tools.innosetup.6.7.3.nupkg' -OutFile $compilerPackage
        [IO.Compression.ZipFile]::ExtractToDirectory($compilerPackage, $compilerRoot)
    }
    Assert-Publisher $compiler 'Pyrsys'
    Assert-Publisher (Join-Path $compilerRoot 'tools\ISCmplr.dll') 'Pyrsys'

    # This helper only reads an old installation during upgrade. Clean installs never run it.
    $webViewCore = Join-Path $widgetRoot 'lib\Microsoft.Web.WebView2.Core.dll'
    if (-not (Test-Path -LiteralPath $webViewCore)) {
        $sdkVersion = '1.0.4258.31'
        $sdkDownloads = Join-Path $widgetRoot 'tools\downloads'
        New-Item -ItemType Directory -Path $sdkDownloads -Force | Out-Null
        $sdkPackage = Join-Path $sdkDownloads "microsoft.web.webview2.$sdkVersion.nupkg"
        if (-not (Test-Path -LiteralPath $sdkPackage)) {
            Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$sdkVersion/microsoft.web.webview2.$sdkVersion.nupkg" -OutFile $sdkPackage
        }
        $sdkArchive = [IO.Compression.ZipFile]::OpenRead($sdkPackage)
        try {
            $sdkCoreEntry = $sdkArchive.GetEntry('lib/net462/Microsoft.Web.WebView2.Core.dll')
            if (-not $sdkCoreEntry) { throw 'Migration SDK assembly is missing from the NuGet package.' }
            New-Item -ItemType Directory -Path (Split-Path -Parent $webViewCore) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($sdkCoreEntry, $webViewCore, $false)
        }
        finally { $sdkArchive.Dispose() }
        if ([Reflection.AssemblyName]::GetAssemblyName($webViewCore).Version.ToString() -ne $sdkVersion) {
            throw 'Unexpected migration SDK assembly version.'
        }
    }
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $migrationBuildDir = Join-Path $widgetRoot 'installer\build'
    New-Item -ItemType Directory -Path $migrationBuildDir -Force | Out-Null
    & $csc /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$migrationBuildDir\MigrateWebData.exe" `
        "/r:$widgetRoot\lib\Microsoft.Web.WebView2.Core.dll" /r:System.Core.dll /r:System.Web.Extensions.dll `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll "/resource:$widgetRoot\src\school-events.json,school-events.json" `
        "$widgetRoot\installer\MigrateWebData.cs" "$widgetRoot\src\CalendarData.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Legacy migration helper build failed.' }

    $requiredFiles = @('SchoolWidget.exe', 'SchoolDockHook.dll')
    foreach ($name in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $widgetRoot "bin\$name"))) { throw "Missing payload: $name" }
    }
    $version = [Reflection.AssemblyName]::GetAssemblyName($exe).Version.ToString(3)
    & $compiler "/DAppVersion=$version" (Join-Path $widgetRoot 'installer\SchoolScheduler.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }

    $output = Join-Path $repoRoot "dist\SchoolScheduler-$version-x64-Setup.exe"
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $output
    "$($hash.Hash)  $([IO.Path]::GetFileName($output))" |
        Set-Content -LiteralPath "$output.sha256" -Encoding ascii
    Write-Output "Installer: $output"
    Write-Output ('Size: {0:N2} MiB' -f ((Get-Item -LiteralPath $output).Length / 1MB))
}
finally {
    if ($wasRunning -and (Test-Path -LiteralPath $exe)) {
        Start-Process -FilePath $exe -WorkingDirectory $repoRoot -WindowStyle Hidden
    }
}
