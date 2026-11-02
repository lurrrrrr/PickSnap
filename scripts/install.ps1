$ErrorActionPreference = 'Stop'
$installPath = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\PickSnap'))
$sourcePath = Join-Path $PSScriptRoot 'app'
if (-not (Test-Path -LiteralPath (Join-Path $sourcePath 'PickSnap.exe'))) { throw '安装文件不完整，请先完整解压安装包。' }
$framework = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full'
if ($framework.Release -lt 528040) { throw '请先安装 .NET Framework 4.8。' }
$running = @(Get-CimInstance Win32_Process -Filter "Name = 'PickSnap.exe'")
if ($running.Count) { throw '请先从托盘退出拾帧，再重新安装。' }
if ((Test-Path -LiteralPath $installPath) -and ((Get-Item -LiteralPath $installPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '安装目录不能是目录链接。' }
New-Item -ItemType Directory -Path $installPath -Force | Out-Null
Copy-Item -Path (Join-Path $sourcePath '*') -Destination $installPath -Recurse -Force

# 卸载时选择「保留数据」的话，data 被暂存在 %LOCALAPPDATA%\PickSnap\Data；
# 装回时迁入新安装目录，重新启动程序即可继续使用原来的图片和设置。
$stagedData = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PickSnap\Data'
$targetData = Join-Path $installPath 'data'
if ((Test-Path -LiteralPath $stagedData) -and -not (Test-Path -LiteralPath $targetData)) {
    Move-Item -LiteralPath $stagedData -Destination $targetData
}
$exePath = Join-Path $installPath 'PickSnap.exe'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'PickSnap.lnk'))
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $installPath
$shortcut.IconLocation = $exePath + ',0'
$shortcut.Description = '拾帧：把截图挂在屏幕上沿，随手取用。'
$shortcut.Save()
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PickSnap'
New-Item -Path $uninstallKey -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'PickSnap · 拾帧' -PropertyType String -Force | Out-Null
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
Write-Output '拾帧安装完成，桌面已创建 PickSnap 快捷方式。'
