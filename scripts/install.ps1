$ErrorActionPreference = 'Stop'
$installPath = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\PickSnap'))
$sourcePath = Join-Path $PSScriptRoot 'app'
. (Join-Path $sourcePath 'maintenance-common.ps1')
Assert-PickSnapTree $sourcePath $sourcePath
Assert-PickSnapTree $installPath $installPath
if ((Test-Path -LiteralPath (Join-Path $sourcePath 'data')) -or (Test-Path -LiteralPath (Join-Path $sourcePath 'preview-data'))) { throw '安装包不能包含用户数据。' }
if (-not (Test-Path -LiteralPath (Join-Path $sourcePath 'PickSnap.exe'))) { throw '安装文件不完整，请先完整解压安装包。' }
$framework = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full'
if ($framework.Release -lt 528040) { throw '请先安装 .NET Framework 4.8。' }
$running = @(Get-CimInstance Win32_Process -Filter "Name = 'PickSnap.exe'")
if ($running.Count) { throw '请先从托盘退出 PickSnap，再重新安装。' }
if ((Test-Path -LiteralPath $installPath) -and ((Get-Item -LiteralPath $installPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '安装目录不能是目录链接。' }
New-Item -ItemType Directory -Path $installPath -Force | Out-Null
Copy-Item -Path (Join-Path $sourcePath '*') -Destination $installPath -Recurse -Force

# 卸载时选择「保留数据」的话，data 被暂存在 %LOCALAPPDATA%\PickSnap\Data；
# 装回时迁入新安装目录，重新启动程序即可继续使用原来的图片和设置。
$stagedData = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PickSnap\Data'
$targetData = Join-Path $installPath 'data'
if ((Test-Path -LiteralPath $stagedData) -and -not (Test-Path -LiteralPath $targetData)) {
    Assert-PickSnapTree $stagedData (Split-Path $stagedData -Parent)
    Assert-PickSnapPath $targetData $installPath
    Move-Item -LiteralPath $stagedData -Destination $targetData
}
$exePath = Join-Path $installPath 'PickSnap.exe'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'PickSnap.lnk'))
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $installPath
$shortcut.IconLocation = $exePath + ',0'
$shortcut.Description = 'PickSnap：把截图挂在屏幕上沿，随手取用。'
$shortcut.Save()
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PickSnap'
New-Item -Path $uninstallKey -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'PickSnap' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value ([Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).ProductVersion) -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installPath -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value ($exePath + ',0') -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name Publisher -Value 'lurrrrrr' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name URLInfoAbout -Value 'https://github.com/lurrrrrr/PickSnap' -PropertyType String -Force | Out-Null
# 从「设置 → 应用」点卸载时不弹控制台黑框：脚本本身用对话框确认，窗口隐藏即可。
$uninstallCommand = 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $installPath 'uninstall.ps1') + '"'
New-ItemProperty -Path $uninstallKey -Name UninstallString -Value $uninstallCommand -PropertyType String -Force | Out-Null
$size = [int](((Get-ChildItem -LiteralPath $installPath -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum) / 1KB)
New-ItemProperty -Path $uninstallKey -Name EstimatedSize -Value $size -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
Start-Process -FilePath $exePath -WindowStyle Hidden

# 程序文件全部是复制过来的，装完解压目录就不再是运行所需。用对话框明确告知可删，
# 并写出具体路径——之前只有一句 Write-Output，Install.cmd 的窗口一闪就关，用户根本看不到。
$sourcePath = [IO.Path]::GetFullPath($PSScriptRoot)
Add-Type -AssemblyName System.Windows.Forms | Out-Null
[System.Windows.Forms.MessageBox]::Show(
    ("PickSnap 安装完成，桌面已创建 PickSnap 快捷方式。`n`n程序已安装到：`n$installPath`n`n本次解压出来的文件夹已经不再需要，可以整个删除：`n$sourcePath`n`n卸载程序只清理上面的安装目录，不会动这个解压文件夹。`n（想留着以后重装，保留下载的 zip 安装包本身即可。）"),
    'PickSnap 安装完成', 'OK', 'Information') | Out-Null
Write-Output 'PickSnap 安装完成，桌面已创建 PickSnap 快捷方式。'
