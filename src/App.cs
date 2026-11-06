using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("PickSnap")]
[assembly: AssemblyProduct("PickSnap")]
[assembly: AssemblyDescription("PickSnap：把截图挂在屏幕上沿，随手复制、拖拽和保存。")]
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]
[assembly: AssemblyInformationalVersion("2.1.0")]

namespace ScreenLine
{
    internal sealed class DesktopController : IDisposable
    {
        public readonly Gallery Gallery;
        public readonly CapturePipeline Captures;
        public readonly TextCollection Texts;
        public readonly ShelfWindow Shelf;
        readonly WindowsEvents events;
        readonly Forms.NotifyIcon tray = new Forms.NotifyIcon();
        LightNotice notice;
        internal readonly TrayPanel TrayMenu;
        internal AppearanceWindow Appearance { get; private set; }
        internal BasketWindow Basket { get; private set; }
        internal TextWindow TextWindow { get; private set; }
        internal CaptureWindow Capture { get; private set; }
        internal ShortcutWindow ShortcutSettings { get; private set; }
        internal readonly List<ImageEditorWindow> Editors = new List<ImageEditorWindow>();
        readonly AppUpdates updates;
        readonly CancellationTokenSource updateLifetime = new CancellationTokenSource();
        const int BasketLimit = 100;
        int lastCollectedCount;
        DateTime lastBasketNotice = DateTime.MinValue;
        readonly DispatcherTimer updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        // 每 6 小时复查一次旧图：软件常驻数天也不会错过清理时机。（开关在设置 → 收集与清理）
        readonly DispatcherTimer purgeTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        readonly bool automaticUpdates;
        internal SettingsWindow Settings { get; private set; }
        UpdateRelease availableUpdate;
        UpdateWindow updateWindow;
        UninstallWindow uninstallWindow;
        bool disposed, started;
        Task<UpdateCheckResult> updateCheckTask;
        internal UpdateCheckResult LastUpdateCheck { get; private set; }
        uint textClipboardSequence;
        int textClipboardVersion;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // 在桌面创建「PickSnap」快捷方式：首次启动自动执行一次，设置 → 更新与卸载中也可随时重建。
        // 便携 zip 没有“安装”环节，快捷方式只能由应用自己补——经 WScript.Shell COM（反射调用，无需额外引用）。
        // 不写 Description：悬停时只显示名字，不带宣传语。
        public static bool CreateDesktopShortcut()
        {
            string exe = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return false;
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop)) return false;
            string link = Path.Combine(desktop, "PickSnap.lnk");
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            object shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
            shortcut.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exe });
            shortcut.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(exe) });
            shortcut.GetType().InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { exe + ",0" });
            shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            return File.Exists(link);
        }

        public DesktopController(StateStore store, string screenshots, bool automaticUpdates = true, AppUpdates updateClient = null)
        {
            this.automaticUpdates = automaticUpdates; updates = updateClient ?? new AppUpdates();
            LastUpdateCheck = new UpdateCheckResult { Phase = UpdateCheckPhase.Idle, Message = "尚未检查更新" };
            Gallery = new Gallery(store);
            Texts = new TextCollection(store);
            Captures = new CapturePipeline(Gallery, Application.Current.Dispatcher, screenshots);
            Shelf = new ShelfWindow(Gallery, Act, EnableClipboard);
            Shelf.BasketRequested += ShowBasket;
            Shelf.RopeStatusChanged += delegate { if (Basket != null) Basket.RefreshCapacity(); };
            Shelf.TextRequested += ShowTexts;
            // 绳子换屏重排时会把自己置顶，篮子跟着提回来，避免被压在下面点不动。
            Shelf.RopeMoved += delegate { if (Basket != null) Basket.BringToFront(); };
            Texts.Changed += Shelf.UpdateTexts;
            events = new WindowsEvents(false);
            events.RegisterToggle(store.State.ToggleModifiers, store.State.ToggleKey); events.RegisterCapture(store.State.CaptureModifiers, store.State.CaptureKey);
            events.CaptureRequested += ShowCapture;
            events.ToggleRequested += ToggleShelf;
            events.SettingsRequested += delegate { ShowSettings(); };
            events.UninstallRequested += delegate { ShowSettings("更新与卸载"); Uninstall(); };
            events.ExitRequested += Exit;
            textClipboardSequence = WindowsNative.GetClipboardSequenceNumber();
            Captures.Arrived += Shelf.ShowNewCapture;
            Captures.Failed += Notice;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PickSnap.Icon"))
                tray.Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            tray.Text = "PickSnap";
            TrayMenu = new TrayPanel();
            TrayMenu.OnAction(TrayMenu.VisibilityButton, ToggleShelf);
            UpdateShortcutLabels();
            if (!events.ShortcutAvailable || !events.CaptureShortcutAvailable) Notice("部分快捷键被占用，请在设置 → 快捷键中更改。");
            TrayMenu.OnAction(TrayMenu.BasketButton, ShowBasket); TrayMenu.OnAction(TrayMenu.TextButton, ShowTexts);
            TrayMenu.OnAction(TrayMenu.CaptureButton, ShowCapture);
            Gallery.Changed += CheckCacheLimit; lastCollectedCount = Gallery.Collected.Count;
            TrayMenu.OnAction(TrayMenu.AddButton, async delegate
            {
                var picker = new OpenFileDialog { Title = "添加图片", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff", Multiselect = true };
                if (picker.ShowDialog() != true) return;
                foreach (string file in picker.FileNames) if (!await Captures.Import(file, null, true)) Notice("无法读取图片：" + Path.GetFileName(file));
            });
            TrayMenu.OnCollectionToggle(TrayMenu.PicturesSwitch, () => Captures.CollectClipboard, SetClipboardImages);
            TrayMenu.OnCollectionToggle(TrayMenu.TextSwitch, () => Texts.CollectClipboard, SetClipboardText);
            TrayMenu.Opened += delegate { TrayMenu.SetVisibility(Shelf.Revealed); TrayMenu.SyncCollection(Captures.CollectClipboard, Texts.CollectClipboard); UpdateShortcutLabels(); };
            TrayMenu.OnAction(TrayMenu.SettingsButton, delegate { ShowSettings(); });
            purgeTimer.Tick += delegate { RunAutoPurge(false); };
            if (store.State.AutoPurge) purgeTimer.Start();
            updateTimer.Tick += async delegate { updateTimer.Interval = TimeSpan.FromHours(12); await CheckForUpdates(false); };
            tray.BalloonTipClicked += delegate { ShowUpdate(); };
            TrayMenu.OnAction(TrayMenu.ExitButton, Exit);
            tray.MouseUp += delegate(object sender, Forms.MouseEventArgs args) { if (args.Button == Forms.MouseButtons.Right) ShowTrayMenu(); };
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs args) { if (args.Button == Forms.MouseButtons.Left) ToggleShelf(); };
            tray.Visible = true;
        }

        async void Exit()
        {
            foreach (var editor in Editors.ToArray()) if (!await editor.ConfirmClose()) return;
            Application.Current.Shutdown();
        }

        // 先在应用内选择并确认，再将明确的数据处理方式交给维护脚本。
        // 取消或编辑未完成时不启动脚本；外部卸载入口也复用此对话框。
        internal void Uninstall()
        {
            if (uninstallWindow != null) { uninstallWindow.Activate(); return; }
            uninstallWindow = new UninstallWindow(async delegate(bool removeData)
            {
                foreach (var editor in Editors.ToArray()) if (!await editor.ConfirmClose()) return false;
                string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uninstall.ps1");
                if (!File.Exists(script)) throw new FileNotFoundException("卸载程序缺失，请使用完整安装包修复。", script);
                Process.Start(new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\" -Mode " + (removeData ? "RemoveAll" : "KeepData") });
                return true;
            }, Gallery.Store.Root) { Owner = Settings };
            try { uninstallWindow.ShowDialog(); } finally { uninstallWindow = null; }
        }

        internal void ShowSettings(string page = null)
        {
            TrayMenu.Close();
            if (Settings == null)
            {
                bool wasRevealed = Shelf.Revealed;
                if (Basket != null) Basket.Close(); if (TextWindow != null) TextWindow.Close();
                Shelf.Reveal(false, false, 0);
                Settings = new SettingsWindow(this);
                Settings.Closed += delegate { Settings = null; if (!disposed && wasRevealed) Shelf.Reveal(true, false, 2, true); };
                Settings.Show();
            }
            if (page != null) Settings.SelectPage(page);
            if (Settings.WindowState == WindowState.Minimized) Settings.WindowState = WindowState.Normal;
            Settings.Activate();
        }

        internal void SetPreference<T>(Func<T> get, Action<T> set, T value)
        {
            T previous = get(); set(value);
            try { Gallery.Store.Save(); } catch { set(previous); throw; }
            Shelf.Preview.SetEnabled(Gallery.Store.State.HoverPreview); Shelf.UpdateTexts();
            TrayMenu.SyncCollection(Captures.CollectClipboard, Texts.CollectClipboard);
            Captures.IgnoreCurrentClipboard();
            textClipboardVersion++; textClipboardSequence = WindowsNative.GetClipboardSequenceNumber();
        }

        internal bool StartupEnabled
        {
            get { using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) return key != null && string.Equals(key.GetValue("PickSnap") as string, Quote(Assembly.GetEntryAssembly().Location), StringComparison.OrdinalIgnoreCase); }
        }
        internal void SetStartup(bool value)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) { if (value) key.SetValue("PickSnap", Quote(Assembly.GetEntryAssembly().Location)); else if (StartupEnabled) key.DeleteValue("PickSnap", false); }
        }
        internal void SyncCollectionControls()
        {
            TrayMenu.SyncCollection(Captures.CollectClipboard, Texts.CollectClipboard); Shelf.UpdateTexts();
            if (Settings != null && Settings.CurrentPage == SettingsWindow.Pages[2]) Settings.RefreshPage();
        }
        internal void SetClipboardImages(bool value) { Captures.CollectClipboard = value; SyncCollectionControls(); }
        internal void SetClipboardText(bool value)
        {
            Texts.CollectClipboard = value; textClipboardVersion++; textClipboardSequence = WindowsNative.GetClipboardSequenceNumber(); SyncCollectionControls();
        }
        internal void RebuildShortcut()
        {
            if (!CreateDesktopShortcut()) throw new IOException("无法创建桌面快捷方式。");
        }
        internal void EditShortcut(bool toggle)
        {
            if (ShortcutSettings != null) { ShortcutSettings.Activate(); return; }
            var state = Gallery.Store.State;
            ShortcutSettings = new ShortcutWindow(toggle ? state.ToggleModifiers : state.CaptureModifiers, toggle ? state.ToggleKey : state.CaptureKey,
                toggle ? (Func<uint, uint, bool>)SetToggleShortcut : SetCaptureShortcut, toggle ? "显示 / 收起绳子" : "区域截图", () => ShortcutError);
            ShortcutSettings.Closed += delegate { ShortcutSettings = null; if (Settings != null) Settings.RefreshPage(); };
            ShortcutSettings.Owner = Settings; ShortcutSettings.ShowDialog();
        }
        internal bool SetToggleShortcut(uint modifiers, uint key) { return SetShortcut(true, modifiers, key); }
        bool SetShortcut(bool toggle, uint modifiers, uint key)
        {
            ShortcutError = "组合键已被其他程序或另一个操作占用，请换一组。";
            var state = Gallery.Store.State;
            uint previousModifiers = toggle ? state.ToggleModifiers : state.CaptureModifiers;
            uint previousKey = toggle ? state.ToggleKey : state.CaptureKey;
            if (toggle) { state.ToggleModifiers = modifiers; state.ToggleKey = key; }
            else { state.CaptureModifiers = modifiers; state.CaptureKey = key; }
            bool committed = false;
            try
            {
                // 先占住新组合键，保存成功后再释放旧绑定。失败时只释放新绑定。
                committed = toggle ? events.RegisterToggle(modifiers, key, Gallery.Store.Save) : events.RegisterCapture(modifiers, key, Gallery.Store.Save);
                if (committed) UpdateShortcutLabels();
            }
            catch (Exception error) { Gallery.Store.Log(error); ShortcutError = "无法保存设置，请检查 data 文件夹权限后重试。"; }
            finally
            {
                if (!committed) { if (toggle) { state.ToggleModifiers = previousModifiers; state.ToggleKey = previousKey; } else { state.CaptureModifiers = previousModifiers; state.CaptureKey = previousKey; } }
            }
            return committed;
        }

        public async Task Start()
        {
            if (started) return;
            started = true;
            Shelf.Show();
            Shelf.Reveal(true, false, 4);
            if (!string.IsNullOrEmpty(Gallery.Store.RecoveryNotice)) Notice(Gallery.Store.RecoveryNotice, 6, true);
            await Captures.Start();
            Captures.IgnoreCurrentClipboard();
            textClipboardSequence = WindowsNative.GetClipboardSequenceNumber();
            events.ClipboardChanged += Captures.ClipboardChanged;
            events.ClipboardChanged += CollectClipboardText;
            RunAutoPurge(false); AppUpdates.ClearStaleCache();
            if (automaticUpdates) updateTimer.Start();
        }

        // 旧图保留天数固定为 7：不给用户一个需要理解的档位设置，规则一句话能说清。
        const int PurgeDays = 7;

        // 收集设置中的开关回调：开启后立刻清一次，之后启动时与每 6 小时复查；状态持久保存。
        internal void SetAutoPurge(bool on)
        {
            SetPreference(() => Gallery.Store.State.AutoPurge, value => Gallery.Store.State.AutoPurge = value, on);
            if (on) { purgeTimer.Start(); RunAutoPurge(true); }
            else if (!on) { purgeTimer.Stop(); Notice("已关闭旧图自动清理"); }
        }

        // 按天数清理过期截图：notify 为 false 时只在真的清掉了东西才提示，避免每次启动打扰。
        void RunAutoPurge(bool notify)
        {
            // notify=true 来自手动操作（开关刚被打开），不受自动开关限制；定时器/启动触发的静默清理才要求开关已开。
            if (disposed || (!notify && !Gallery.Store.State.AutoPurge)) return;
            try
            {
                int removed = Gallery.PurgeOlderThan(PurgeDays);
                if (removed > 0) Notice("已自动清理 " + removed + " 张超过 " + PurgeDays + " 天的图片");
                else if (notify) Notice("已开启旧图自动清理：当前没有超过 " + PurgeDays + " 天的截图");
            }
            catch (Exception error) { Gallery.Store.Log(error); if (notify) Notice("清理未完成：" + error.Message); }
        }

        internal string ShortcutError { get; private set; }
        internal bool ToggleShortcutAvailable { get { return events.ShortcutAvailable; } }
        internal bool CaptureShortcutAvailable { get { return events.CaptureShortcutAvailable; } }
        void UpdateShortcutLabels()
        {
            var state = Gallery.Store.State;
            TrayMenu.SetShortcuts(events.ShortcutAvailable ? ShortcutWindow.Label(state.ToggleModifiers, state.ToggleKey) : "未生效", events.CaptureShortcutAvailable ? ShortcutWindow.Label(state.CaptureModifiers, state.CaptureKey) : "未生效");
        }
        internal void ToggleShelf()
        {
            if (ShortcutSettings != null || Shelf.Capturing) return;
            bool show = Settings != null || !Shelf.Revealed;
            if (Appearance != null) Appearance.Close();
            if (Settings != null) Settings.Close();
            TrayMenu.Close();
            if (Shelf.Revealed != show) Shelf.Toggle();
            else if (show) Shelf.Reveal(true, true, 0);
        }

        internal bool SetCaptureShortcut(uint modifiers, uint key) { return SetShortcut(false, modifiers, key); }

        internal async void ShowCapture()
        {
            if (disposed || Shelf.Capturing || ShortcutSettings != null) return;
            Shelf.Capturing = true;
            bool revealed = Shelf.Revealed;
            try
            {
                TrayMenu.Close(); if (Basket != null) Basket.Close(); if (TextWindow != null) TextWindow.Close();
                if (Appearance != null) Appearance.Close(); if (Settings != null) Settings.Close();
                Shelf.Reveal(false, false, 0); await Task.Delay(260);
                if (disposed) return;
                var result = await CaptureWindow.PickAcrossScreens(window => Capture = window);
                if (disposed) return;
                if (result != null) await Captures.AcceptCapture(result);
                else if (revealed) Shelf.Reveal(true, false, 2);
            }
            catch (Exception error) { if (!disposed) { Gallery.Store.Log(error); Notice("截图未完成，请重试。"); } }
            finally { if (Capture != null) Capture.Close(); Capture = null; Shelf.Capturing = false; }
        }

        internal Task<UpdateCheckResult> CheckForUpdates(bool manual)
        {
            if (updateCheckTask != null && !updateCheckTask.IsCompleted) return updateCheckTask;
            if (disposed) return Task.FromResult(LastUpdateCheck);
            LastUpdateCheck = new UpdateCheckResult { Phase = UpdateCheckPhase.Checking, Message = "正在检查更新…" };
            if (Settings != null) Settings.RefreshUpdateStatus();
            updateCheckTask = CheckUpdatesCore(manual);
            return updateCheckTask;
        }
        async Task<UpdateCheckResult> CheckUpdatesCore(bool manual)
        {
            await Task.Yield(); // 让共享任务先赋值，手动检查可等待已开始的后台请求。
            UpdateCheckResult result;
            try
            {
                availableUpdate = await updates.CheckAsync(updateLifetime.Token);
                if (string.IsNullOrEmpty(updates.LastSeenVersion)) throw new InvalidDataException("尚未找到可用的正式版本，请打开发布页查看。");
                Gallery.Store.State.LastConfirmedVersion = updates.LastSeenVersion;
                Gallery.Store.State.LastConfirmedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                try { Gallery.Store.Save(); } catch (Exception error) { Gallery.Store.Log(error); }
                result = new UpdateCheckResult { Phase = availableUpdate == null ? UpdateCheckPhase.Current : UpdateCheckPhase.Available, Release = availableUpdate, CheckedAt = DateTime.Now,
                    Message = availableUpdate == null ? "已是最新版本" : "发现新版本 v" + availableUpdate.Version + "，查看说明后即可更新。" };
                if (!disposed && !manual && availableUpdate != null && availableUpdate.Version != Gallery.Store.State.SkippedUpdate && Gallery.Store.State.LastUpdateNotification != availableUpdate.Version)
                {
                    tray.ShowBalloonTip(10000, "PickSnap 有新版本 " + availableUpdate.Version, "点击查看更新，下载与重启安装由你决定。", Forms.ToolTipIcon.Info);
                    Gallery.Store.State.LastUpdateNotification = availableUpdate.Version;
                    try { Gallery.Store.Save(); } catch (Exception error) { Gallery.Store.Log(error); }
                }
            }
            catch (Exception error)
            {
                Gallery.Store.Log(error);
                string reason = error is OperationCanceledException ? "检查超时或已取消，请检查网络或代理后重试。" : error is System.Net.Http.HttpRequestException ? "无法连接 GitHub，请检查网络或代理后重试。" : error.Message;
                result = new UpdateCheckResult { Phase = UpdateCheckPhase.Failed, CheckedAt = DateTime.Now, Message = "检查失败：" + reason };
            }
            LastUpdateCheck = result;
            if (!disposed && Settings != null) Settings.RefreshUpdateStatus();
            return result;
        }

        internal void ShowUpdate()
        {
            if (disposed || availableUpdate == null) return;
            if (updateWindow != null) { updateWindow.Activate(); return; }
            var release = availableUpdate;
            updateWindow = new UpdateWindow(release, updates, async delegate(string work)
            {
                foreach (var editor in Editors.ToArray()) if (!await editor.ConfirmClose()) return false;
                AppUpdates.Install(work);
                Application.Current.Shutdown();
                return true;
            }, delegate
            {
                // 跳过此版本：记录版本号，自动检查不再提醒它，直到下一个新版本；手动检查仍然可见。
                Gallery.Store.State.SkippedUpdate = release.Version;
                Gallery.Store.Save();
                LastUpdateCheck.Message = "已跳过 v" + release.Version + " 的自动提醒，仍可手动查看与安装。";
                if (Settings != null) Settings.RefreshUpdateStatus();
            }, async delegate { foreach (var editor in Editors.ToArray()) if (!await editor.ConfirmClose()) return false; return true; });
            updateWindow.Closed += delegate { updateWindow = null; };
            updateWindow.Owner = Settings; updateWindow.Topmost = true; updateWindow.WindowStartupLocation = Settings == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner; updateWindow.Show();
        }

        void EnableClipboard()
        {
            SetClipboardImages(true);
        }


        // 图片篮子软上限（100 张）：只提醒，不删除任何文件。
        // 只在「图片数量变多」时（即又收到一张新图）提示一次；删除、挂绳等操作波动不打扰。
        void CheckCacheLimit()
        {
            int count = Gallery.Collected.Count;
            bool grew = count > lastCollectedCount;
            lastCollectedCount = count;
            if (!grew || count <= BasketLimit || DateTime.UtcNow - lastBasketNotice < TimeSpan.FromHours(6)) return;
            lastBasketNotice = DateTime.UtcNow;
            Notice("图片篮子已超过 " + BasketLimit + " 张，请打开图片篮子清理不需要的截图。");
        }

        internal void SelectScreen(string device) { Shelf.SetDisplay(device); }

        async void CollectClipboardText()
        {
            uint sequence = WindowsNative.GetClipboardSequenceNumber();
            if (disposed || !Texts.CollectClipboard || sequence == textClipboardSequence) return;
            textClipboardSequence = sequence;
            int version = textClipboardVersion;
            try
            {
                string content = await WindowsClipboard.ReadText(sequence);
                if (disposed || !Texts.CollectClipboard || version != textClipboardVersion || sequence != WindowsNative.GetClipboardSequenceNumber()) return;
                Texts.Add(content);
            }
            catch (Exception error) { if (!disposed) Gallery.Store.Log(error); }
        }

        internal void ShowBasket()
        {
            if (Basket != null) { Basket.Activate(); return; }
            Gallery.Prune();
            bool wasRevealed = Shelf.Revealed;
            var screen = WindowsNative.ScreenAtPointer(); bool compact = screen.WorkingArea.Height / WindowsNative.MonitorScale(screen) < 650;
            Basket = new BasketWindow(Gallery, Captures.Hang, Act, Shelf.IsPinned, Shelf.RoomForHanging, () => Shelf.RopeCapacity, SetAutoPurge);
            Basket.Owner = Shelf;
            Shelf.ChoosingBasket = true;
            Shelf.Reveal(!compact, false, 0);
            Basket.Closed += delegate { Basket = null; Shelf.ChoosingBasket = false; if (!disposed && compact && wasRevealed && Settings == null) Shelf.Reveal(true, false, 2, true); };
            Basket.Show();
        }

        internal void ShowTexts()
        {
            if (TextWindow != null) { TextWindow.Close(); return; }
            // 收集关着且一条文字都没有时信封根本不摆出来（绳子右端没有锚点），
            // 硬开窗只会得到一块马上要自毁的空面板；轻提示一句就够。
            if (Texts.Items.Count == 0) { Notice(Texts.CollectClipboard ? "文字信封还是空的，复制文字后会收进这里。" : "文字信封还是空的，可从托盘开启「收集文字」。", 2, true); return; }
            Shelf.Reveal(true, false, 0);
            Shelf.ChoosingTexts = true;
            TextWindow = new TextWindow(Texts, Shelf);
            TextWindow.Closed += delegate { TextWindow = null; Shelf.ChoosingTexts = false; };
            TextWindow.Show();
        }

        async void ChangeTheme(string mode)
        {
            if (Gallery.Store.State.AppTheme == mode) return;
            bool requiresRestart = LightTheme.Resolve(mode) != LightTheme.Dark;
            if (requiresRestart)
                foreach (var editor in Editors.ToArray()) if (!await editor.ConfirmClose()) return;
            try
            {
                SetPreference(() => Gallery.Store.State.AppTheme, value => Gallery.Store.State.AppTheme = value, mode);
                if (requiresRestart) Program.Restart();
                else if (Appearance != null) Appearance.RefreshMode(mode);
            }
            catch (Exception error) { Gallery.Store.Log(error); Notice("配色保存失败，请重试"); }
        }

        internal void ShowAppearance()
        {
            if (Appearance != null) { Appearance.Activate(); return; }
            Appearance = new AppearanceWindow(Shelf.Appearance, Gallery, delegate(LineTheme theme)
            {
                Gallery.Store.State.Theme = theme.Id;
                Gallery.Store.Save();
                Shelf.ApplyTheme(theme);
                Shelf.Reveal(true, false, 3);
            }, Gallery.Store.State.AppTheme, ChangeTheme);
            Appearance.Owner = (Window)Settings ?? Shelf;
            Shelf.ChoosingAppearance = true;
            Shelf.Reveal(true, false, 0);
            Appearance.Closed += delegate { Appearance = null; Shelf.ChoosingAppearance = false; if (Settings != null) Shelf.Reveal(false, false, 0); };
            Appearance.Show();
        }

        internal void ShowTrayMenu()
        {
            WindowsNative.SetForegroundWindow(Shelf.Handle);
            var cursor = WindowsNative.Cursor();
            var point = Shelf.PointFromScreen(new Point(cursor.X, cursor.Y));
            TrayMenu.PlacementTarget = Shelf;
            TrayMenu.Placement = PlacementMode.RelativePoint;
            TrayMenu.HorizontalOffset = point.X - 16;
            TrayMenu.VerticalOffset = point.Y - 16;
            TrayMenu.IsOpen = true;
        }

        void Notice(string message) { Notice(message, 2, false); }

        void Notice(string message, int seconds) { Notice(message, seconds, false); }

        void Notice(string message, int seconds, bool plain)
        {
            if (notice != null) notice.Close();
            notice = new LightNotice(message, seconds, plain);
            var current = notice;
            current.Closed += delegate { if (notice == current) notice = null; };
            notice.Show();
        }

        static string Quote(string path) { return "\"" + path + "\""; }

        public async void Act(ShotAction action, CaptureItem item)
        {
            try
            {
                if (!File.Exists(item.Path)) { Gallery.Prune(); return; }
                switch (action)
                {
                    case ShotAction.RemoveLeft:
                    case ShotAction.RemoveRight:
                    case ShotAction.RemoveOthers:
                    case ShotAction.RemoveAll:
                        var hanging = Shelf.HangingItems();
                        int index = Array.IndexOf(hanging, item);
                        if (index < 0) break;
                        var targets = hanging.Where((image, position) => action == ShotAction.RemoveAll
                            || action == ShotAction.RemoveLeft && position < index
                            || action == ShotAction.RemoveRight && position > index
                            || action == ShotAction.RemoveOthers && position != index).ToArray();
                        Gallery.RemoveMany(targets);
                        break;
                    case ShotAction.TrashLeft:
                    case ShotAction.TrashRight:
                    case ShotAction.TrashOthers:
                    case ShotAction.TrashAll:
                        {
                            // 批量删除与批量放进篮子选的是同一批图，区别只在收尾：这里进回收站。
                            // 逐张删并在中间留出间隔：连续调用回收站删除时 shell 会忙不过来抛
                            // 「操作已取消」，不留间隔的话删掉一两张就整个中断，看起来像没生效。
                            var onRope = Shelf.HangingItems();
                            int at = Array.IndexOf(onRope, item);
                            if (at < 0) break;
                            var doomed = onRope.Where((image, position) => action == ShotAction.TrashAll
                                || action == ShotAction.TrashLeft && position < at
                                || action == ShotAction.TrashRight && position > at
                                || action == ShotAction.TrashOthers && position != at).ToArray();
                            int done = 0, failed = 0;
                            foreach (var target in doomed)
                            {
                                if (!File.Exists(target.Path)) continue;
                                if (TrashOne(target)) { done++; await Task.Delay(60); continue; }
                                // 刚忙完上一张，等一下再试一次；仍然失败就跳过，不中断整批。
                                await Task.Delay(200);
                                if (TrashOne(target)) done++; else failed++;
                                await Task.Delay(60);
                            }
                            Gallery.Prune();
                            if (failed > 0) Notice("已把 " + done + " 张送进回收站，" + failed + " 张没能删除", 5);
                            // 成功提示只停 2 秒：结果一眼能确认，不必赖在屏幕角落。
                            else if (done > 0) Notice("已把 " + done + " 张送进回收站");
                            break;
                        }
                    case ShotAction.Copy:
                        var payload = new DataObject();
                        byte[] png;
                        using (var stream = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            var image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                            payload.SetImage(image);
                            png = ImageContent.EncodeClipboard(image);
                        }
                        payload.SetData("PNG", new MemoryStream(png));
                        payload.SetFileDropList(new StringCollection { item.Path });
                        payload.SetData(ClipboardRules.OwnFormat, "1");
                        await WindowsClipboard.Write(payload);
                        Shelf.ShowCopied(item.Path);
                        break;
                    case ShotAction.Open:
                        Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
                        Shelf.FollowImageViewer(item.Path);
                        break;
                    case ShotAction.Edit:
                        var existing = Editors.Find(editor => FileRules.SameFile(editor.ImagePath, item.Path));
                        if (existing != null)
                        {
                            existing.Present();
                            break;
                        }
                        if (!FileRules.IsInside(item.Path, Gallery.Store.Inbox))
                        {
                            string copy = await Task.Run(() => Captures.CopyToInbox(item.Path));
                            try
                            {
                                var replacement = Gallery.Replace(item, copy);
                                if (replacement == null) { File.Delete(copy); break; }
                                item = replacement;
                            }
                            catch { File.Delete(copy); throw; }
                        }
                        var window = new ImageEditorWindow(item.Path, Gallery.Store, RefreshEditedImage, Captures.IgnoreCurrentClipboard);
                        Editors.Add(window);
                        window.Closed += delegate
                        {
                            Editors.Remove(window);
                            if (!window.ReturnBounds.IsEmpty) Shelf.ShowEditedImage(window.ImagePath, window.ReturnBounds, window.ReturnPicture);
                        };
                        Shelf.Reveal(false, false, 0);
                        window.Present();
                        break;
                    case ShotAction.Save:
                        var dialog = new SaveFileDialog { Title = "保存副本", FileName = Path.GetFileName(item.Path), Filter = "图片|*" + Path.GetExtension(item.Path), OverwritePrompt = true };
                        if (dialog.ShowDialog() == true && !FileRules.SameFile(item.Path, dialog.FileName)) File.Copy(item.Path, dialog.FileName, true);
                        break;
                    case ShotAction.Locate:
                        Process.Start("explorer.exe", "/select," + Quote(item.Path));
                        break;
                    case ShotAction.Remove:
                        Shelf.ShowCollect(item);
                        Gallery.Remove(item);
                        break;
                    case ShotAction.Trash:
                        // 单张失败也要说一声，否则文件还在、记录却不在绳上会让人困惑。
                        if (!TrashOne(item)) Notice("这一张没能送进回收站", 4);
                        break;
                }
            }
            catch (Exception error) { Gallery.Store.Log(error); Notice("操作未完成：" + error.Message); }
        }

        // 单张删除与批量删除共用：收集副本进回收站，外部原图只移除记录、文件留在原处。
        // 返回是否成功；失败只记日志、不往外抛，批量删除靠它决定要不要继续下一张。
        bool TrashOne(CaptureItem target)
        {
            try
            {
                if (FileRules.IsInside(target.Path, Gallery.Store.Inbox))
                {
                    FileRules.AssertPlainTree(target.Path, Gallery.Store.Root);
                    FileSystem.DeleteFile(target.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    Shelf.ShowDiscard(target);
                    Gallery.Prune();
                }
                else { Shelf.ShowDiscard(target); Gallery.Forget(target); }
                return true;
            }
            catch (Exception error) { Gallery.Store.Log(error); return false; }
        }

        public void Dispose()
        {
            disposed = true; updateTimer.Stop(); purgeTimer.Stop(); updateLifetime.Cancel();
            if (Capture != null) Capture.Close();
            if (ShortcutSettings != null) ShortcutSettings.Close();
            if (updateWindow != null) updateWindow.Close();
            if (uninstallWindow != null) uninstallWindow.Close();
            updates.Dispose();
            if (Basket != null) Basket.Close();
            if (TextWindow != null) TextWindow.Close();
            if (Appearance != null) Appearance.Close();
            if (Settings != null) Settings.Close();
            if (notice != null) notice.Close();
            foreach (var editor in Editors.ToArray()) editor.CloseWithoutPrompt();
            TrayMenu.Close();
            Gallery.Changed -= CheckCacheLimit;
            Texts.Changed -= Shelf.UpdateTexts;
            Captures.Dispose();
            events.Dispose();
            tray.Visible = false;
            var icon = tray.Icon;
            tray.Dispose();
            icon.Dispose();
            Shelf.Close();
        }

        async Task RefreshEditedImage(string path)
        {
            ImageContent image = await Task.Run(() => ImageContent.Read(path));
            var item = Gallery.Items.Find(candidate => FileRules.SameFile(candidate.Path, path));
            if (item == null) Gallery.Add(path, image);
            else
            {
                item.Image = image;
                item.Written = File.GetLastWriteTimeUtc(path);
                Gallery.Notify();
            }
        }
    }

    internal static class Program
    {
        static Mutex instanceMutex;

        // 切换深浅色后重启自身：先放掉单实例锁，再拉起新进程，否则新进程会以为已有实例在跑。
        internal static void Restart()
        {
            string exe = Assembly.GetEntryAssembly().Location;
            if (!File.Exists(exe)) throw new FileNotFoundException("找不到程序文件。", exe);
            // Exit 的控制器清理先执行，释放热键与窗口后再启动，避免新进程把自己的快捷键误判为占用。
            Application.Current.Exit += delegate
            {
                if (instanceMutex != null)
                {
                    try { instanceMutex.ReleaseMutex(); } catch (ApplicationException) { }
                    instanceMutex.Dispose(); instanceMutex = null;
                }
                try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) }); }
                catch (Exception error) { MessageBox.Show("配色已保存，请手动重新启动 PickSnap。\n" + error.Message, "PickSnap"); }
            };
            Application.Current.Shutdown();
        }

        [STAThread]
        public static void Main(string[] arguments)
        {
            if (Array.IndexOf(arguments, "--quit") >= 0) { WindowsNative.RequestExit(); return; }
            bool uninstallRequested = Array.IndexOf(arguments, "--uninstall") >= 0;
            bool first;
            using (var instance = new Mutex(true, @"Local\PickSnap.Windows", out first))
            {
                if (!first) { if (Array.IndexOf(arguments, "--uninstall") >= 0) WindowsNative.ShowUninstall(); else WindowsNative.ShowExisting(Array.IndexOf(arguments, "--settings") >= 0); return; }
                instanceMutex = instance;
                if (!uninstallRequested) StateStore.AdoptStagedData();
                var store = new StateStore(StateStore.DefaultRoot);
                // 首次启动（没有旧配置）自动在桌面放一个快捷方式，之后不再重复；删掉的用户不会被强行加回。
                if (store.FreshInstall && !uninstallRequested)
                {
                    try { DesktopController.CreateDesktopShortcut(); }
                    catch (Exception error) { store.Log(error); }
                }
                // 配色在建立任何界面对象之前定下来，之后所有窗口都按同一套取值。
                LightTheme.UseDark(LightTheme.Resolve(store.State.AppTheme));
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                LightTheme.Install(app);
                DesktopController controller = null;
                app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args) { store.Log(args.Exception); args.Handled = true; };
                app.Startup += async delegate
                {
                    try
                    {
                        controller = new DesktopController(store, WindowsNative.ScreenshotFolder());
                        if (uninstallRequested)
                        {
                            // 独立卸载入口只展示确认页，不启动收集、自动清理或更新检查。
                            controller.ShowSettings("更新与卸载"); controller.Uninstall(); app.Shutdown(); return;
                        }
                        await controller.Start();
                        if (Array.IndexOf(arguments, "--settings") >= 0) controller.ShowSettings();
                    }
                    catch (Exception error)
                    {
                        store.Log(error); MessageBox.Show("PickSnap 启动未完成：" + error.Message + "\n请保留 data 文件夹并重新运行完整安装包。", "PickSnap", MessageBoxButton.OK, MessageBoxImage.Error); app.Shutdown(1);
                    }
                };
                app.Exit += delegate { if (controller != null) controller.Dispose(); };
                app.Run();
            }
        }
    }
}
