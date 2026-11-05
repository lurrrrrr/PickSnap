using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenLine
{
    internal sealed class SettingsWindow : Window
    {
        readonly DesktopController controller;
        internal readonly ListBox Navigation = new ListBox { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, Padding = new Thickness(0) };
        readonly StackPanel page = new StackPanel();
        readonly System.Collections.Generic.List<ComboBox> choices = new System.Collections.Generic.List<ComboBox>();
        readonly ScrollViewer pageScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        readonly TextBlock feedback = new TextBlock { Text = "", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = LightTheme.Brush("#65717F") };
        internal string CurrentPage { get; private set; }
        internal static readonly string[] Pages = { "常规与外观", "键盘快捷键", "收集与清理", "更新与卸载" };
        const double ActionWidth = 164;
        internal TextBlock UpdateStatusText { get; private set; }
        internal Button CheckUpdateButton { get; private set; }
        internal Button ViewUpdateButton { get; private set; }
        TextBlock updateTime;
        ProgressBar updateProgress;

        internal SettingsWindow(DesktopController controller)
        {
            this.controller = controller;
            Title = "设置 · PickSnap"; FontFamily = LightTheme.UiFont; FontSize = 13;
            Background = LightTheme.Brush("#F5F7FA"); Foreground = LightTheme.Brush("#283343");
            var screen = WindowsNative.ScreenAtPointer(); double dpi = WindowsNative.MonitorScale(screen);
            Width = Math.Min(900, screen.WorkingArea.Width / dpi - 32); Height = Math.Min(700, screen.WorkingArea.Height / dpi - 32);
            MinWidth = Math.Min(760, Width); MinHeight = Math.Min(420, Height);
            WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
            LightTheme.Polish(this); System.Windows.Automation.AutomationProperties.SetAutomationId(this, "PickSnapSettings");
            var root = new Grid(); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); body.ColumnDefinitions.Add(new ColumnDefinition());
            var navigation = new StackPanel { Margin = new Thickness(10, 24, 10, 16) };
            navigation.Children.Add(new TextBlock { Text = "PickSnap", FontSize = 19, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 26) });
            Navigation.ItemContainerStyle = LightTheme.NavigationItemStyle;
            System.Windows.Media.TextOptions.SetTextRenderingMode(Navigation, System.Windows.Media.TextRenderingMode.Grayscale);
            string[] labels = Pages;
            for (int i = 0; i < Pages.Length; i++)
            {
                var item = new Grid();
                var letters = new Grid { Width = 96, HorizontalAlignment = HorizontalAlignment.Center };
                for (int c = 0; c < labels[i].Length; c++)
                {
                    if (c > 0) letters.ColumnDefinitions.Add(new ColumnDefinition());
                    letters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var letter = new TextBlock { Text = labels[i][c].ToString(), FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei"), FontWeight = FontWeights.Normal, FontSize = 16 }; letter.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) }); Grid.SetColumn(letter, c * 2); letters.Children.Add(letter);
                }
                item.Children.Add(letters); var entry = new ListBoxItem { Content = item }; System.Windows.Automation.AutomationProperties.SetName(entry, Pages[i]); Navigation.Items.Add(entry);
            }
            Navigation.SelectionChanged += delegate { if (Navigation.SelectedIndex >= 0) { CurrentPage = Pages[Navigation.SelectedIndex]; RefreshPage(); } };
            System.Windows.Automation.AutomationProperties.SetName(Navigation, "设置分类");
            navigation.Children.Add(Navigation);
            var rail = new Border { Background = LightTheme.Brush("#FFFFFF"), BorderBrush = LightTheme.Brush("#E8EBEF"), BorderThickness = new Thickness(0, 0, 1, 0), Child = navigation }; body.Children.Add(rail);
            page.Margin = new Thickness(28, 24, 28, 24);
            pageScroll.Content = page;
            Grid.SetColumn(pageScroll, 1); body.Children.Add(pageScroll); root.Children.Add(body);
            feedback.Margin = new Thickness(16, 0, 0, 0); feedback.TextWrapping = TextWrapping.Wrap; feedback.TextAlignment = TextAlignment.Right;
            Content = root; SelectPage(Pages[0]);
            Loaded += delegate { Dispatcher.BeginInvoke(new Action(pageScroll.ScrollToTop), System.Windows.Threading.DispatcherPriority.ContextIdle); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Escape) return;
                var open = choices.FirstOrDefault(box => box.IsDropDownOpen);
                if (open != null) open.IsDropDownOpen = false; else Close();
                e.Handled = true;
            };
        }
        internal void SelectPage(string name) { int index = Array.IndexOf(Pages, name == "快捷键" ? "键盘快捷键" : name); Navigation.SelectedIndex = index < 0 ? 0 : index; }
        internal void RefreshPage()
        {
            CheckUpdateButton = null; ViewUpdateButton = null; UpdateStatusText = null;
            var oldHeader = feedback.Parent as Panel; if (oldHeader != null) oldHeader.Children.Remove(feedback);
            page.Children.Clear(); choices.Clear(); var state = controller.Gallery.Store.State;
            feedback.Text = ""; feedback.Visibility = Visibility.Collapsed;
            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition());
            header.Children.Add(new TextBlock { Text = CurrentPage, FontSize = 24, FontWeight = FontWeights.SemiBold }); Grid.SetColumn(feedback, 1); header.Children.Add(feedback); page.Children.Add(header);
            if (CurrentPage == Pages[0])
            {
                Description("让绳子按你的习惯出现。");
                Switch("鼠标移到顶部时展开", "关闭后仍可通过快捷键或托盘图标显示。", () => !state.NoHoverReveal, v => controller.SetPreference(() => state.NoHoverReveal, x => state.NoHoverReveal = x, !v));
                Choice("顶部触发范围", "只在所选区域响应，减少误展开。", new[] { "屏幕中段 · 30%～70%", "偏左区域 · 8%～40%", "整个顶边 · 0%～100%" }, state.RevealZone == "full" ? 2 : state.RevealZone == "left" ? 1 : 0, i => controller.SetPreference(() => state.RevealZone, v => state.RevealZone = v, i == 2 ? "full" : i == 1 ? "left" : "center"));
                Choice("停留时间", "停留越久，越不容易误展开。", new[] { "快速 · 250 毫秒", "适中 · 450 毫秒", "从容 · 750 毫秒", "防误触 · 1 秒" }, state.RevealDelayMs <= 250 ? 0 : state.RevealDelayMs <= 450 ? 1 : state.RevealDelayMs <= 750 ? 2 : 3, i => controller.SetPreference(() => state.RevealDelayMs, v => state.RevealDelayMs = v, new[] { 250, 450, 750, 1000 }[i]));
                Switch("悬停放大预览", "停在图片上时显示大图。", () => state.HoverPreview, v => controller.SetPreference(() => state.HoverPreview, x => state.HoverPreview = x, v));
                var screens = WindowsNative.Screens;
                int selected = Array.FindIndex(screens, s => s.DeviceName == state.DisplayDevice);
                var options = new[] { "跟随鼠标" }.Concat(screens.Select((s, i) => "屏幕 " + (i + 1) + (s.Primary ? " · 主屏" : ""))).ToArray();
                bool disconnected = !string.IsNullOrEmpty(state.DisplayDevice) && selected < 0;
                if (disconnected) { options = options.Concat(new[] { "原屏幕（未连接）" }).ToArray(); selected = options.Length - 2; }
                string detail = disconnected ? "原屏幕未连接，暂用主屏；重连后恢复。" : "跟随鼠标，或始终显示在固定屏幕。";
                Choice("显示屏幕", detail, options, selected + 1, i => { if (i <= screens.Length) controller.SelectScreen(i == 0 ? null : screens[i - 1].DeviceName); });
                Switch("开机启动", "登录 Windows 后启动 PickSnap。", () => controller.StartupEnabled, controller.SetStartup);
                ActionRow("绳子主题与配色", "五套绳子主题，支持浅色、深色和跟随系统。", "调整外观", controller.ShowAppearance);
            }
            else if (CurrentPage == Pages[1])
            {
                Description("点击修改，按下你想使用的组合键。");
                ActionRow("显示 / 收起绳子", ShortcutWindow.Label(state.ToggleModifiers, state.ToggleKey) + (controller.ToggleShortcutAvailable ? " · 已生效" : " · 未生效，请更换组合键"), "修改快捷键", () => controller.EditShortcut(true));
                ActionRow("区域截图", ShortcutWindow.Label(state.CaptureModifiers, state.CaptureKey) + (controller.CaptureShortcutAvailable ? " · 已生效" : " · 未生效，请更换组合键"), "修改快捷键", () => controller.EditShortcut(false));
            }
            else if (CurrentPage == Pages[2])
            {
                Description("选择要收集的内容，也可以随时从托盘开关。");
                Switch("收集图片", "系统截图和复制的图片会自动挂上绳子。", () => controller.Captures.CollectClipboard, controller.SetClipboardImages);
                Switch("收集文字", "保留最近 100 条复制的文字。", () => controller.Texts.CollectClipboard, controller.SetClipboardText);
                Switch("自动清理 7 天前的旧图", "超过 7 天未修改的图片会移入回收站。", () => state.AutoPurge, controller.SetAutoPurge);
                ActionRow("收集文件夹", controller.Gallery.Store.Inbox, "打开文件夹", () => WindowsShell.OpenInbox(controller.Gallery.Store));
            }
            else
            {
                Description("PickSnap 的版本与应用管理。");
                BuildUpdateCard();
                ActionRow("版本说明与手动下载", "在 GitHub 查看正式版本与更新说明。", "打开发布页", () => Process.Start(new ProcessStartInfo(AppUpdates.Repository + "/releases/latest") { UseShellExecute = true }));
                ActionRow("桌面快捷方式", "在桌面添加 PickSnap 图标。", "创建快捷方式", controller.RebuildShortcut, null, "桌面快捷方式已创建");
                ActionRow("卸载 PickSnap", "移除程序，可选择保留图片和设置。", "卸载选项", controller.Uninstall, "danger");
            }
            pageScroll.ScrollToTop();
        }
        void BuildUpdateCard()
        {
            var body = new StackPanel();
            var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(new TextBlock { Text = "PickSnap  v" + AppUpdates.CurrentVersion, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            CheckUpdateButton = LightTheme.Button("检查更新", delegate { }); CheckUpdateButton.Width = ActionWidth;
            CheckUpdateButton.Click += async delegate { await controller.CheckForUpdates(true); };
            Grid.SetColumn(CheckUpdateButton, 1); heading.Children.Add(CheckUpdateButton); body.Children.Add(heading);
            UpdateStatusText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 21, Margin = new Thickness(0, 14, 0, 6) }; body.Children.Add(UpdateStatusText);
            updateTime = new TextBlock { Foreground = LightTheme.Brush("#65717F"), FontSize = 12 }; body.Children.Add(updateTime);
            updateProgress = new ProgressBar { Height = 3, IsIndeterminate = true, Foreground = LightTheme.Brush("#2463AE"), Margin = new Thickness(0, 12, 0, 0) }; body.Children.Add(updateProgress);
            ViewUpdateButton = LightTheme.Button("查看更新说明 ›", controller.ShowUpdate); ViewUpdateButton.Style = LightTheme.LinkStyle; ViewUpdateButton.Width = ActionWidth; ViewUpdateButton.HorizontalAlignment = HorizontalAlignment.Right; ViewUpdateButton.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(ViewUpdateButton);
            page.Children.Add(new Border { Background = LightTheme.Brush("#FFFFFF"), BorderBrush = LightTheme.Brush("#E0E4EA"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12), Child = body });
            RefreshUpdateStatus();
        }
        internal void RefreshUpdateStatus()
        {
            if (CheckUpdateButton == null || UpdateStatusText == null) return;
            var result = controller.LastUpdateCheck; bool checking = result.Phase == UpdateCheckPhase.Checking;
            CheckUpdateButton.IsEnabled = !checking; CheckUpdateButton.Content = checking ? "检查中…" : result.Phase == UpdateCheckPhase.Failed ? "重新检查" : "检查更新";
            System.Windows.Automation.AutomationProperties.SetName(CheckUpdateButton, checking ? "正在检查更新" : "检查更新");
            UpdateStatusText.Text = result.Message;
            UpdateStatusText.Foreground = LightTheme.Brush(result.Phase == UpdateCheckPhase.Failed ? "#B74E4A" : result.Phase == UpdateCheckPhase.Current ? "#2E7D46" : "#343D49");
            updateTime.Text = result.CheckedAt == default(DateTime) ? "" : "上次检查 " + result.CheckedAt.ToString("MM-dd HH:mm");
            updateProgress.Visibility = checking ? Visibility.Visible : Visibility.Collapsed;
            ViewUpdateButton.Visibility = result.Phase == UpdateCheckPhase.Available ? Visibility.Visible : Visibility.Collapsed;
        }
        void Description(string value) { page.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 21, Foreground = LightTheme.Brush("#65717F"), Margin = new Thickness(0, 0, 0, 20) }); }
        void SetFeedback(string message) { feedback.Text = message; feedback.Visibility = Visibility.Visible; }
        void Try(Action action, string success = null) { try { action(); if (success != null) SetFeedback(success); feedback.Foreground = LightTheme.Brush("#65717F"); } catch (Exception error) { controller.Gallery.Store.Log(error); SetFeedback(error.Message); feedback.Foreground = LightTheme.Brush("#B74E4A"); } }
        void Switch(string title, string detail, Func<bool> read, Action<bool> change)
        {
            var box = new SwitchBox { IsChecked = read(), VerticalAlignment = VerticalAlignment.Center, Content = "开启", MinHeight = 36, Style = LightTheme.SwitchStyle };
            System.Windows.Automation.AutomationProperties.SetName(box, title);
            bool updating = false;
            RoutedEventHandler handler = delegate { if (updating) return; Try(() => change(box.IsChecked == true), "已保存"); updating = true; box.IsChecked = read(); updating = false; };
            box.Checked += handler; box.Unchecked += handler; Row(title, detail, box);
        }
        void Choice(string title, string detail, string[] options, int selected, Action<int> change)
        {
            var box = new ComboBox { ItemsSource = options, SelectedIndex = selected, Width = title == "顶部触发范围" ? 200 : 164, Height = 36, VerticalContentAlignment = VerticalAlignment.Center, Style = LightTheme.ChoiceStyle };
            choices.Add(box);
            System.Windows.Automation.AutomationProperties.SetName(box, title);
            int previous = selected; bool updating = false;
            box.SelectionChanged += delegate { if (updating) return; int next = box.SelectedIndex; try { change(next); previous = next; SetFeedback("已保存"); } catch (Exception error) { updating = true; box.SelectedIndex = previous; updating = false; SetFeedback(error.Message); } };
            Row(title, detail, box);
        }
        void ActionRow(string title, string detail, string label, Action action, string kind = null, string success = null)
        {
            var button = LightTheme.Button(label, () => Try(action, success), kind); button.Width = ActionWidth; Row(title, detail, button);
        }
        void Row(string title, string detail, FrameworkElement control)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(ActionWidth, double.IsNaN(control.Width) ? 0 : control.Width)) });
            var labels = new StackPanel { Margin = new Thickness(0, 4, 20, 4) }; labels.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrEmpty(detail)) labels.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 19, Foreground = LightTheme.Brush("#65717F"), Margin = new Thickness(0, 5, 0, 0) });
            row.Children.Add(labels); control.VerticalAlignment = VerticalAlignment.Center; control.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(control, 1); row.Children.Add(control);
            row.MinHeight = 48; page.Children.Add(new Border { Background = LightTheme.Brush("#FFFFFF"), BorderBrush = LightTheme.Brush("#E8EBEF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 0, 8), Child = row });
        }
    }
}
