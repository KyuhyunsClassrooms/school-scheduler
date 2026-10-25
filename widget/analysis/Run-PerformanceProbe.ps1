param([string]$BaselineRef='v2.1.0')
$ErrorActionPreference='Stop'
$widgetRoot=Split-Path -Parent $PSScriptRoot
$repoRoot=Split-Path -Parent $widgetRoot
$reportRoot=Join-Path $PSScriptRoot 'performance'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
if (Get-Process SchoolWidget -ErrorAction SilentlyContinue) { throw 'Quit the calendar before running the desktop benchmark.' }
$baselineRoot=Join-Path $reportRoot ('source-'+[Guid]::NewGuid().ToString('N'))
$archivePath=Join-Path $reportRoot 'baseline-source.zip'
git -C $repoRoot archive --format=zip "--output=$archivePath" $BaselineRef
if($LASTEXITCODE -ne 0){throw 'Baseline ref is unavailable. Fetch tags or pass an existing commit.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($archivePath,$baselineRoot)
$oldLocation=Get-Location
try {
    foreach($label in @('baseline','optimized')) {
        $sourceWidget=if($label -eq 'baseline'){Join-Path $baselineRoot 'widget'}else{$widgetRoot}
        & (Join-Path $sourceWidget 'build-native.cmd')
        if($LASTEXITCODE -ne 0){throw 'Native build failed'}
        $output=Join-Path $reportRoot ($label+'-bin')
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $sourceWidget 'bin\SchoolDockHook.dll') -Destination (Join-Path $output 'SchoolDockHook.dll') -Force
        $sources=@('Program.cs','CalendarData.cs','CalendarRenderer.cs','NativeDialogs.cs','NativeWidget.cs','ScheduleFeed.cs','FeedConnectionDialog.cs') | ForEach-Object {Join-Path $sourceWidget ('src\'+$_)}
        $csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
        & $csc /nologo /target:exe /platform:x64 /codepage:65001 /main:SchoolWidget.PerformanceProbe "/out:$output\PerformanceProbe.exe" `
            /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Net.Http.dll /r:System.Security.dll `
            "/resource:$sourceWidget\src\school-events.json,school-events.json" $sources (Join-Path $PSScriptRoot 'PerformanceProbe.cs')
        if($LASTEXITCODE -ne 0){throw 'Performance probe compilation failed'}
        Set-Location $widgetRoot
        & (Join-Path $output 'PerformanceProbe.exe') $label
        if($LASTEXITCODE -ne 0){throw 'Performance probe failed'}
    }
}finally{Set-Location $oldLocation}
