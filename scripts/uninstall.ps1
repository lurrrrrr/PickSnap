param(
    [ValidateSet('Ask', 'KeepData', 'RemoveAll')][string]$Mode = 'Ask'
)
$ErrorActionPreference = 'Stop'

# 拾帧卸载程序：先确认，再停程序、清快捷方式与注册表、清缓存，最后删文件。
# 原则：默认保留用户收集的图片与设置（data 目录），因为删掉无法恢复；
# 想彻底清空必须显式选择「不保留」。

Add-Type -AssemblyName System.Windows.Forms | Out-Null
$installPath = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\PickSnap'))
$dataPath = Join-Path $installPath 'data'
$exePath = Join-Path $installPath 'PickSnap.exe'
$cacheRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PickSnap'

function Show-Info([string]$text, [string]$icon = 'Information') {
    [System.Windows.Forms.MessageBox]::Show($text, '拾帧 PickSnap', 'OK', $icon) | Out-Null
}

# 便携运行（没走 Install.cmd）时没有安装目录，不碰注册表和快捷方式；
# 只处理 data 的去留，让「卸载后始终只有一份数据」，其余文件用户删文件夹即可。
if (-not [string]::Equals([IO.Path]::GetFullPath($PSScriptRoot), $installPath, [StringComparison]::OrdinalIgnoreCase)) {
    $stagedData = Join-Path $cacheRoot 'Data'
    $portableData = Join-Path ([IO.Path]::GetFullPath($PSScriptRoot)) 'data'
    $keep = $Mode -ne 'RemoveAll'
    if ($Mode -eq 'Ask') {
        $answer = [System.Windows.Forms.MessageBox]::Show(
            ("当前是便携运行的拾帧。要保留这个文件夹 data 目录里的图片和设置吗？`n`n是：保留（暂存到系统里，之后用 Install.cmd 安装会自动迁回）`n否：连同图片和设置一起删除，无法恢复"),
            '图片与设置', 'YesNo', 'Question', 'Button1')
        $keep = $answer -eq 'Yes'
    }
    if ($keep -and (Test-Path -LiteralPath $portableData)) {
        foreach ($name in @('EditorCache', 'error.log')) {
            $path = Join-Path $portableData $name
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue }
        }
        # 旧的暂存只会在「卸载后从未装回」时出现，属于被放弃的上一次暂存，覆盖它。
        if (Test-Path -LiteralPath $stagedData) { Remove-Item -LiteralPath $stagedData -Recurse -Force -ErrorAction SilentlyContinue }
        Move-Item -LiteralPath $portableData -Destination $stagedData
        Show-Info ("图片与设置已暂存到：`n$stagedData`n`n之后用完整安装包里的 Install.cmd 安装，会自动迁回继续使用。`n当前文件夹里剩下的都是程序文件，直接删除整个文件夹即可。")
    }
    elseif ($keep) {
        Show-Info ("这个文件夹的 data 目录不存在（本来就没有收集内容）。`n`n程序文件直接删除整个文件夹即可。")
    }
    else {
        if (Test-Path -LiteralPath $portableData) { Remove-Item -LiteralPath $portableData -Recurse -Force -ErrorAction SilentlyContinue }
        if (Test-Path -LiteralPath $stagedData) { Remove-Item -LiteralPath $stagedData -Recurse -Force -ErrorAction SilentlyContinue }
        Show-Info '图片、设置与缓存已删除。当前文件夹里剩下的都是程序文件，直接删除整个文件夹即可。'
    }
    exit 0
}

if (-not (Test-Path -LiteralPath $installPath)) {
    Show-Info '没有找到拾帧的安装目录，可能已经卸载过了。'
    exit 0
}
if ((Get-Item -LiteralPath $installPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    Show-Info '安装目录是目录链接，为避免误删已停止卸载，请手动处理。'
    exit 1
}

# 第一步：确认要卸载。
if ($Mode -eq 'Ask') {
    $answer = [System.Windows.Forms.MessageBox]::Show(
        ("确定要卸载拾帧 PickSnap 吗？`n`n程序文件位于：`n$installPath`n`n下一步会让你选择是否保留已收集的图片和设置。"),
        '卸载拾帧', 'YesNo', 'Question', 'Button2')
    if ($answer -ne 'Yes') { exit 0 }
}

# 第二步：决定 data 的去留。默认按钮是「是」，即保留。
$keepData = $Mode -ne 'RemoveAll'
if ($Mode -eq 'Ask') {
    $answer = [System.Windows.Forms.MessageBox]::Show(
        ("是否保留已收集的图片和设置？`n`n是：只删除程序，图片与设置暂存到系统里，重新安装后自动迁回继续使用（推荐）`n否：连同图片和设置一起删除，无法恢复`n取消：中止卸载"),
        '图片与设置', 'YesNoCancel', 'Warning', 'Button1')
    if ($answer -eq 'Cancel') { exit 0 }
    $keepData = $answer -eq 'Yes'
}

# 先让程序正常退出：走 --quit，给它 10 秒；退不掉就停下来，避免删到一半留下半残状态。
$running = @(Get-CimInstance Win32_Process -Filter "Name = 'PickSnap.exe'" |
    Where-Object { $_.ExecutablePath -eq $exePath })
if ($running.Count) {
    if (Test-Path -LiteralPath $exePath) {
        Start-Process -FilePath $exePath -ArgumentList '--quit' -WindowStyle Hidden -Wait -ErrorAction SilentlyContinue
    }
    foreach ($process in $running) {
        $active = Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue
        if ($active -and -not $active.WaitForExit(10000)) {
            Show-Info '拾帧仍在运行，卸载已停止。请先在托盘退出拾帧（或处理未保存的图片）后重试。' 'Warning'
            exit 1
        }
    }
}
Start-Sleep -Milliseconds 300

# 桌面快捷方式：只删指向本安装目录的那一个，别误伤同名的其他快捷方式。
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PickSnap.lnk'
if (Test-Path -LiteralPath $shortcutPath) {
    try {
        $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
        if ($shortcut.TargetPath -eq $exePath) { Remove-Item -LiteralPath $shortcutPath -Force }
    }
    catch { }
}

# 注册表：开机启动项与「设置 → 应用」里的卸载条目。
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PickSnap' -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PickSnap' -Recurse -Force -ErrorAction SilentlyContinue

# 更新缓存（下载过的安装包，纯缓存，不属于用户数据），无论保留与否都清掉；
# 选择不保留时，连之前卸载暂存的数据也一并清掉，做到真正无残留。
foreach ($name in @('Updates')) {
    $path = Join-Path $cacheRoot $name
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
    }
}
if (-not $keepData) {
    $staged = Join-Path $cacheRoot 'Data'
    if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged -Recurse -Force -ErrorAction SilentlyContinue }
}

function Remove-Everything([string]$path) {
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        try {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
            return $true
        }
        catch { Start-Sleep -Milliseconds 350 }
    }
    return $false
}

# 删文件：逐个顶层项处理并重试，某个文件被占用时记下来，最后统一告知，而不是中途中断。
$failed = New-Object 'System.Collections.Generic.List[string]'
foreach ($child in @(Get-ChildItem -LiteralPath $installPath -Force)) {
    if ($keepData -and $child.Name -ieq 'data') { continue }
    if (-not (Remove-Everything $child.FullName)) { $failed.Add($child.Name) }
}

$pictureCount = 0
$stagedData = Join-Path $cacheRoot 'Data'
if ($keepData -and (Test-Path -LiteralPath $dataPath)) {
    # data 里唯一可以丢的是编辑器的 WebView2 缓存和错误日志，删掉能省几十 MB，重新打开会自动重建。
    foreach ($name in @('EditorCache', 'error.log')) {
        $path = Join-Path $dataPath $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue }
    }
    $captures = Join-Path $dataPath 'Captures'
    if (Test-Path -LiteralPath $captures) {
        $pictureCount = @(Get-ChildItem -LiteralPath $captures -File -ErrorAction SilentlyContinue).Count
    }
    # 把 data 暂存到系统固定位置（而不是留在安装目录），安装目录就能整个删掉；
    # 重新运行 Install.cmd 时安装脚本会把它迁回新目录，始终只有这一份数据。
    if (Test-Path -LiteralPath $stagedData) { Remove-Item -LiteralPath $stagedData -Recurse -Force -ErrorAction SilentlyContinue }
    Move-Item -LiteralPath $dataPath -Destination $stagedData
}

# 目录空了就连同外层缓存目录一起收掉；保留的数据已暂存到系统里，安装目录也能整个删掉。
$leftover = @(Get-ChildItem -LiteralPath $installPath -Force -ErrorAction SilentlyContinue).Count
if ($leftover -eq 0 -and $failed.Count -eq 0) {
    Remove-Item -LiteralPath $installPath -Recurse -Force -ErrorAction SilentlyContinue
    if ((Test-Path -LiteralPath $cacheRoot) -and @(Get-ChildItem -LiteralPath $cacheRoot -Force -ErrorAction SilentlyContinue).Count -eq 0) {
        Remove-Item -LiteralPath $cacheRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# 解压文件夹是用户自己解压出来的，不在安装/卸载脚本的管辖范围内（脚本也不知道它在哪），
# 明确告知一声，免得用户以为卸载没删干净。
$sourceNote = "`n`n安装时解压出来的文件夹（含 Install.cmd）不属于卸载范围，如仍保留可自行整个删除；程序已装在上面的固定目录，与它无关。"

if ($failed.Count -gt 0) {
    Show-Info ("拾帧已卸载，但下面这些文件没能删除（可能正被其他程序占用）：`n`n" + ($failed -join "`n") + "`n`n稍后手动删除这个目录即可：`n$installPath") 'Warning'
}
elseif ($keepData -and (Test-Path -LiteralPath $stagedData)) {
    Show-Info ("拾帧已卸载。`n`n图片与设置已保留（共 $pictureCount 张截图），暂存在：`n$stagedData`n`n重新运行 Install.cmd 安装后会自动迁回，继续使用这些内容。`n如果不再需要，直接删除上面这个目录即可。" + $sourceNote)
}
elseif ($keepData) {
    Show-Info ('拾帧已卸载。安装目录里没有找到已收集的图片和设置（可能之前卸载时已暂存），重新安装后会自动恢复。' + $sourceNote)
}
else {
    Show-Info ('拾帧已卸载，程序、图片、设置和缓存都已删除。' + $sourceNote)
}
exit 0
