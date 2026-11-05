$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$outputPath = Join-Path $projectRoot 'build\safety-tests'
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$references = @(
    "/r:$frameworkPath\WPF\PresentationCore.dll",
    "/r:$frameworkPath\WPF\PresentationFramework.dll",
    "/r:$frameworkPath\WPF\WindowsBase.dll",
    "/r:$frameworkPath\System.Xaml.dll",
    "/r:$frameworkPath\System.Web.Extensions.dll",
    "/r:$frameworkPath\Microsoft.VisualBasic.dll",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll'
)
$sources = @('Core.cs', 'CapturePipeline.cs', 'WindowsClipboard.cs', 'WindowsNative.cs', 'RevealPolicy.cs') | ForEach-Object { Join-Path $projectRoot ('src\' + $_) }
$executable = Join-Path $outputPath 'SafetyTests.exe'
& (Join-Path $frameworkPath 'csc.exe') /nologo /target:exe /platform:x64 /codepage:65001 "/out:$executable" $references $sources (Join-Path $PSScriptRoot 'SafetyTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Safety test compilation failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Safety regression checks failed.' }
