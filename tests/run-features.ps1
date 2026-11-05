param([switch]$Dark, [switch]$Render, [switch]$UiOnly, [switch]$Demos)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$outputPath = Join-Path $projectRoot 'build\feature-tests'
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
& (Join-Path $projectRoot 'scripts\prepare-assets.ps1')
$references = @(
    "/r:$frameworkPath\WPF\PresentationCore.dll", "/r:$frameworkPath\WPF\PresentationFramework.dll",
    "/r:$frameworkPath\WPF\WindowsBase.dll", "/r:$frameworkPath\System.Xaml.dll",
    "/r:$frameworkPath\System.Web.Extensions.dll", "/r:$frameworkPath\Microsoft.VisualBasic.dll",
    "/r:$projectRoot\vendor\WebView2\Microsoft.Web.WebView2.Core.dll", "/r:$projectRoot\vendor\WebView2\Microsoft.Web.WebView2.Wpf.dll",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    '/r:System.Net.Http.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll'
)
$resources = @(Get-ChildItem (Join-Path $projectRoot 'build\runtime-assets') -Filter '*.png' | ForEach-Object {
    $kind, $id = $_.BaseName.Split('-', 2)
    $category = if ($kind -eq 'basket') { 'Basket' } else { 'Envelope' }
    '/resource:' + $_.FullName + ',PickSnap.' + $category + '.' + $id
})
$resources += "/resource:$projectRoot\src\assets\PickSnap.ico,PickSnap.Icon"
$resources += "/resource:$projectRoot\scripts\update.ps1,PickSnap.UpdateScript"
$sources = (Get-ChildItem (Join-Path $projectRoot 'src') -Filter '*.cs').FullName
$executable = Join-Path $outputPath 'FeatureTests.exe'
& (Join-Path $frameworkPath 'csc.exe') /nologo /target:exe /platform:x64 /codepage:65001 /main:ScreenLine.FeatureTests "/win32manifest:$projectRoot\src\app.manifest" "/out:$executable" $resources $references $sources (Join-Path $PSScriptRoot 'FeatureTests.cs') (Join-Path $PSScriptRoot 'DemoRecorder.cs')
if ($LASTEXITCODE -ne 0) { throw 'Feature test compilation failed.' }
$testArguments = @()
if ($Demos) { $testArguments += '--demos' }
if ($UiOnly) { $testArguments += '--ui-only' }
if ($Dark) { $testArguments += '--dark' }; if ($Render) { $testArguments += '--render' }
& $executable @testArguments
if ($LASTEXITCODE -ne 0) { throw 'Feature regression checks failed.' }
