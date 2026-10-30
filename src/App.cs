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

[assembly: AssemblyTitle("PickSnap · 拾帧")]
[assembly: AssemblyProduct("PickSnap")]
[assembly: AssemblyDescription("拾帧：把截图挂在屏幕上沿，随手复制、拖拽和保存。")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

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
        readonly CheckBox collect;
        readonly CheckBox collectText;
        readonly CheckBox autoHang;
        internal readonly TrayPanel TrayMenu;
        internal AppearanceWindow Appearance { get; private set; }
        internal BasketWindow Basket { get; private set; }
        internal TextWindow TextWindow { get; private set; }
        internal CaptureWindow Capture { get; private set; }
        internal ShortcutWindow ShortcutSettings { get; private set; }
        readonly MenuItem shortcutItem;
        internal readonly List<ImageEditorWindow> Editors = new List<ImageEditorWindow>();
        readonly AppUpdates updates = new AppUpdates();
        readonly CancellationTokenSource updateLifetime = new CancellationTokenSource();
        const int BasketLimit = 100;
        int lastCollectedCount;
        readonly DispatcherTimer updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        readonly MenuItem updateMenu;
        readonly bool automaticUpdates;
        UpdateRelease availableUpdate;
        UpdateWindow updateWindow;
        bool checkingUpdates, disposed;
        uint textClipboardSequence;
        int textClipboardVersion;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // 在桌面创建「PickSnap」快捷方式：首次启动自动执行一次，托盘「更多操作」里也可随时重建。
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

        public DesktopController(StateStore store, string screenshots, bool automaticUpdates = true)
        {
            this.automaticUpdates = automaticUpdates;
            Gallery = new Gallery(store);
            Texts = new TextCollection(store);
            Captures = new CapturePipeline(Gallery, Application.Current.Dispatcher, screenshots);
            Shelf = new ShelfWindow(Gallery, Act, EnableClipboard);
            Shelf.BasketRequested += ShowBasket;
            Shelf.TextRequested += ShowTexts;
            Texts.Changed += Shelf.UpdateTexts;
            events = new WindowsEvents();
            events.RegisterCapture(store.State.CaptureModifiers, store.State.CaptureKey);
            events.CaptureRequested += ShowCapture;
            events.ToggleRequested += Shelf.Toggle;
            events.ExitRequested += Exit;
            events.ClipboardChanged += Captures.ClipboardChanged;
            textClipboardSequence = WindowsNative.GetClipboardSequenceNumber();
            events.ClipboardChanged += CollectClipboardText;
            Captures.Arrived += Shelf.ShowNewCapture;
            Captures.Failed += Notice;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PickSnap.Icon"))
                tray.Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            tray.Text = "PickSnap · 拾帧";
            TrayMenu = new TrayPanel();
            SyncScreens();
            TrayMenu.Opened += delegate { SyncScreens(); };
            TrayMenu.ScreenMenu.Opened += delegate { SyncScreens(); };
            TrayMenu.OnAction(TrayMenu.VisibilityButton, delegate { Shelf.Toggle(); });
            if (events.ShortcutAvailable) TrayMenu.SetShortcutTip("Ctrl+Alt+T");
            else Notice("Ctrl+Alt+T 已被占用，可点击托盘图标打开。");
            TrayMenu.OnAction(TrayMenu.BasketButton, ShowBasket);
            TrayMenu.OnAction(TrayMenu.TextButton, ShowTexts);
            TrayMenu.OnAction(TrayMenu.CaptureButton, ShowCapture);
            autoHang = TrayMenu.AutoHang;
            TrayMenu.OnToggle(autoHang, Captures.AutoHang, delegate(bool value) { if (Captures.AutoHang != value) Captures.AutoHang = value; });
            Gallery.Changed += SyncAutoHang;
            Gallery.Changed += CheckCacheLimit;
            lastCollectedCount = Gallery.Collected.Count;
            collect = TrayMenu.Pictures;
            TrayMenu.OnToggle(collect, Captures.CollectClipboard, delegate(bool value) { Captures.CollectClipboard = value; Shelf.Update(); });
            collectText = TrayMenu.Text;
            TrayMenu.OnToggle(collectText, Texts.CollectClipboard, delegate(bool value)
            {
                textClipboardVersion++;
                textClipboardSequence = WindowsNative.GetClipboardSequenceNumber();
                Texts.CollectClipboard = value;
            });
            TrayMenu.OnAction(TrayMenu.AppearanceButton, ShowAppearance);
            var more = TrayMenu.MoreMenu;            MenuItem previewToggle = null;
            previewToggle = more.AddAction("悬停放大预览", "picture", delegate
            {
                bool previous = store.State.HoverPreview;
                store.State.HoverPreview = previewToggle.IsChecked;
                try { store.Save(); Shelf.Preview.SetEnabled(store.State.HoverPreview); }
                catch (Exception error) { store.State.HoverPreview = previous; previewToggle.IsChecked = previous; store.Log(error); Notice("预览设置保存失败"); }
            });
            previewToggle.IsCheckable = true;
            previewToggle.IsChecked = store.State.HoverPreview;
            previewToggle.ToolTip = "开启后，鼠标停在绳子上的图片会浮出一块放大预览";
            shortcutItem = more.AddAction("截图快捷键…", "shortcut", delegate
            {
                if (ShortcutSettings != null) { ShortcutSettings.Activate(); return; }
                ShortcutSettings = new ShortcutWindow(store.State.CaptureModifiers, store.State.CaptureKey, SetCaptureShortcut);
                ShortcutSettings.Closed += delegate { ShortcutSettings = null; }; ShortcutSettings.Show();
            });
            UpdateCaptureLabel();
            more.Separator();
            more.AddAction("添加图片…", "add", async delegate
            {
                var picker = new OpenFileDialog { Title = "添加图片", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff", Multiselect = true };
                if (picker.ShowDialog() != true) return;
                foreach (string file in picker.FileNames)
                    if (!await Captures.Import(file, null, true)) Notice("无法读取图片：" + Path.GetFileName(file));
            });
            more.AddAction("打开收集文件夹", "folder", delegate
            {
                try { WindowsShell.OpenInbox(store); }
                catch (Exception error) { store.Log(error); Notice("无法打开收集文件夹：" + error.Message); }
            });
            more.AddAction("全部取下（保留文件）", "clear", delegate { Gallery.Clear(); });
            more.Separator();
            MenuItem startup = null;
            startup = more.AddAction("开机启动", "startup", delegate
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (startup.IsChecked) key.SetValue("PickSnap", Quote(Assembly.GetEntryAssembly().Location));
                    else key.DeleteValue("PickSnap", false);
                }
            });
            startup.IsCheckable = true;
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) startup.IsChecked = key != null && key.GetValue("PickSnap") != null;
            more.AddAction("在桌面创建快捷方式", "show", delegate
            {
                try { Notice(DesktopController.CreateDesktopShortcut() ? "已在桌面创建「PickSnap」快捷方式" : "快捷方式创建失败，请检查桌面目录权限"); }
                catch (Exception error) { store.Log(error); Notice("快捷方式创建失败：" + error.Message); }
            });
            updateMenu = more.AddAction("检查更新…", "save", async delegate { await CheckForUpdates(true); });
            updateTimer.Tick += async delegate { updateTimer.Interval = TimeSpan.FromHours(12); await CheckForUpdates(false); };
            tray.BalloonTipClicked += delegate { ShowUpdate(); };
            TrayMenu.OnAction(TrayMenu.ExitButton, Exit);
            tray.MouseUp += delegate(object sender, Forms.MouseEventArgs args) { if (args.Button == Forms.MouseButtons.Right) ShowTrayMenu(); };
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs args) { if (args.Button == Forms.MouseButtons.Left) Shelf.Toggle(); };
            tray.Visible = true;
        }

        async void Exit()
        {
            foreach (var editor in Editors.ToArray()) if (!await editor.ConfirmClose()) return;
            Application.Current.Shutdown();
        }

        public async Task Start()
        {
            Shelf.Show();
            Shelf.Reveal(true, false, 4);
            await Captures.Start();
            if (automaticUpdates) updateTimer.Start();
        }

        void UpdateCaptureLabel()
        {
            // 快捷键显示在「更多操作 › 截图快捷键…」行尾；不放快捷格 ToolTip——
            // 托盘面板是穿透/捕获频繁的窗口，ToolTip 一旦弹出常因收不到移开事件而卡住不消失。
            if (shortcutItem != null)
                shortcutItem.InputGestureText = events.CaptureShortcutAvailable ? ShortcutWindow.Label(Gallery.Store.State.CaptureModifiers, Gallery.Store.State.CaptureKey) : "快捷键被占用";
        }

        internal bool SetCaptureShortcut(uint modifiers, uint key)
        {
            if (!events.RegisterCapture(modifiers, key)) return false;
            uint previousModifiers = Gallery.Store.State.CaptureModifiers, previousKey = Gallery.Store.State.CaptureKey;
            Gallery.Store.State.CaptureModifiers = modifiers; Gallery.Store.State.CaptureKey = key;
            try { Gallery.Store.Save(); }
            catch (Exception error)
            {
                Gallery.Store.State.CaptureModifiers = previousModifiers; Gallery.Store.State.CaptureKey = previousKey;
                events.RegisterCapture(previousModifiers, previousKey); Gallery.Store.Log(error); Notice("无法保存快捷键，请重试。"); return false;
            }
            UpdateCaptureLabel(); return true;
        }

        internal async void ShowCapture()
        {
            if (disposed || Shelf.Capturing) return;
            Shelf.Capturing = true;
            var display = WindowsNative.ScreenAtPointer();
            bool revealed = Shelf.Revealed;
            try
            {
                TrayMenu.Close(); if (Basket != null) Basket.Close(); if (TextWindow != null) TextWindow.Close();
                if (Appearance != null) Appearance.Close(); if (ShortcutSettings != null) ShortcutSettings.Close();
                Shelf.Reveal(false, false, 0); await Task.Delay(260);
                if (disposed) return;
                Capture = new CaptureWindow(display);
                var result = await Capture.PickAsync(); Capture = null;
                if (disposed) return;
                if (result != null) await Captures.AcceptCapture(result);
                else if (revealed) Shelf.Reveal(true, false, 2);
            }
            catch (Exception error) { if (!disposed) { Gallery.Store.Log(error); Notice("截图未完成，请重试。"); } }
            finally { if (Capture != null) Capture.Close(); Capture = null; Shelf.Capturing = false; }
        }

        async Task CheckForUpdates(bool manual)
        {
            if (disposed || checkingUpdates) return;
            if (manual && availableUpdate != null) { ShowUpdate(); return; }
            checkingUpdates = true; updateMenu.IsEnabled = false; updateMenu.Header = "正在检查更新…";
            try
            {
                availableUpdate = await updates.CheckAsync(updateLifetime.Token);
                if (disposed) return;
                if (availableUpdate == null) { if (manual) Notice("你当前已是最新版本", 2, true); }
                else if (manual) ShowUpdate();
                else if (availableUpdate.Version != Gallery.Store.State.SkippedUpdate && Gallery.Store.State.LastUpdateNotification != availableUpdate.Version)
                {
                    // 自动后台检查只在新版本时出气泡，跳过的版本与已提醒过的保持静默。
                    tray.ShowBalloonTip(10000, "拾帧有新版本 " + availableUpdate.Version, "点击查看更新并下载安装。", Forms.ToolTipIcon.Info);
                    Gallery.Store.State.LastUpdateNotification = availableUpdate.Version; Gallery.Store.Save();
                }
            }
            catch (Exception)
            {
                // 统一归为「连接失败」，不给用户技术术语；网络恢复后再试即可。
                if (!disposed && manual) Notice("暂时无法连接 GitHub 检查更新，请稍后重试。", 2, true);
            }
            finally
            {
                checkingUpdates = false; updateMenu.IsEnabled = true;
                updateMenu.Header = availableUpdate == null ? "检查更新…" : "更新到 " + availableUpdate.Version + "…";
            }
        }

        void ShowUpdate()
        {
            if (disposed || availableUpdate == null) return;
            if (updateWindow != null) { updateWindow.Activate(); return; }
            updateWindow = new UpdateWindow(availableUpdate, updates, async delegate(string work)
            {
                foreach (var editor in Editors.ToArray()) if (!await editor.ConfirmClose()) return false;
                AppUpdates.Install(work);
                Application.Current.Shutdown();
                return true;
            }, delegate
            {
                // 跳过此版本：记录版本号，自动检查不再提醒它，直到下一个新版本；手动检查仍然可见。
                Gallery.Store.State.SkippedUpdate = availableUpdate.Version;
                Gallery.Store.Save();
                availableUpdate = null;
                updateMenu.Header = "检查更新…";
            });
            updateWindow.Closed += delegate { updateWindow = null; };
            updateWindow.Show();
        }

        void EnableClipboard()
        {
            Captures.CollectClipboard = true;
            collect.IsChecked = true;
        }

        void SyncAutoHang() { autoHang.IsChecked = Captures.AutoHang; }

        // 图片篮子软上限（100 张）：只提醒，不删除任何文件。
        // 只在「图片数量变多」时（即又收到一张新图）提示一次；删除、挂绳等操作波动不打扰。
        void CheckCacheLimit()
        {
            int count = Gallery.Collected.Count;
            bool grew = count > lastCollectedCount;
            lastCollectedCount = count;
            if (!grew || count <= BasketLimit) return;
            Notice("图片篮子已超过 " + BasketLimit + " 张，请打开图片篮子清理不需要的截图。");
        }

        void SyncScreens()
        {
            TrayMenu.SetScreens(WindowsNative.Screens, Gallery.Store.State.DisplayDevice, SelectScreen);
        }

        void SelectScreen(string device)
        {
            TrayMenu.Close();
            try { Shelf.SetDisplay(device); }
            catch (Exception error) { Gallery.Store.Log(error); Notice("无法保存屏幕设置：" + error.Message); }
            SyncScreens();
        }

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
            Basket = new BasketWindow(Gallery, delegate(bool value) { Captures.AutoHang = value; }, Captures.Hang, Act, Shelf.IsPinned, Shelf.RoomForHanging, () => Shelf.RopeCapacity);
            Basket.Owner = Shelf;
            Shelf.ChoosingBasket = true;
            Shelf.Reveal(true, false, 0);
            Basket.Closed += delegate { Basket = null; Shelf.ChoosingBasket = false; };
            Basket.Show();
        }

        internal void ShowTexts()
        {
            if (TextWindow != null) { TextWindow.Close(); return; }
            Shelf.Reveal(true, false, 0);
            Shelf.ChoosingTexts = true;
            TextWindow = new TextWindow(Texts, Shelf);
            TextWindow.Closed += delegate { TextWindow = null; Shelf.ChoosingTexts = false; };
            TextWindow.Show();
        }

        void ShowAppearance()
        {
            if (Appearance != null) { Appearance.Activate(); return; }
            Appearance = new AppearanceWindow(Shelf.Appearance, Gallery, delegate(LineTheme theme)
            {
                Gallery.Store.State.Theme = theme.Id;
                Gallery.Store.Save();
                Shelf.ApplyTheme(theme);
                Shelf.Reveal(true, false, 3);
            }, Gallery.Store.State.AppTheme, delegate(string mode)
            {
                if (Gallery.Store.State.AppTheme == mode) return;
                Gallery.Store.State.AppTheme = mode;
                Gallery.Store.Save();
                // 配色牵涉到所有已建窗口的画刷，重启一次比逐个重刷更稳，用户几乎无感。
                DispatcherTimer restart = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                restart.Tick += delegate { restart.Stop(); Program.Restart(); };
                restart.Start();
            });
            Appearance.Owner = Shelf;
            Shelf.ChoosingAppearance = true;
            Shelf.Reveal(true, false, 0);
            Appearance.Closed += delegate { Appearance = null; Shelf.ChoosingAppearance = false; };
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
                        if (FileRules.IsInside(item.Path, Gallery.Store.Inbox))
                        {
                            FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                            Shelf.ShowDiscard(item);
                            Gallery.Prune();
                        }
                        else { Shelf.ShowDiscard(item); Gallery.Forget(item); }
                        break;
                }
            }
            catch (Exception error) { Gallery.Store.Log(error); Notice("操作未完成：" + error.Message); }
        }

        public void Dispose()
        {
            disposed = true; updateTimer.Stop(); updateLifetime.Cancel();
            if (Capture != null) Capture.Close();
            if (ShortcutSettings != null) ShortcutSettings.Close();
            if (updateWindow != null) updateWindow.Close();
            updates.Dispose();
            if (Basket != null) Basket.Close();
            if (TextWindow != null) TextWindow.Close();
            if (Appearance != null) Appearance.Close();
            if (notice != null) notice.Close();
            foreach (var editor in Editors.ToArray()) editor.CloseWithoutPrompt();
            TrayMenu.Close();
            Gallery.Changed -= SyncAutoHang;
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
            try
            {
                if (instanceMutex != null)
                {
                    try { instanceMutex.ReleaseMutex(); }
                    catch { }
                    instanceMutex.Dispose();
                    instanceMutex = null;
                }
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
            }
            catch { }
            Application.Current.Shutdown();
        }

        [STAThread]
        public static void Main(string[] arguments)
        {
            if (Array.IndexOf(arguments, "--quit") >= 0) { WindowsNative.RequestExit(); return; }
            bool first;
            using (var instance = new Mutex(true, @"Local\TangHao.ScreenLine.Windows", out first))
            {
                if (!first) { WindowsNative.ShowExisting(); return; }
                instanceMutex = instance;
                var store = new StateStore(StateStore.DefaultRoot);
                // 首次启动（没有旧配置）自动在桌面放一个快捷方式，之后不再重复；删掉的用户不会被强行加回。
                if (store.FreshInstall)
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
                app.Startup += async delegate { controller = new DesktopController(store, WindowsNative.ScreenshotFolder()); await controller.Start(); };
                app.Exit += delegate { if (controller != null) controller.Dispose(); };
                app.Run();
            }
        }
    }
}
