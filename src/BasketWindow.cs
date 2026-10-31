using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenLine
{
    internal static class PictureBasket
    {
        static readonly Dictionary<string, BitmapSource> pictures = new Dictionary<string, BitmapSource>();
        // 原图中的裁切边界与穿绳支点，保证换皮肤后挂钩仍贴住绳子。
        static readonly Int32Rect[] bounds = {
            new Int32Rect(40, 90, 1170, 1045), new Int32Rect(50, 105, 1150, 1000),
            new Int32Rect(110, 100, 1050, 975), new Int32Rect(20, 115, 1220, 1005),
            new Int32Rect(50, 95, 1160, 1020)
        };
        static readonly Point[] hooks = { new Point(592, 155), new Point(584, 165), new Point(565, 178), new Point(599, 175), new Point(573, 155) };
        static readonly double[] rims = { 735, 710, 740, 710, 700 };

        public static BitmapSource Source(LineTheme theme)
        {
            BitmapSource picture;
            if (pictures.TryGetValue(theme.Id, out picture)) return picture;
            picture = SkinImages.Crop("PickSnap.Basket." + theme.Id, bounds[Array.IndexOf(LineTheme.All, theme)]);
            pictures.Add(theme.Id, picture);
            return picture;
        }

        public static Point Hook(LineTheme theme)
        {
            int index = Array.IndexOf(LineTheme.All, theme);
            return new Point((hooks[index].X - bounds[index].X) * 112 / bounds[index].Width,
                (hooks[index].Y - bounds[index].Y) * 112 / bounds[index].Width);
        }

        public static Point Mouth(LineTheme theme)
        {
            int index = Array.IndexOf(LineTheme.All, theme);
            return new Point(56, (rims[index] - bounds[index].Y) * 112 / bounds[index].Width);
        }
    }

    internal sealed class BasketWindow : Window
    {
        static readonly ControlTemplate choiceTemplate = (ControlTemplate)XamlReader.Parse(LightTheme.Sheet(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
          <Border x:Name='Surface' Background='Transparent' BorderBrush='Transparent' BorderThickness='1' CornerRadius='8'><ContentPresenter HorizontalAlignment='Stretch'/></Border>
        </ControlTemplate>"));
        readonly Gallery gallery;
        readonly Action<bool> setAutoHang;
        readonly Action<CaptureItem> hang;
        readonly Action<ShotAction, CaptureItem> act;
        readonly HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 当前框选手势造成的选中：矩形变动时据此实时反选，手势结束即清空。
        readonly HashSet<string> bandTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly UniformGrid thumbnails = new UniformGrid { Columns = 3, VerticalAlignment = VerticalAlignment.Top };
        readonly ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        readonly TextBlock empty = new TextBlock { TextAlignment = TextAlignment.Center, Foreground = LightTheme.Brush("#65717F"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock status = new TextBlock { FontSize = 14, Foreground = LightTheme.Brush("#283343"), VerticalAlignment = VerticalAlignment.Center };
        readonly Image preview = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(18, 10, 18, 12) };
        readonly TextBlock previewHint = new TextBlock { Text = "选择图片，查看大图", FontSize = 14, Foreground = LightTheme.Brush("#65717F"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock metadata = new TextBlock { FontSize = 12, Foreground = LightTheme.Brush("#65717F"), Margin = new Thickness(16, 0, 16, 14) };
        // 常驻数量摘要：绳上 = 当前挂在绳子上的张数（最多 24 张，可横向滚动看完），
        // 贴图 = 已钉到桌面，待挂 = 收集了但不在绳上（绳子挂满后最早的会自动退回到这里）。
        // 顶部数量摘要：一枚一枚的彩色圆角小胶囊，每项独立、间距拉开，口径一眼可分。
        readonly StackPanel summary = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Dictionary<string, Button> choices = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Action> updaters = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
        List<string> ordered = new List<string>();
        string lastClicked;
        readonly Dictionary<CaptureItem, Image> pictures = new Dictionary<CaptureItem, Image>();
        readonly HashSet<Image> visiblePictures = new HashSet<Image>();
        readonly HashSet<Image> loadingPictures = new HashSet<Image>();
        static readonly SemaphoreSlim thumbnailSlots = new SemaphoreSlim(2, 2);
        static readonly SemaphoreSlim previewSlot = new SemaphoreSlim(1, 1);
        internal readonly Button PendingTab, AllTab, HangButton, DeleteButton, SelectAllButton;
        internal readonly CheckBox AutoHang;
        readonly Func<string, bool> isPinned;
        readonly Func<int> ropeRoom;
        readonly Func<int> ropeCapacity;
        bool pendingOnly = true, refreshing, mounting, closed;
        CaptureItem previewItem;
        ImageContent previewContent;
        int previewVersion;
        // 缩略图高度固定：行距与分布不随图片多少变化，视觉与三排三列时一致。
        const double thumbnailHeight = 140;
        internal int SelectedCount { get { return selected.Count; } }
        internal Image Preview { get { return preview; } }
        internal int VisibleCount { get { return choices.Count; } }
        internal Button ChoiceFor(string path) { return choices[path]; }

        public BasketWindow(Gallery gallery, Action<bool> setAutoHang, Action<CaptureItem> hang, Action<ShotAction, CaptureItem> act, Func<string, bool> isPinned = null, Func<int> ropeRoom = null, Func<int> ropeCapacity = null,
            Func<bool> getAutoPurge = null, Action<bool> setAutoPurge = null)
        {
            this.gallery = gallery; this.setAutoHang = setAutoHang; this.hang = hang; this.act = act; this.isPinned = isPinned; this.ropeRoom = ropeRoom; this.ropeCapacity = ropeCapacity;
            Title = "图片篮子 · 拾帧";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = true;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            AutomationProperties.SetAutomationId(this, "PictureBasket");
            var screen = WindowsNative.ScreenAtPointer();
            double dpi = WindowsNative.MonitorScale(screen);
            Width = Math.Min(1040, screen.WorkingArea.Width / dpi - 32);
            Height = Math.Min(660, screen.WorkingArea.Height / dpi - 48);
            MinWidth = Math.Min(720, Width); MinHeight = Math.Min(420, Height);

            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = 48 });
            body.RowDefinitions.Add(new RowDefinition());
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) });
            var card = LightTheme.Card(body); card.Padding = new Thickness(0); Content = card;

            // 面板本身也是拖放目标：绳子上的图片不必瞄准右端的小图标，
            // 直接拖进打开着的篮子窗口就能收下（与拖到篮子图标等效，松手只取第一张）。
            AllowDrop = true;
            DragOver += delegate(object sender, DragEventArgs e)
            {
                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                bool match = paths != null && paths.Any(path => gallery.Contains(path));
                e.Effects = match ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
                card.BorderBrush = LightTheme.Brush(match ? "#2463AE" : "#E0E4EA");
            };
            DragLeave += delegate { card.BorderBrush = LightTheme.Brush("#E0E4EA"); };
            Drop += delegate(object sender, DragEventArgs e)
            {
                card.BorderBrush = LightTheme.Brush("#E0E4EA");
                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null)
                    foreach (string path in paths)
                    {
                        var item = gallery.Collected.FirstOrDefault(image => FileRules.SameFile(image.Path, path));
                        if (item != null && gallery.Contains(path)) { act(ShotAction.Remove, item); break; }
                    }
                e.Handled = true;
            };

            var heading = new Grid { Margin = new Thickness(22, 0, 18, 0) };
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = "图片篮子", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = LightTheme.Brush("#283343"), VerticalAlignment = VerticalAlignment.Center });
            heading.Children.Add(title);
            var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            tools.Children.Add(new TextBlock { Text = "新图片自动上绳", FontSize = 12, Foreground = LightTheme.Brush("#65717F"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            AutoHang = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = "关闭后，新图片仍收进图片篮子" };
            AutoHang.Template = (ControlTemplate)XamlReader.Parse(LightTheme.Sheet(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='CheckBox'>
              <Border x:Name='Track' Width='38' Height='22' CornerRadius='11' Background='#B8C0CC' BorderBrush='Transparent' BorderThickness='1'>
                <Border x:Name='Knob' Width='16' Height='16' CornerRadius='8' Background='White' HorizontalAlignment='Left' Margin='2,0'/>
              </Border><ControlTemplate.Triggers>
                <Trigger Property='IsChecked' Value='True'><Setter TargetName='Track' Property='Background' Value='#2463AE'/><Setter TargetName='Knob' Property='HorizontalAlignment' Value='Right'/></Trigger>
              </ControlTemplate.Triggers></ControlTemplate>"));
            AutomationProperties.SetName(AutoHang, "新图片自动上绳");
            AutoHang.Checked += AutoChanged; AutoHang.Unchecked += AutoChanged;
            tools.Children.Add(AutoHang);
            if (getAutoPurge != null && setAutoPurge != null)
            {
                // 旧图清理收敛为篮子里的一句话规则：超过 7 天的截图自动移到回收站；开关状态由 App 持久保存。
                // 放在头部「新图片自动上绳」左侧，两个开关并排。
                var purgeLabel = new TextBlock { Text = "自动清理 7 天前的旧图", FontSize = 12, Foreground = LightTheme.Brush("#65717F"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                var autoPurge = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                    ToolTip = "开启后：超过 7 天的截图自动移到回收站，开启时立即检查一次，之后启动时和每 6 小时检查一次。只处理收集文件夹里的副本，外部添加的原图只移除记录、不删文件。" };
                autoPurge.Template = AutoHang.Template;
                autoPurge.IsChecked = getAutoPurge();
                AutomationProperties.SetName(autoPurge, "自动清理 7 天前的旧图");
                autoPurge.Checked += delegate { setAutoPurge(true); };
                autoPurge.Unchecked += delegate { setAutoPurge(false); };
                tools.Children.Insert(0, autoPurge);
                tools.Children.Insert(0, purgeLabel);
                ((TextBlock)tools.Children[2]).Margin = new Thickness(26, 0, 10, 0);
            }
            var close = LightTheme.Button("", Close, "quiet"); close.Content = LightTheme.CloseIcon();
            close.Width = close.Height = 28; close.MinWidth = 0; close.Padding = new Thickness(0); close.Margin = new Thickness(22, 0, 0, 0);
            AutomationProperties.SetName(close, "关闭图片篮子"); tools.Children.Add(close); Grid.SetColumn(tools, 1); heading.Children.Add(tools);
            heading.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is TextBlock) DragMove(); };
            body.Children.Add(new Border { BorderBrush = LightTheme.Brush("#E8EBEF"), BorderThickness = new Thickness(0, 0, 0, 1), Child = heading });

            var tabs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            PendingTab = LightTheme.Button("待挂", delegate { pendingOnly = true; Refresh(); });
            AllTab = LightTheme.Button("全部", delegate { pendingOnly = false; Refresh(); });
            PendingTab.Margin = new Thickness(0, 0, 10, 0); tabs.Children.Add(PendingTab); tabs.Children.Add(AllTab);
            var tabRow = new Grid { Margin = new Thickness(22, 6, 22, 8) };
            tabRow.Children.Add(tabs);
            summary.HorizontalAlignment = HorizontalAlignment.Right;
            summary.ToolTip = "绳上：当前挂在绳子上的张数，一屏放不下时可用滚轮或绳子两端的圆形箭头横向查看；贴图：钉在桌面上的张数；待挂：篮子里还没上绳的张数。绳子挂满后，最早的一张会自动退回这里。";
            tabRow.Children.Add(summary);
            var tabBar = new Border { BorderBrush = LightTheme.Brush("#E8EBEF"), BorderThickness = new Thickness(0, 0, 0, 1), Child = tabRow };
            Grid.SetRow(tabBar, 1); body.Children.Add(tabBar);

            var content = new Grid { ClipToBounds = true };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            var left = new Grid { Margin = new Thickness(16, 10, 12, 12), ClipToBounds = true };
            scroll.Content = thumbnails; left.Children.Add(scroll); left.Children.Add(empty);
            // Ctrl+左键：按下未拖动 = 普通点击（按钮自己弹 Click，Ctrl+点击切换选中不受影响）；
            // 拖动超过 3px 才算框选——捕获鼠标、拉虚线矩形，框到的图批量加入选中（保留已有选中，可多次框选）。
            // 拖到列表上下边缘 28px 内时自动滚动，长列表一次框不完也能连续框。
            var bandLayer = new Canvas { IsHitTestVisible = false };
            var band = new System.Windows.Shapes.Rectangle
            {
                Stroke = LightTheme.Brush("#2463AE"), StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 4, 2.5 }, StrokeDashCap = PenLineCap.Round,
                Fill = LightTheme.Brush("#122463AE"), RadiusX = 3, RadiusY = 3, Visibility = Visibility.Collapsed
            };
            bandLayer.Children.Add(band); left.Children.Add(bandLayer);
            Point? bandStart = null; bool bandActive = false;
            var bandScroll = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            bandScroll.Tick += delegate
            {
                if (!bandActive || bandStart == null) { bandScroll.Stop(); return; }
                Rect view = scroll.TransformToAncestor(left).TransformBounds(new Rect(scroll.RenderSize));
                Point cursor = Mouse.GetPosition(left);
                double speed = cursor.Y < view.Top + 28 ? -14 : cursor.Y > view.Bottom - 28 ? 14 : 0;
                if (speed != 0)
                {
                    scroll.ScrollToVerticalOffset(Math.Max(0, Math.Min(scroll.ScrollableHeight, scroll.VerticalOffset + speed)));
                    BandSweep(band, bandStart.Value, Mouse.GetPosition(thumbnails), left, UpdateSelection);
                }
            };
            left.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.OriginalSource is System.Windows.Controls.Primitives.ScrollBar) return;
                bandStart = e.GetPosition(thumbnails); bandActive = false; // 起点记内容坐标：滚动后仍锚在最初那块上。
            };
            left.PreviewMouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (bandStart == null) return;
                Point now = e.GetPosition(thumbnails);
                if (!bandActive && (Math.Abs(now.X - bandStart.Value.X) > 3 || Math.Abs(now.Y - bandStart.Value.Y) > 3))
                {
                    bandActive = true; band.Visibility = Visibility.Visible;
                    bandTouched.Clear(); // 新手势从头计：之前手势的选中不再回退。
                    left.CaptureMouse(); bandScroll.Start();
                }
                if (!bandActive) return;
                e.Handled = true;
                BandSweep(band, bandStart.Value, now, left, UpdateSelection);
            };
            // 框选进行中接管滚轮：内容滚动、矩形起点随内容锚定，命中按新位置实时重算。
            // 平时（未框选）不加 Handled，滚轮照常交给 ScrollViewer。
            left.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if (!bandActive || bandStart == null) return;
                scroll.ScrollToVerticalOffset(Math.Max(0, Math.Min(scroll.ScrollableHeight, scroll.VerticalOffset - e.Delta)));
                BandSweep(band, bandStart.Value, Mouse.GetPosition(thumbnails), left, UpdateSelection);
                e.Handled = true;
            };
            left.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (bandStart == null) return;
                bool wasBand = bandActive;
                bandStart = null; bandActive = false; band.Visibility = Visibility.Collapsed; bandScroll.Stop();
                bandTouched.Clear(); // 手势结束，记录清空；已确定的选中保留。
                if (left.IsMouseCaptured) left.ReleaseMouseCapture();
                if (wasBand) e.Handled = true; // 框选结束吞掉释放事件，避免落到缩略图上误触发单击。
                // 未拖动的 Ctrl+单击什么都不做——Click 已由按钮正常发出。
            };
            content.Children.Add(left);
            scroll.ScrollChanged += delegate { UpdateThumbnails(); };
            Loaded += delegate { UpdateThumbnails(); if (previewItem != null) ShowPreview(previewItem); };
            var previewBody = new Grid { ClipToBounds = true };
            previewBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            previewBody.RowDefinitions.Add(new RowDefinition());
            previewBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            previewBody.Children.Add(new TextBlock { Text = "预览", Margin = new Thickness(16, 10, 0, 0), FontSize = 12, Foreground = LightTheme.Brush("#65717F") });
            Grid.SetRow(preview, 1); previewBody.Children.Add(preview);
            Grid.SetRow(previewHint, 1); previewBody.Children.Add(previewHint);
            Grid.SetRow(metadata, 2); previewBody.Children.Add(metadata);
            previewBody.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2 && previewItem != null) act(ShotAction.Open, previewItem); };
            previewBody.ToolTip = "双击打开原图";
            var previewPanel = new Border { Background = LightTheme.Brush("#F1F4F8"), CornerRadius = new CornerRadius(9), Margin = new Thickness(12, 10, 20, 12), Child = previewBody };
            var right = new Border { BorderBrush = LightTheme.Brush("#E8EBEF"), BorderThickness = new Thickness(1, 0, 0, 0), Child = previewPanel };
            Grid.SetColumn(right, 1); content.Children.Add(right);
            Grid.SetRow(content, 2); body.Children.Add(content);

            var footer = new Grid { Margin = new Thickness(24, 0, 24, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition());
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SelectAllButton = LightTheme.Button("全选", SelectAll);
            SelectAllButton.Tag = "select";
            SelectAllButton.Width = 128; SelectAllButton.Height = 38;
            SelectAllButton.VerticalAlignment = VerticalAlignment.Center;
            SelectAllButton.Margin = new Thickness(0, 0, 16, 0); footer.Children.Add(SelectAllButton);
            status.TextTrimming = TextTrimming.CharacterEllipsis; status.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(status, 1); footer.Children.Add(status);
            DeleteButton = LightTheme.Button("删除所选", DeleteSelected, "danger");
            DeleteButton.Width = 128; DeleteButton.Height = 38;
            DeleteButton.HorizontalAlignment = HorizontalAlignment.Right; DeleteButton.VerticalAlignment = VerticalAlignment.Center;
            DeleteButton.Margin = new Thickness(0, 0, 12, 0);
            DeleteButton.ToolTip = "收集副本移到回收站，外部原图只移除记录";
            AutomationProperties.SetName(DeleteButton, "删除所选图片"); Grid.SetColumn(DeleteButton, 2); footer.Children.Add(DeleteButton);
            HangButton = LightTheme.Button("挂上绳子", Mount, "primary"); HangButton.Width = 128; HangButton.Height = 38;
            HangButton.HorizontalAlignment = HorizontalAlignment.Right; HangButton.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(HangButton, "挂上绳子"); Grid.SetColumn(HangButton, 3); footer.Children.Add(HangButton);
            foreach (var button in new[] { PendingTab, AllTab, SelectAllButton, DeleteButton, HangButton })
            {
                button.FontWeight = FontWeights.Normal;
            }
            foreach (var button in new[] { SelectAllButton, DeleteButton, HangButton }) button.FontSize = 14;
            var bottom = new Border { BorderBrush = LightTheme.Brush("#E8EBEF"), BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
            Grid.SetRow(bottom, 3); body.Children.Add(bottom);
            SourceInitialized += delegate
            {
                int width = (int)Math.Round(Width * dpi), height = (int)Math.Round(Height * dpi);
                WindowsNative.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
                    screen.WorkingArea.Left + (screen.WorkingArea.Width - width) / 2,
                    screen.WorkingArea.Top + Math.Min((int)(190 * dpi), Math.Max(16, (screen.WorkingArea.Height - height) / 2)), width, height, 0x14);
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                // 兼容 Windows 习惯：Ctrl+A 全选/取消全选。
                if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    SelectAll(); e.Handled = true; return;
                }
                if (e.Key == Key.Escape) Close();
            };
            gallery.Changed += GalleryChanged;
            Closed += delegate { closed = true; gallery.Changed -= GalleryChanged; previewVersion++; preview.Source = null; foreach (var image in pictures.Values) image.Source = null; pictures.Clear(); visiblePictures.Clear(); };
            Refresh();
        }

        void AutoChanged(object sender, RoutedEventArgs e)
        {
            if (!refreshing && gallery.Store.State.AutoHang != (AutoHang.IsChecked == true)) setAutoHang(AutoHang.IsChecked == true);
        }

        void GalleryChanged() { if (!mounting) Refresh(); }

        void Refresh()
        {
            refreshing = true;
            AutoHang.IsChecked = gallery.Store.State.AutoHang;
            refreshing = false;
            selected.RemoveWhere(path => (pendingOnly && gallery.Contains(path)) || !gallery.Collected.Any(i => FileRules.SameFile(i.Path, path)));
            int pending = gallery.Collected.Count(i => !gallery.Contains(i.Path));
            // 数量口径与绳上一致：「已挂上」只统计肉眼可见的夹子；贴到桌面的单独归入「贴图」。
            int pinnedCount = isPinned == null ? 0 : gallery.Collected.Count(i => gallery.Contains(i.Path) && isPinned(i.Path));
            int onRope = gallery.Collected.Count(i => gallery.Contains(i.Path)) - pinnedCount;
            int capacity = ropeCapacity == null ? 0 : ropeCapacity();
            int room = ropeRoom == null ? -1 : Math.Max(0, ropeRoom());
            // 摘要胶囊：一项一枚、颜色分域（绳上蓝、贴图灰、待挂琥珀、状态红/绿），间距拉开方便区分。
            summary.Children.Clear();
            summary.Children.Add(Chip("绳上", onRope + (capacity > 0 ? "/" + capacity : ""), "#E8F1FB", "#5B7A9E", "#2463AE"));
            summary.Children.Add(Chip("贴图", pinnedCount.ToString(), "#F1F4F8", "#5B6B7C", "#343D49"));
            summary.Children.Add(Chip("待挂", pending.ToString(), "#FCF3E2", "#8A6D35", "#9A6B1F"));
            if (room == 0) summary.Children.Add(Chip("绳子已满", null, "#FBE3E0", "#A93E3A", "#A93E3A"));
            else if (room > 0) summary.Children.Add(Chip("还能挂", room + " 张", "#E4F2E8", "#5B8A67", "#2E7D46"));
            PendingTab.Content = "待挂 " + pending; AllTab.Content = "全部 " + gallery.Collected.Count;
            PendingTab.Tag = pendingOnly ? "primary" : "quiet"; AllTab.Tag = pendingOnly ? "quiet" : "primary";
            double offset = scroll.VerticalOffset;
            thumbnails.Children.Clear(); choices.Clear(); updaters.Clear(); pictures.Clear(); visiblePictures.Clear();
            var items = gallery.Collected.Where(i => !pendingOnly || !gallery.Contains(i.Path)).OrderByDescending(i => i.Written).ToArray();
            ordered = items.Select(i => i.Path).ToList();
            if (lastClicked != null && !ordered.Contains(lastClicked)) lastClicked = null;
            foreach (var item in items) AddChoice(item);
            Dispatcher.BeginInvoke(new Action(UpdateThumbnails), System.Windows.Threading.DispatcherPriority.Loaded);
            empty.Text = gallery.Collected.Count == 0 ? "图片篮子还是空的\n截图收集后会出现在这里" : "图片都挂好了\n切换到「全部」查看收集的图片";
            empty.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            scroll.Visibility = items.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            scroll.ScrollToVerticalOffset(offset);
            UpdateSelection();
            if (previewItem != null && !gallery.Collected.Contains(previewItem)) previewItem = null;
            if (previewItem == null) ShowPreview(items.FirstOrDefault());
            else if (previewContent != previewItem.Image) ShowPreview(previewItem);
        }

        // 框选一步：起点/当前位置都是**内容坐标**（随滚动移动），所以滚轮翻页后矩形仍锚定在最初选中的那块上。
        // 绘制时换算回可视区坐标；命中判定在内容坐标里做，两边随内容同步滚动，结果稳定。
        void BandSweep(System.Windows.Shapes.Rectangle band, Point start, Point now, Grid left, Action after)
        {
            Rect rect = new Rect(start, now);
            Rect view = thumbnails.TransformToAncestor(left).TransformBounds(rect);
            Canvas.SetLeft(band, view.Left); Canvas.SetTop(band, view.Top); band.Width = view.Width; band.Height = view.Height;
            var inside = new HashSet<string>();
            foreach (var entry in choices)
            {
                if (!ordered.Contains(entry.Key)) continue;
                Rect bounds = entry.Value.TransformToAncestor(thumbnails).TransformBounds(new Rect(0, 0, entry.Value.ActualWidth, entry.Value.ActualHeight));
                if (rect.IntersectsWith(bounds)) inside.Add(entry.Key);
            }
            foreach (string path in bandTouched)
                if (!inside.Contains(path)) selected.Remove(path);
            foreach (string path in inside)
            {
                selected.Add(path);
                bandTouched.Add(path);
            }
            foreach (var update in updaters.Values) update();
            after();
        }

        // 一枚摘要胶囊：浅色圆角底 + 「标签 数值」两段文字（数值加粗、用主题色），负间距靠外边距拉开。
        static Border Chip(string label, string value, string bg, string dim, string accent)
        {
            var text = new TextBlock { FontSize = 12 };
            if (value == null) text.Inlines.Add(new Run(label) { Foreground = LightTheme.Brush(accent), FontWeight = FontWeights.SemiBold });
            else
            {
                text.Inlines.Add(new Run(label + "  ") { Foreground = LightTheme.Brush(dim) });
                text.Inlines.Add(new Run(value) { Foreground = LightTheme.Brush(accent), FontWeight = FontWeights.SemiBold });
            }
            return new Border
            {
                Background = LightTheme.Brush(bg), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 3, 10, 4), Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center, Child = text
            };
        }

        void AddChoice(CaptureItem item)
        {
            bool mounted = gallery.Contains(item.Path);
            bool pinned = mounted && isPinned != null && isPinned(item.Path); // 已贴到桌面，暂不在绳上
            var stack = new StackPanel();
            var image = new Grid { Height = thumbnailHeight };
            var picture = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(7) }; pictures[item] = picture;
            image.Children.Add(picture);
            var mark = new Border { Width = mounted ? 54 : 21, Height = 21, CornerRadius = new CornerRadius(11),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(7),
                BorderThickness = new Thickness(1) };
            image.Children.Add(mark);
            var frame = new Border { Child = image, Background = LightTheme.Brush("#F1F4F8"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8) };
            stack.Children.Add(frame);
            stack.Children.Add(new TextBlock { Text = TimeLabel(item.Written), FontSize = 11, Foreground = LightTheme.Brush("#65717F"), Margin = new Thickness(3, 7, 0, 0) });
            var button = new Button { Style = LightTheme.ButtonStyle, Tag = "quiet", VerticalAlignment = VerticalAlignment.Top };
            Action updateChoice = delegate
            {
                bool chosen = selected.Contains(item.Path);
                mark.Visibility = chosen || mounted ? Visibility.Visible : Visibility.Collapsed;
                frame.BorderBrush = LightTheme.Brush(chosen ? "#2463AE" : button.IsKeyboardFocused ? "#6E97CB" : "#E5EAF1");
                mark.Background = LightTheme.Brush(chosen ? "#2463AE" : "#FFFFFF"); mark.BorderBrush = LightTheme.Brush(chosen ? "#2463AE" : "#9CAABD");
                if (mounted)
                {
                    string label = pinned ? "贴图中" : "已挂上";
                    mark.Width = chosen ? 80 : 58;
                    mark.Child = new TextBlock { Text = chosen ? "\u2713 " + label : label, FontSize = 10, Foreground = chosen ? Brushes.White : LightTheme.Brush("#65717F"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                }
                else mark.Child = chosen ? new TextBlock { Text = "\uE73E", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null;
                AutomationProperties.SetItemStatus(button, chosen ? (mounted ? (pinned ? "已选择，贴图中" : "已选择，已挂上") : "已选择") : pinned ? "贴图中" : mounted ? "已挂上" : "待挂");
            };
            button.IsKeyboardFocusedChanged += delegate { updateChoice(); };
            // 悬停反馈只落在图片框上，不把下面的时间行也罩进去。
            button.MouseEnter += delegate { frame.Background = LightTheme.Brush("#E9EEF4"); };
            button.MouseLeave += delegate { frame.Background = LightTheme.Brush("#F1F4F8"); };
            button.Click += delegate
            {
                // 点击即切换：点一下选中，再点一下取消（无需按 Ctrl）；
                // Shift+点击=从上次点击处范围选择（覆盖式）；Ctrl+A 在 PreviewKeyDown 里处理。
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                if (shift && lastClicked != null)
                {
                    int from = ordered.IndexOf(lastClicked), to = ordered.IndexOf(item.Path);
                    if (from >= 0 && to >= 0)
                    {
                        selected.Clear();
                        for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++) selected.Add(ordered[i]);
                    }
                }
                else
                {
                    if (!selected.Add(item.Path)) selected.Remove(item.Path);
                }
                lastClicked = item.Path;
                ShowPreview(item);
                foreach (var update in updaters.Values) update();
                UpdateSelection();
            };
            button.Content = stack; button.Height = Double.NaN; button.Padding = new Thickness(0); button.Margin = new Thickness(6, 0, 6, 16);
            button.Template = choiceTemplate;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.ToolTip = Path.GetFileName(item.Path);
            AutomationProperties.SetName(button, Path.GetFileName(item.Path));
            updateChoice();
            updaters[item.Path] = updateChoice;
            thumbnails.Children.Add(button); choices[item.Path] = button;
        }

        void UpdateThumbnails()
        {
            if (closed) return;
            // 缩略图高度固定，不随可视区或图片数量撑缩：两张和三排时间距、分布保持一致。
            var viewport = new Rect(0, 0, scroll.ActualWidth, scroll.ActualHeight);
            visiblePictures.Clear();
            foreach (var entry in pictures)
            {
                var picture = entry.Value;
                var frame = (FrameworkElement)picture.Parent;
                if (frame.ActualWidth == 0 || !frame.IsDescendantOf(scroll)) continue;
                var bounds = frame.TransformToAncestor(scroll).TransformBounds(new Rect(frame.RenderSize));
                if (!bounds.IntersectsWith(viewport)) { picture.Source = null; continue; }
                visiblePictures.Add(picture);
                if (picture.Source != null) continue;
                var cached = entry.Key.Image.CachedThumbnail;
                if (cached != null) { picture.Source = cached; continue; }
                if (loadingPictures.Add(picture)) LoadThumbnail(entry.Key, picture);
            }
        }

        bool ThumbnailCurrent(CaptureItem item, Image picture, ImageContent content)
        {
            Image current;
            return !closed && visiblePictures.Contains(picture) && item.Image == content && pictures.TryGetValue(item, out current) && current == picture;
        }

        async void LoadThumbnail(CaptureItem item, Image picture)
        {
            ImageContent content = item.Image;
            try
            {
                await thumbnailSlots.WaitAsync();
                try
                {
                    if (!ThumbnailCurrent(item, picture, content)) return;
                    var bitmap = await Task.Run(() => content.Thumbnail);
                    if (ThumbnailCurrent(item, picture, content)) picture.Source = bitmap;
                }
                finally { thumbnailSlots.Release(); }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (NotSupportedException) { }
            catch (FormatException) { }
            finally { loadingPictures.Remove(picture); }
        }

        internal static string TimeLabel(DateTime time)
        {
            if (time.Kind == DateTimeKind.Utc) time = time.ToLocalTime();
            return (time.Date == DateTime.Today ? "今天 " : time.ToString("MM-dd ")) + time.ToString("HH:mm");
        }

        async void ShowPreview(CaptureItem item)
        {
            int version = ++previewVersion;
            previewItem = item; previewContent = item == null ? null : item.Image;
            preview.Source = item == null ? null : item.Image.CachedThumbnail;
            previewHint.Visibility = preview.Source == null ? Visibility.Visible : Visibility.Collapsed;
            previewHint.Text = item == null ? "选择图片，查看大图" : "正在加载预览…";
            metadata.Text = item == null ? "" : TimeLabel(item.Written) + " · " + item.Image.PixelWidth + " × " + item.Image.PixelHeight;
            if (item == null || !IsLoaded) return;
            try
            {
                await previewSlot.WaitAsync();
                try
                {
                    if (closed || version != previewVersion) return;
                    ImageContent content = item.Image;
                    var visual = PresentationSource.FromVisual(this);
                    double scale = visual == null ? 1 : visual.CompositionTarget.TransformToDevice.M11;
                    var panel = (Grid)preview.Parent;
                    double width = Math.Max(1, (panel.ActualWidth - preview.Margin.Left - preview.Margin.Right) * scale);
                    double height = Math.Max(1, (panel.RowDefinitions[1].ActualHeight - preview.Margin.Top - preview.Margin.Bottom) * scale);
                    var picture = await Task.Run(() => ReadPreview(item.Path, content, width, height));
                    if (version == previewVersion) { preview.Source = picture; previewHint.Visibility = Visibility.Collapsed; }
                }
                finally { previewSlot.Release(); }
            }
            catch (IOException) { if (version == previewVersion) { preview.Source = null; previewHint.Text = "图片已移动或删除"; previewHint.Visibility = Visibility.Visible; } }
            catch (UnauthorizedAccessException) { if (version == previewVersion) { preview.Source = null; previewHint.Text = "暂时无法读取这张图片"; previewHint.Visibility = Visibility.Visible; } }
            catch (NotSupportedException) { if (version == previewVersion) { preview.Source = null; previewHint.Text = "暂时无法预览这张图片"; previewHint.Visibility = Visibility.Visible; } }
            catch (FormatException) { if (version == previewVersion) { preview.Source = null; previewHint.Text = "暂时无法预览这张图片"; previewHint.Visibility = Visibility.Visible; } }
        }

        static BitmapSource ReadPreview(string path, ImageContent content, double width, double height)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream;
                double ratio = Math.Min(1, Math.Min(width / content.PixelWidth, height / content.PixelHeight));
                if (width / content.PixelWidth < height / content.PixelHeight) bitmap.DecodePixelWidth = Math.Max(1, (int)Math.Ceiling(content.PixelWidth * ratio));
                else bitmap.DecodePixelHeight = Math.Max(1, (int)Math.Ceiling(content.PixelHeight * ratio));
                bitmap.EndInit();
                var color = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                int stride = color.PixelWidth * 4; var pixels = new byte[stride * color.PixelHeight];
                color.CopyPixels(pixels, stride, 0);
                var result = BitmapSource.Create(color.PixelWidth, color.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
                result.Freeze(); return result;
            }
        }

        void SelectAll()
        {
            bool clear = choices.Count > 0 && choices.Keys.All(selected.Contains);
            foreach (string path in choices.Keys)
                if (clear) selected.Remove(path); else selected.Add(path);
            Refresh();
        }

        void UpdateSelection()
        {
            SelectAllButton.IsEnabled = choices.Count > 0;
            bool all = choices.Count > 0 && choices.Keys.All(selected.Contains);
            SelectAllButton.Content = all ? "✓ 取消全选" : "全选";
            // 两态视觉：未全选是浅色描边款，全选后转填充款，一眼可分。
            SelectAllButton.Tag = all ? "select-on" : "select";
            int pending = selected.Count(path => !gallery.Contains(path));
            // 临界提示：选中的待挂数超过绳子剩余空间时，提前告知只能挂一部分。
            int room = ropeRoom == null ? int.MaxValue : Math.Max(0, ropeRoom());
            status.Text = (all ? "已全选 " + choices.Count + " 张" : "已选 " + selected.Count + " 张")
                + (pending < selected.Count ? " · " + pending + " 张待挂" : "")
                + (pending > room ? " · 绳子还能挂 " + room + " 张" : "");
            HangButton.ToolTip = room < int.MaxValue ? "绳子最多挂 24 张，超出部分会留在篮子" : null;
            HangButton.IsEnabled = pending > 0;
            DeleteButton.IsEnabled = selected.Count > 0;
        }

        void Mount()
        {
            var items = gallery.Collected.Where(i => selected.Contains(i.Path) && !gallery.Contains(i.Path)).ToArray();
            // 绳子容量有限（最多 24 张）：一次只挂到满，剩下的留在篮子并保持选中，避免刚挂上又被挤下来。
            int room = ropeRoom == null ? items.Length : Math.Max(0, ropeRoom());
            var mount = items.Take(room).ToArray();
            mounting = true;
            try { foreach (var item in mount) hang(item); }
            finally { mounting = false; }
            selected.RemoveWhere(path => gallery.Contains(path));
            Refresh();
            status.Text = mount.Length == 0 ? "绳子上已经挂满，先取下几张再挂"
                : "已挂上 " + mount.Length + " 张" + (mount.Length < items.Length ? " · 绳子已满，其余 " + (items.Length - mount.Length) + " 张保持选中" : "");
        }

        void DeleteSelected()
        {
            var items = gallery.Collected.Where(item => selected.Contains(item.Path)).ToArray();
            if (items.Length == 0) return;
            mounting = true;
            try { foreach (var item in items) act(ShotAction.Trash, item); }
            finally { mounting = false; Refresh(); }
            int removed = items.Count(item => !gallery.Collected.Contains(item));
            status.Text = "已删除 " + removed + " 张" + (removed < items.Length ? " · " + (items.Length - removed) + " 张未删除" : "");
        }
    }
}
