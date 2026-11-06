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

# WPF completion view with installation and cleanup locations.
Add-Type -AssemblyName PresentationFramework
[xml]$completionXaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Title="PickSnap" Width="510" MaxHeight="700" SizeToContent="Height" ResizeMode="NoResize" WindowStartupLocation="CenterScreen" Background="#F5F7FA" FontFamily="Microsoft YaHei" ShowInTaskbar="True">
 <Window.Resources>
  <Style TargetType="Button"><Setter Property="Background" Value="#2866AD"/><Setter Property="Foreground" Value="White"/><Setter Property="Padding" Value="24,10"/><Setter Property="FontSize" Value="14"/>
   <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#215793"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter Property="Opacity" Value="0.85"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
  </Style>
 </Window.Resources>
 <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="20">
  <Grid Margin="0,0,0,16">
   <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
   <TextBlock Text="PickSnap 安装完成" FontSize="21" FontWeight="SemiBold" Foreground="#223247" VerticalAlignment="Center"/>
   <TextBlock Grid.Column="1" Text="已创建快捷方式" FontSize="13" Foreground="#62748A" VerticalAlignment="Center" Margin="16,0,0,0"/>
  </Grid>
  <Border Background="White" CornerRadius="10" Padding="10" Margin="0,0,0,12">
   <StackPanel><TextBlock Text="安装位置" FontSize="12" Foreground="#6A7A8E"/><TextBlock x:Name="InstallLocation" FontSize="13" Foreground="#223247" TextWrapping="Wrap" Margin="0,8,0,0"/></StackPanel>
  </Border>
  <Border Background="White" CornerRadius="10" Padding="10" Margin="0,0,0,12">
   <StackPanel>
    <TextBlock Text="解压文件夹可以删除" FontSize="14" FontWeight="SemiBold" Foreground="#223247"/>
    <TextBlock Text="程序已安装完成，下面这个解压文件夹已不再需要，可以整个删除：" FontSize="13" Foreground="#62748A" TextWrapping="Wrap" Margin="0,8,0,0"/>
    <TextBlock x:Name="ExtractLocation" FontSize="13" Foreground="#223247" TextWrapping="Wrap" Margin="0,8,0,0"/>
   </StackPanel>
  </Border>
  <TextBlock Text="卸载时会清理上方的安装目录、快捷方式和开机启动项，不会删除这个解压文件夹。图片、文字与设置是否保留，由你在卸载时选择。" FontSize="13" Foreground="#52647A" TextWrapping="Wrap"/>
  <TextBlock Text="如需留着以后重装，保留下载的 ZIP 安装包即可。" FontSize="13" Foreground="#62748A" TextWrapping="Wrap" Margin="0,10,0,0"/>
  <Button x:Name="Finish" Content="完成" HorizontalAlignment="Right" MinWidth="110" Margin="0,16,0,0" IsDefault="True" IsCancel="True"/>
 </StackPanel></ScrollViewer>
</Window>
"@
$completionWindow = [Windows.Markup.XamlReader]::Load((New-Object System.Xml.XmlNodeReader $completionXaml))
# Use the installed application's icon instead of the PowerShell host icon.
Add-Type -AssemblyName System.Drawing
$appIcon = [Drawing.Icon]::ExtractAssociatedIcon($exePath)
try {
    $completionWindow.Icon = [Windows.Interop.Imaging]::CreateBitmapSourceFromHIcon($appIcon.Handle, [Windows.Int32Rect]::Empty, [Windows.Media.Imaging.BitmapSizeOptions]::FromEmptyOptions())
    $completionWindow.Icon.Freeze()
} finally { if ($appIcon) { $appIcon.Dispose() } }
$completionWindow.FindName('InstallLocation').Text = $installPath
$completionWindow.FindName('ExtractLocation').Text = [IO.Path]::GetFullPath($PSScriptRoot)
$completionWindow.FindName('Finish').Add_Click({ $completionWindow.Close() })
$completionWindow.ShowDialog() | Out-Null
Write-Output 'PickSnap 安装完成，桌面已创建 PickSnap 快捷方式。'
