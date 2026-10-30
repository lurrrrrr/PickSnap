param([switch]$Package)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerPath = Join-Path $frameworkPath 'csc.exe'
$buildPath = Join-Path $projectRoot 'build'
New-Item -ItemType Directory -Path $buildPath -Force | Out-Null
$references = @(
    "/r:$frameworkPath\WPF\PresentationCore.dll",
    "/r:$frameworkPath\WPF\PresentationFramework.dll",
    "/r:$frameworkPath\WPF\WindowsBase.dll",
    "/r:$frameworkPath\System.Xaml.dll",
    "/r:$frameworkPath\System.Web.Extensions.dll",
    "/r:$frameworkPath\Microsoft.VisualBasic.dll",
    "/r:$projectRoot\vendor\WebView2\Microsoft.Web.WebView2.Core.dll",
    "/r:$projectRoot\vendor\WebView2\Microsoft.Web.WebView2.Wpf.dll",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll'
    '/r:System.Net.Http.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll'
)
$sources = (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs').FullName
$manifestPath = Join-Path $projectRoot 'src\app.manifest'
$iconPath = Join-Path $projectRoot 'src\assets\PickSnap.ico'
& (Join-Path $PSScriptRoot 'prepare-assets.ps1')
$basketResources = Get-ChildItem -LiteralPath (Join-Path $buildPath 'runtime-assets') -Filter 'basket-*.png' | ForEach-Object { '/resource:' + $_.FullName + ',PickSnap.Basket.' + $_.BaseName.Substring(7) }
$basketResources += @(Get-ChildItem -LiteralPath (Join-Path $buildPath 'runtime-assets') -Filter 'envelope-*.png' | ForEach-Object { '/resource:' + $_.FullName + ',PickSnap.Envelope.' + $_.BaseName.Substring(9) })
& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$buildPath\PickSnap.exe" "/win32manifest:$manifestPath" "/win32icon:$iconPath" "/resource:$iconPath,PickSnap.Icon" $basketResources $references $sources
if ($LASTEXITCODE -ne 0) { throw '应用构建失败' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\App.config') -Destination (Join-Path $buildPath 'PickSnap.exe.config')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'update.ps1') -Destination $buildPath
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE'),(Join-Path $projectRoot 'NOTICE') -Destination $buildPath
$editorPath = Join-Path $buildPath 'editor'
$noticePath = Join-Path $buildPath 'third-party-notices'
New-Item -ItemType Directory -Path $editorPath,(Join-Path $noticePath 'Filerobot'),(Join-Path $noticePath 'WebView2') -Force | Out-Null
Copy-Item -Path (Join-Path $projectRoot 'src\editor\*') -Destination $editorPath
Copy-Item -LiteralPath (Join-Path $projectRoot 'vendor\Filerobot\filerobot-image-editor.min.js') -Destination $editorPath
Copy-Item -LiteralPath (Join-Path $projectRoot 'vendor\Filerobot\LICENSE'),(Join-Path $projectRoot 'vendor\Filerobot\THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $noticePath 'Filerobot')
Copy-Item -LiteralPath (Join-Path $projectRoot 'vendor\WebView2\LICENSE.txt'),(Join-Path $projectRoot 'vendor\WebView2\NOTICE.txt') -Destination (Join-Path $noticePath 'WebView2')
Copy-Item -LiteralPath (Join-Path $projectRoot 'vendor\WebView2\Microsoft.Web.WebView2.Core.dll'),(Join-Path $projectRoot 'vendor\WebView2\Microsoft.Web.WebView2.Wpf.dll'),(Join-Path $projectRoot 'vendor\WebView2\WebView2Loader.dll') -Destination $buildPath
Write-Output ('构建完成：' + (Join-Path $buildPath 'PickSnap.exe'))
if ($Package) { & (Join-Path $PSScriptRoot 'package.ps1') }
