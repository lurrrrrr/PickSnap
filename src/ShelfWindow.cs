using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ScreenLine
{
    internal enum ShotAction { Copy, Open, Edit, Remove, Trash, Save, Locate, RemoveLeft, RemoveRight, RemoveOthers, RemoveAll }

    internal sealed class ShelfWindow : Window
    {
        const double HiddenY = -300;
        readonly Gallery gallery;
        readonly Action<ShotAction, CaptureItem> act;
        internal readonly HoverPreview Preview;
        readonly ScrollViewer viewport = new ScrollViewer();
        readonly Canvas content = new Canvas();
        readonly Canvas line = new Canvas();
        readonly System.Windows.Shapes.Path rope = new System.Windows.Shapes.Path();
        readonly LinearGradientBrush edgeMask = new LinearGradientBrush(new GradientStopCollection {
            new GradientStop(Colors.White, 0), new GradientStop(Colors.White, 0.03),
            new GradientStop(Colors.White, 0.97), new GradientStop(Colors.White, 1)
        }, new Point(0, 0), new Point(1, 0));
        readonly Dictionary<string, ShotCard> cards = new Dictionary<string, ShotCard>(StringComparer.OrdinalIgnoreCase);
        readonly Border hint = new Border();
        readonly Button enable = new Button();
        readonly TextBlock helpTitle = new TextBlock();
        readonly TextBlock helpDetail = new TextBlock();
        readonly TextBlock basketCount = new TextBlock();
        readonly Image basketImage = new Image { Width = 112, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        readonly RotateTransform basketTurn = new RotateTransform();
        readonly ScaleTransform basketCounterScale = new ScaleTransform(1, 1);
        readonly SpringMotion basketMotion = new SpringMotion(0.5, 0.5, 0);
        readonly SpringMotion basketCounterMotion = new SpringMotion(0.28, 0.65, 1);
        int pendingPictures;
        readonly Button openBasket = new Button();
        internal readonly Button BasketButton = new Button();
        public event Action BasketRequested;
        internal readonly Button TextButton = new Button();
        readonly EnvelopeIcon envelopeImage = new EnvelopeIcon { Width = 96, Height = 86, Stretch = Stretch.Fill, IsHitTestVisible = false };
        readonly Canvas basketHolder, textHolder;
        readonly ClothesClip textClip = new ClothesClip();
        readonly TextBlock textCount = new TextBlock { FontFamily = LightTheme.UiFont, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center };
        readonly Border textCounter = new Border();
        public event Action TextRequested;
        public event Action RopeHidden;
        readonly TranslateTransform slide = new TranslateTransform(0, HiddenY);
        readonly SpringMotion revealMotion = new SpringMotion(0.42, 0.82, HiddenY);
        readonly SpringMotion ropeMotion = new SpringMotion(0.55, 0.78, 18);
        readonly SpringMotion scrollMotion = new SpringMotion(0.5, 0.9, 0);
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Random random = new Random();
        readonly List<ShotFlight> flights = new List<ShotFlight>();
        readonly Dictionary<string, PinnedImageWindow> pins = new Dictionary<string, PinnedImageWindow>(StringComparer.OrdinalIgnoreCase);
        bool closing;
        readonly DispatcherTimer pointer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        readonly DispatcherTimer scrollHover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        DateTime edgeSince = DateTime.MaxValue, awaySince = DateTime.MaxValue, peekUntil;
        DateTime nextGust;
        double lastFrame, hideTime, hideFrom, drawnWidth = -1, drawnSag = -1;
        double lastScrollOffset;
        bool rendering;
        bool pinned, previousDown, suppressEdge, trackingScrollHover, avoidingCaption;
        string viewingName;
        IntPtr viewingWindow;
        DateTime viewerSearchUntil;
        public bool Revealed { get; private set; }
        public IntPtr Handle { get; private set; }
        public Forms.Screen Display { get; private set; }
        public LineTheme Appearance { get; private set; }
        internal bool ChoosingAppearance { get; set; }
        internal bool ChoosingBasket { get; set; }
        internal bool ChoosingTexts { get; set; }
        internal bool Capturing { get; set; }
        double AccessoryWidth { get { return 156 + 104; } }
        // 绳子两端的滚动箭头：无底板、无边框的纤细雪佛龙，外面包一块几乎不可见的隐形垫（约 2% 透明度）
        // 扩大命中区——纯透明背景在分层窗口会被系统穿透，alpha>0 才点得到。默认近乎隐身（透明度 0.25），
        // 指针进入绳带区域升到 0.6，悬停箭头 0.9 并轻微放大（arrowHover 轮询驱动）。随绳子一起隐藏（共用 slide 位移）。
        static Border ScrollArrow(bool right)
        {
            var chevron = new Path
            {
                Data = Geometry.Parse(right ? "M9.5,4.5 L15.5,12 L9.5,19.5" : "M14.5,4.5 L8.5,12 L14.5,19.5"),
                Stroke = LightTheme.Brush("#F2F5F9"), StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                Width = 15, Height = 15, Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            return new Border
            {
                Width = 34, Height = 34, Child = chevron,
                Background = LightTheme.Brush("#05050505"), BorderThickness = new Thickness(0),
                HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = right ? new Thickness(0, 26, 288, 0) : new Thickness(22, 26, 0, 0),
                Opacity = 0.25, Cursor = Cursors.Hand, Visibility = Visibility.Collapsed,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
        }
        readonly Border scrollRight = ScrollArrow(true), scrollLeft = ScrollArrow(false);
        readonly ScaleTransform scrollRightScale = new ScaleTransform(), scrollLeftScale = new ScaleTransform();
        // 箭头明暗轮询：绳带悬停状态（含透明像素区，WPF 事件收不到）靠定时器探测。
        readonly DispatcherTimer arrowHover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        // 拖夹子到视口边缘时的自动滚动方向（-1 向左 / 1 向右 / 0 不滚）；按渲染帧积分推进，保持顺滑。
        int dragScrollDirection;
        // 刚放下夹子后不再吸附，避免视图自己挪动。
        bool suppressSnap;
        // 低级鼠标钩子：接管绳子横带内空白区域的滚轮（透明像素在系统层穿透，WPF 收不到）。
        IntPtr mouseHook;
        WindowsNative.MouseHookProc mouseHookProc;
        uint processId;
        const double CardWidth = 164;
        const double DipRadius = 78;
        const double DipDepth = 8;
        const double CardStep = 172;
        // 夹子被拖过之后的位置（路径 → 内容坐标 X），没记录的就按默认均分排列。
        readonly Dictionary<string, double> manualOffsets = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        // 已完成首次排位的卡片：此后保持原位不动，只有新挂的卡片参与避让排位。
        readonly HashSet<string> settled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ShotCard dragCard;
        double dragGrab;
        double dipCenter, dipAmount;
        double drawnDip = -1, drawnDipCenter = -1;
        bool dragMoved;
        ShotCard suppressGripClick;
        double PhotoWidth { get { return Math.Max(1, (ActualWidth > 0 ? ActualWidth : Width) - 48 - AccessoryWidth); } }
        double LineWidth { get { return Math.Max(1, (ActualWidth > 0 ? ActualWidth : Width) - 48); } }
        double ScrollLimit { get { return Math.Max(0, content.Width - PhotoWidth); } }
        internal Point TextAnchor
        {
            get
            {
                if (!TextButton.IsVisible) return new Point(Display.WorkingArea.Right - 80, Display.WorkingArea.Top + 160 * WindowsNative.MonitorScale(Display));
                var point = textCounter.PointToScreen(new Point(textCounter.ActualWidth / 2, textCounter.ActualHeight + 4));
                point.Y -= slide.Y * WindowsNative.MonitorScale(Display);
                return point;
            }
        }

        public ShelfWindow(Gallery gallery, Action<ShotAction, CaptureItem> act, Action enableClipboard)
        {
            this.gallery = gallery;
            this.act = act;
            Preview = new HoverPreview(this, gallery.Store.State.HoverPreview);
            scrollHover.Tick += delegate
            {
                scrollHover.Stop(); trackingScrollHover = false;
                if (!Revealed || Mouse.LeftButton != MouseButtonState.Released || Mouse.RightButton != MouseButtonState.Released) return;
                var card = cards.Values.FirstOrDefault(item => item.Surface.IsMouseOver && !item.Grip.IsMouseOver);
                if (card != null) Preview.Enter(card);
            };
            Title = "PickSnap · 拾帧";
            Width = 1000;
            Height = 220;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            LightTheme.Polish(this);
            viewport.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            viewport.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            viewport.Content = content;
            viewport.Margin = new Thickness(24, 0, 284, 0);
            viewport.RenderTransform = slide;
            viewport.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e)
            {
                if (e.HorizontalChange != 0)
                {
                    Preview.Dismiss();
                    if (trackingScrollHover) { scrollHover.Stop(); scrollHover.Start(); }
                }
                if (e.ExtentWidthChange != 0 || e.ViewportWidthChange != 0) lastScrollOffset = viewport.HorizontalOffset;
                foreach (var card in cards.Values) card.Frame(0, ropeMotion.Position, LineWidth, viewport.HorizontalOffset, viewport.ActualWidth);
                UpdateEdgeMask();
                UpdateScrollHint();
            };
            // 滚轮挂在窗口层而不是 viewport 上：鼠标落在卡片之间的空档时，
            // 命中的是窗口本身（不是 viewport 的子元素），viewport 的隧道事件根本收不到。
            // 另外透明分层窗口的空白像素（alpha=0）在系统层直接穿透，那里的滚轮
            // 由 WatchMouse 低级钩子接管——两条路最终都走 ScrollByWheel。
            PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                ScrollByWheel(e.Delta);
                e.Handled = true;
            };
            var root = new Grid();
            line.Margin = new Thickness(24, 0, 24, 0); line.RenderTransform = slide; line.IsHitTestVisible = false;
            line.Children.Add(rope); root.Children.Add(line);
            root.Children.Add(viewport); Content = root;
            Appearance = LineTheme.FromId(gallery.Store.State.Theme);
            // 只恢复"当前挂在绳上"的卡片位置：篮子里的图如果沿用上次的旧坐标，
            // 重新挂上时会直接跳回最右端（而不是接到最后一张后面），这里把陈旧的坐标过滤掉。
            if (gallery.Store.State.CardOffsets != null)
                foreach (var entry in gallery.Store.State.CardOffsets)
                    if (gallery.Contains(entry.Key)) manualOffsets[entry.Key] = entry.Value;
            RopeLine.Apply(rope, Appearance);

            var basket = basketHolder = new Canvas { Width = 156, Height = 220, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 24, 0), RenderTransform = slide };
            var basketBody = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            basketImage.Source = PictureBasket.Source(Appearance);
            RenderOptions.SetBitmapScalingMode(basketImage, BitmapScalingMode.HighQuality);
            basketImage.RenderTransform = basketTurn;
            Point hook = PictureBasket.Hook(Appearance); basketTurn.CenterX = hook.X; basketTurn.CenterY = hook.Y;
            basketImage.Height = 112 * basketImage.Source.Height / basketImage.Source.Width;
            basketBody.Children.Add(basketImage);
            basketCount.FontFamily = LightTheme.UiFont; basketCount.FontSize = 13; basketCount.FontWeight = FontWeights.SemiBold;
            basketCount.Foreground = Brushes.White; basketCount.TextAlignment = TextAlignment.Center;
            basketCount.RenderTransformOrigin = new Point(0.5, 0.5); basketCount.RenderTransform = basketCounterScale;
            basketBody.Children.Add(new Border { Child = basketCount, Background = LightTheme.Brush("#D920242C"),
                BorderBrush = LightTheme.Brush("#40FFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 1, 6, 2), MinWidth = 26, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
            BasketButton.Content = basketBody; BasketButton.Width = 144; BasketButton.Height = 154; BasketButton.Focusable = false; BasketButton.Cursor = Cursors.Hand;
            BasketButton.ToolTip = "打开图片篮子";
            BasketButton.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(LightTheme.Sheet("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='#02FFFFFF'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Top'/></Border></ControlTemplate>"));
            BasketButton.Click += delegate { if (BasketRequested != null) BasketRequested(); };
            BasketButton.MouseEnter += delegate { basketMotion.Target = 1.3; StartRendering(); };
            BasketButton.MouseLeave += delegate { basketMotion.Target = 0; };
            System.Windows.Automation.AutomationProperties.SetName(BasketButton, "打开图片篮子");
            basket.Children.Add(BasketButton); Canvas.SetLeft(BasketButton, 6); Canvas.SetTop(BasketButton, 20);
            // 篮子作为拖放目标：把挂着的图片直接拖到篮子上即可收进篮子，
            // 松手后走「取下放篮子」的既有动画（飞行入篮 + 篮身晃动）。
            BasketButton.AllowDrop = true;
            BasketButton.DragOver += delegate(object sender, DragEventArgs e)
            {
                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                bool match = paths != null && paths.Any(path => cards.ContainsKey(path));
                e.Effects = match ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
                if (match && basketMotion.Target == 0) { basketMotion.Target = 1.3; StartRendering(); }
            };
            BasketButton.DragLeave += delegate(object sender, DragEventArgs e)
            {
                if (!BasketButton.IsMouseOver && basketMotion.Target != 0) { basketMotion.Target = 0; StartRendering(); }
            };
            BasketButton.Drop += delegate(object sender, DragEventArgs e)
            {
                if (basketMotion.Target != 0) { basketMotion.Target = 0; StartRendering(); }
                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null)
                    foreach (string path in paths)
                    {
                        ShotCard card;
                        if (cards.TryGetValue(path, out card) && !card.Detached) { act(ShotAction.Remove, card.Item); break; }
                    }
                e.Handled = true;
            };
            root.Children.Add(basket);

            textHolder = new Canvas { Width = 112, Height = 220, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 166, 0), RenderTransform = slide };
            var textBody = new Grid { Width = 96, Height = 154 };
            envelopeImage.Apply(Appearance);
            envelopeImage.HorizontalAlignment = HorizontalAlignment.Center; envelopeImage.VerticalAlignment = VerticalAlignment.Top;
            envelopeImage.Margin = new Thickness(0, 20, 0, 0);
            RenderOptions.SetBitmapScalingMode(envelopeImage, BitmapScalingMode.HighQuality); textBody.Children.Add(envelopeImage);
            textClip.Apply(Appearance, 2); textClip.HorizontalAlignment = HorizontalAlignment.Center; textClip.VerticalAlignment = VerticalAlignment.Top;
            textBody.Children.Add(textClip);
            textCounter.Child = textCount; textCounter.Background = LightTheme.Brush("#D920242C");
            textCounter.BorderBrush = LightTheme.Brush("#40FFFFFF"); textCounter.BorderThickness = new Thickness(1); textCounter.CornerRadius = new CornerRadius(6);
            textCounter.Padding = new Thickness(6, 1, 6, 2); textCounter.MinWidth = 26;
            textCounter.HorizontalAlignment = HorizontalAlignment.Center; textCounter.VerticalAlignment = VerticalAlignment.Top;
            textBody.Children.Add(textCounter);
            TextButton.Content = textBody; TextButton.Width = 96; TextButton.Height = 154; TextButton.Focusable = false; TextButton.Cursor = Cursors.Hand;
            TextButton.Template = BasketButton.Template; TextButton.ToolTip = "打开文字";
            TextButton.MouseEnter += delegate { envelopeImage.Expand(true); StartRendering(); };
            TextButton.MouseLeave += delegate { envelopeImage.Expand(false); };
            AutomationProperties.SetName(TextButton, "打开文字");
            TextButton.Click += delegate { if (TextRequested != null) TextRequested(); };
            textHolder.Children.Add(TextButton); Canvas.SetLeft(TextButton, 8);
            root.Children.Add(textHolder); UpdateTexts();

            scrollRight.ToolTip = "横向浏览后面的图片（滚轮也可以）";
            scrollLeft.ToolTip = "回到前面的图片";
            scrollRight.MouseLeftButtonUp += delegate
            {
                scrollMotion.Target = Math.Min(ScrollLimit, viewport.HorizontalOffset + viewport.ViewportWidth * 0.9);
                StartRendering();
            };
            scrollLeft.MouseLeftButtonUp += delegate
            {
                scrollMotion.Target = Math.Max(0, viewport.HorizontalOffset - viewport.ViewportWidth * 0.9);
                StartRendering();
            };
            AutomationProperties.SetName(scrollRight, "横向浏览后面的图片");
            AutomationProperties.SetName(scrollLeft, "回到前面的图片");
            // 与绳子共用同一位移动画：绳子收起时箭头一起滑出屏幕；悬停放大叠加在位移之上。
            scrollRight.RenderTransform = new TransformGroup { Children = { slide, scrollRightScale } };
            scrollLeft.RenderTransform = new TransformGroup { Children = { slide, scrollLeftScale } };
            root.Children.Add(scrollRight); root.Children.Add(scrollLeft);
            arrowHover.Tick += delegate { UpdateArrowGlow(); };
            arrowHover.Start();

            var help = new StackPanel { Margin = new Thickness(20, 14, 20, 14) };
            helpTitle.FontSize = 15; helpTitle.FontWeight = FontWeights.SemiBold; helpTitle.Foreground = LightTheme.Brush("#283343"); helpTitle.HorizontalAlignment = HorizontalAlignment.Center;
            helpDetail.FontSize = 12; helpDetail.Foreground = LightTheme.Brush("#65717F"); helpDetail.Margin = new Thickness(0, 8, 0, 12); helpDetail.HorizontalAlignment = HorizontalAlignment.Center;
            help.Children.Add(helpTitle); help.Children.Add(helpDetail);
            openBasket.Content = "打开图片篮子"; openBasket.Style = LightTheme.ButtonStyle; openBasket.Tag = "primary";
            openBasket.HorizontalAlignment = HorizontalAlignment.Center; openBasket.Margin = new Thickness(0, 0, 0, 8);
            openBasket.Click += delegate { if (BasketRequested != null) BasketRequested(); }; help.Children.Add(openBasket);
            enable.Content = "开启剪贴板收集";
            enable.Style = LightTheme.ButtonStyle;
            enable.Tag = "primary";
            enable.Padding = new Thickness(14, 5, 14, 5);
            enable.HorizontalAlignment = HorizontalAlignment.Center;
            enable.Focusable = false;
            enable.Click += delegate { enableClipboard(); Update(); };
            help.Children.Add(enable);
            hint.Child = help;
            hint.Width = 380;
            hint.Background = new SolidColorBrush(Color.FromArgb(248, 255, 255, 255));
            hint.CornerRadius = new CornerRadius(16);
            hint.BorderBrush = new SolidColorBrush(Color.FromRgb(218, 224, 232));
            hint.BorderThickness = new Thickness(1);
            hint.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.18 };
            content.Children.Add(hint);

            SourceInitialized += delegate
            {
                Handle = new WindowInteropHelper(this).Handle;
                WindowsNative.PrepareOverlay(Handle);
                WindowsNative.PassMouse(Handle, true);
                HwndSource.FromHwnd(Handle).AddHook(Messages);
                processId = WindowsNative.WindowProcess(Handle);
                mouseHookProc = WatchMouse;
                mouseHook = WindowsNative.InstallMouseHook(mouseHookProc);
                MoveToDisplay();
                pointer.Start();
            };
            Loaded += delegate { Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate { if (!closing) MoveToDisplay(); })); };
            SizeChanged += delegate { UpdateAccessories(); TrimToCapacity(); LayoutCards(); };
            pointer.Tick += Tick;
            gallery.Changed += Update;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaysChanged;
            Closed += delegate
            {
                closing = true;
                WindowsNative.RemoveMouseHook(mouseHook);
                scrollHover.Stop(); trackingScrollHover = false;
                Preview.Dispose();
                foreach (var pin in pins.Values.ToArray()) pin.Close();
                pointer.Stop();
                StopRendering();
                foreach (var flight in flights.ToArray()) flight.Close();
                gallery.Changed -= Update;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
                foreach (var card in cards.Values) card.Dispose();
            };
            Update();
        }

        IntPtr Messages(IntPtr window, int message, IntPtr w, IntPtr l, ref bool handled)
        {
            if (message == 0x21) { handled = true; return new IntPtr(3); }
            if (message == 0x02E0) Dispatcher.BeginInvoke(new Action(delegate { if (Display != null) MoveToDisplay(); }));
            return IntPtr.Zero;
        }

        void DisplaysChanged(object sender, EventArgs args)
        {
            Dispatcher.BeginInvoke(new Action(MoveToDisplay));
        }

        Forms.Screen SelectedDisplay()
        {
            string device = gallery.Store.State.DisplayDevice;
            if (string.IsNullOrEmpty(device)) return WindowsNative.ScreenAtPointer();
            var screens = WindowsNative.Screens;
            return Array.Find(screens, screen => screen.DeviceName == device) ?? Array.Find(screens, screen => screen.Primary);
        }

        void MoveToDisplay()
        {
            scrollHover.Stop(); trackingScrollHover = false;
            Preview.Dismiss();
            var screen = SelectedDisplay();
            Display = screen;
            double scale = WindowsNative.MonitorScale(screen);
            Height = 220;
            Width = screen.WorkingArea.Width / scale;
            if (Handle != IntPtr.Zero)
                WindowsNative.SetWindowPos(Handle, new IntPtr(-1), screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Width, (int)Math.Round(220 * scale), 0x10);
            LayoutCards();
        }

        internal void SetDisplay(string device)
        {
            string previous = gallery.Store.State.DisplayDevice;
            gallery.Store.State.DisplayDevice = device;
            try { gallery.Store.Save(); }
            catch { gallery.Store.State.DisplayDevice = previous; throw; }
            MoveToDisplay();
            Reveal(true, true, 0);
        }

        public void Update()
        {
            Preview.Dismiss();
            UpdateAccessories();
            var retained = new HashSet<string>(gallery.Items.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
            bool offsetsChanged = false;
            foreach (string gone in cards.Keys.Where(k => !retained.Contains(k)).ToArray())
            {
                cards[gone].Dispose();
                if (manualOffsets.Remove(gone)) offsetsChanged = true;
                settled.Remove(gone);
                PinnedImageWindow pin;
                if (pins.TryGetValue(gone, out pin)) pin.Close();
                content.Children.Remove(cards[gone]);
                cards.Remove(gone);
            }
            // 图被取下后，把它的位置坐标一并从存档里清掉，否则下次挂回会跳到旧位置。
            if (offsetsChanged) PersistCardOffsets();
            foreach (var item in gallery.Items)
            {
                ShotCard card;
                if (!cards.TryGetValue(item.Path, out card))
                {
                    card = new ShotCard(item, act, gallery, HangingItems);
                    var hovered = card;
                    card.Surface.MouseEnter += delegate { Preview.Enter(hovered); };
                    card.Surface.MouseLeave += delegate { Preview.Leave(hovered); };
                    card.PreviewMouseDown += delegate { scrollHover.Stop(); trackingScrollHover = false; Preview.Dismiss(); };
                    card.Grip.MouseEnter += delegate { Preview.Dismiss(); };
                    card.ApplyTheme(Appearance, gallery.Items.IndexOf(item));
                    var mounted = card;
                    card.Grip.PreviewMouseLeftButtonDown += delegate { BeginCardDrag(mounted); };
                    card.Grip.PreviewMouseMove += CardDragMove;
                    // 抬起瞬间自己判断是不是拖动过：不依赖 Button.Click——拖动过程中要 CaptureMouse/
                    // ReleaseMouseCapture，按钮内部的 Click 判定（依赖 IsMouseCaptured）并不可靠。
                    card.Grip.PreviewMouseLeftButtonUp += delegate { EndCardDrag(mounted); GripClick(mounted); };
                    cards.Add(item.Path, card);
                    content.Children.Add(card);
                }
                bool changed = card.UpdateImage();
                PinnedImageWindow pinnedImage;
                if (changed && pins.TryGetValue(item.Path, out pinnedImage)) pinnedImage.UpdateImage();
            }
            hint.Visibility = gallery.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            helpTitle.Text = gallery.Store.State.AutoHang ? "截图会挂在这里" : "从图片篮子里挑几张挂上";
            helpDetail.Text = gallery.Store.State.AutoHang ? "Win + PrtScn · 或开启剪贴板截图收集" : "截图会先收好，需要时再挂上绳子";
            openBasket.Visibility = gallery.Store.State.AutoHang ? Visibility.Collapsed : Visibility.Visible;
            int pending = gallery.Collected.Count(i => !gallery.Contains(i.Path));
            if (pending > pendingPictures && Revealed && !flights.Any(flight => flight.Collecting || flight.Falling)) BasketReceived();
            pendingPictures = pending; basketCount.Text = pending.ToString();
            System.Windows.Automation.AutomationProperties.SetItemStatus(BasketButton, basketCount.Text + " 张待挂");
            enable.Visibility = gallery.Store.State.CollectClipboard ? Visibility.Collapsed : Visibility.Visible;
            LayoutCards();
            TrimToCapacity();
        }

        // 一屏放得下的夹子数（首张占 CardWidth，其后每张占 CardStep）。
        int FitCount { get { return Math.Max(1, (int)Math.Floor((PhotoWidth - 16 - CardWidth) / CardStep)) + 1; } }

        // 绳子总量上限：两屏（滚动一次就能看完）与 24 张取较大者——宽屏上两屏偏少，
        // 24 张约为三屏多一点的滚动量，既够用又不会滚得太远。
        internal int RopeCapacity { get { return Math.Max(FitCount * 2, 24); } }

        bool trimming;

        // 绳子还能再挂几张（容量减去当前挂着的数量）；图片篮子据此限制一次挂上的数量。
        internal int RoomForHanging()
        {
            int hanging = 0;
            foreach (var card in cards.Values) if (!card.Detached && !card.Flying) hanging++;
            return Math.Max(0, RopeCapacity - hanging);
        }

        // 超出容量时把最早的几张走「取下放篮子」退回（带飞入篮子的动画，记录与文件都保留）。
        // act 会触发 Gallery.Changed → Update 重入，用 trimming 防止递归。
        void TrimToCapacity()
        {
            if (trimming) return;
            var candidates = new List<CaptureItem>();
            foreach (var item in gallery.Items)
            {
                ShotCard card;
                if (!cards.TryGetValue(item.Path, out card) || card.Detached || card.Flying || card.Releasing) continue;
                candidates.Add(item);
            }
            int excess = candidates.Count - RopeCapacity;
            if (excess <= 0) return;
            trimming = true;
            try { for (int i = 0; i < excess; i++) act(ShotAction.Remove, candidates[i]); }
            finally { trimming = false; }
        }

        void LayoutCards()
        {
            double available = PhotoWidth;
            var hanging = gallery.Items.Where(item => !cards[item.Path].Detached).ToArray();
            double contentWidth = Math.Max(available, hanging.Length * 172 + 32);
            foreach (var item in hanging)
            {
                double saved;
                if (manualOffsets.TryGetValue(item.Path, out saved)) contentWidth = Math.Max(contentWidth, saved + CardWidth + 16);
                else if (settled.Contains(item.Path)) contentWidth = Math.Max(contentWidth, cards[item.Path].MountX + CardWidth + 16);
            }
            content.Width = contentWidth;
            content.Height = 220;
            scrollMotion.Target = Math.Max(0, Math.Min(ScrollLimit, scrollMotion.Target));
            if (ScrollLimit == 0) { scrollMotion.Snap(0); viewport.ScrollToHorizontalOffset(0); lastScrollOffset = 0; }
            else if (scrollMotion.Position > ScrollLimit) { scrollMotion.Position = ScrollLimit; scrollMotion.Velocity = 0; }
            // 绳子下垂度只在首次排位时确定：之后新挂图片不再改变绳形，避免原有夹子被顶得上下移动。
            if (settled.Count == 0) ropeMotion.Target = 18 + Math.Min(7, hanging.Length) * 1.2;
            if (!Revealed) ropeMotion.Snap(ropeMotion.Target);
            DrawRope();
            Canvas.SetLeft(hint, (content.Width - hint.Width) / 2);
            Canvas.SetTop(hint, 56);
            // 正在收起（Detached）但还在淡出的卡片仍然可见，也要挡住排位，否则新图会压上去。
            var placed = new List<double>();
            var blocked = new List<double>();
            foreach (var item in gallery.Items)
            {
                ShotCard leaving;
                if (cards.TryGetValue(item.Path, out leaving) && leaving.Detached) blocked.Add(leaving.MountX);
            }
            bool initial = settled.Count == 0; // 启动首次排位仍居中；之后增量挂图才走"最左空位"策略。
            for (int i = 0; i < hanging.Length; i++)
            {
                ShotCard card = cards[hanging[i].Path];
                double x;
                double saved;
                if (manualOffsets.TryGetValue(hanging[i].Path, out saved))
                {
                    // 手动位置原样使用：允许与其他卡片重叠，保证自由摆放的可控性；
                    // 排位避让只针对自动放置的卡片。
                    x = saved;
                }
                else if (settled.Contains(hanging[i].Path))
                {
                    // 已排过位的卡片保持原位：新图挂上来时绝不移动原有夹子。
                    x = card.MountX;
                }
                else if (initial)
                {
                    // 首次排位居中，但同样要避开手动位置，否则启动时就会叠在一起。
                    x = (contentWidth - hanging.Length * CardStep) / 2 + i * CardStep;
                    double moved = NearestFree(blocked, x);
                    if (moved >= 0) x = moved;
                    else { x = content.Width; content.Width = x + CardWidth + 16; }
                    settled.Add(hanging[i].Path);
                }
                else
                {
                    // 新卡片：接在最右一张已挂卡片的后面，保持"一张接一张挂上去"的顺序；
                    // 最后一张已经贴到绳端时，回到绳子最左边的空位（相当于换到下一屏继续挂）。
                    double lastX = placed.Count > 0 ? placed.Max() : -1;
                    double candidate = lastX >= 0 ? lastX + CardStep : 0;
                    if (lastX >= 0 && candidate > content.Width - CardWidth - 16) candidate = 0;
                    x = NearestFree(blocked, candidate);
                    if (x < 0)
                    {
                        x = content.Width;
                        content.Width = x + CardWidth + 16;
                    }
                    settled.Add(hanging[i].Path);
                }
                placed.Add(x); blocked.Add(x);
                card.Place(x, !Revealed);
                card.Frame(0, ropeMotion.Position, LineWidth, viewport.HorizontalOffset, viewport.ActualWidth);
            }
            UpdateScrollHint();
        }

        // 内容超出一屏时显示两端箭头：右端还有内容显右箭头，已滚过去则显左箭头。
        void UpdateScrollHint()
        {
            double limit = ScrollLimit;
            bool overflow = limit > 2;
            scrollRight.Visibility = overflow && viewport.HorizontalOffset < limit - 2 ? Visibility.Visible : Visibility.Collapsed;
            scrollLeft.Visibility = overflow && viewport.HorizontalOffset > 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        // 箭头明暗三档：默认 0.25 几乎隐身；指针在绳带区域内 0.6；悬停箭头本体 0.9 并轻微放大。
        void UpdateArrowGlow()
        {
            if (!Revealed || (scrollRight.Visibility != Visibility.Visible && scrollLeft.Visibility != Visibility.Visible)) return;
            double opacity = 0.25, scale = 1;
            if (scrollRight.IsMouseOver || scrollLeft.IsMouseOver) { opacity = 0.9; scale = 1.15; }
            else
            {
                try
                {
                    var cursor = WindowsNative.Cursor();
                    Point local = PointFromScreen(new Point(cursor.X, cursor.Y));
                    var area = viewport.TransformToAncestor(this).TransformBounds(new Rect(viewport.RenderSize));
                    if (area.Contains(local)) opacity = 0.6;
                }
                catch { }
            }
            SetArrow(scrollLeft, scrollLeftScale, opacity, scale);
            SetArrow(scrollRight, scrollRightScale, opacity, scale);
        }

        static void SetArrow(Border arrow, ScaleTransform scale, double opacity, double zoom)
        {
            if (Math.Abs(arrow.Opacity - opacity) > 0.01) arrow.Opacity = opacity;
            if (Math.Abs(scale.ScaleX - zoom) > 0.01) { scale.ScaleX = zoom; scale.ScaleY = zoom; }
        }

        // 从 candidate 向左右就近搜索不与已占位置重叠的空位（步长 CardStep）；找不到返回 -1。
        double NearestFree(List<double> blocked, double candidate)
        {
            if (SlotFree(blocked, candidate)) return candidate;
            double maxX = content.Width - CardWidth - 16;
            for (int step = 1; ; step++)
            {
                double right = candidate + step * CardStep, left = candidate - step * CardStep;
                if (right <= maxX && SlotFree(blocked, right)) return right;
                if (left >= 0 && SlotFree(blocked, left)) return left;
                if (right > maxX && left < 0) return -1;
            }
        }

        // 排位辅助：候选位置与所有已放置卡片之间至少空出一个步长。
        bool SlotFree(List<double> occupied, double candidate)
        {
            foreach (double position in occupied)
                if (Math.Abs(position - candidate) < CardStep) return false;
            return true;
        }

        // 夹子可以沿着绳子拖动：拖到哪儿就停在哪儿，其他图片保持原位。
        // 这样图多的时候可以把挡住内容的几张挪开，而不必改变绳子的长度和样式。
        void BeginCardDrag(ShotCard card)
        {
            var cursor = WindowsNative.Cursor();
            Point local = content.PointFromScreen(new Point(cursor.X, cursor.Y));
            dragCard = card;
            dragMoved = false;
            // 上一次拖动留下的抑制标记必须在「按下」时清掉：拖完之后 Click 事件不会再到达该卡片，
            // 标记会一直挂着，把用户下一次真正的单击（钉到桌面）也吞掉。
            suppressGripClick = null;
            dragGrab = local.X - card.MountX;
            dipCenter = card.MountX + CardWidth / 2;
            StartRendering();
            peekUntil = DateTime.UtcNow.AddMinutes(5);
            Preview.Dismiss();
            scrollHover.Stop(); trackingScrollHover = false;
            Panel.SetZIndex(card, 1000);
            card.Grip.CaptureMouse();
        }

        void CardDragMove(object sender, MouseEventArgs e)
        {
            if (dragCard == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;
            ApplyDragPosition();
            UpdateDragEdgeScroll();
        }

        // 把夹子放到光标位置：允许两端各越出视口 72px（约半个夹子留在屏外），但左侧不出内容起点
        // （否则滚回起点时必然切一半）。想挪更远的绳段由 UpdateDragEdgeScroll 自动滚动/加长绳子。
        void ApplyDragPosition()
        {
            if (dragCard == null) return;
            var cursor = WindowsNative.Cursor();
            Point local = content.PointFromScreen(new Point(cursor.X, cursor.Y));
            double viewLeft = viewport.HorizontalOffset;
            double viewRight = Math.Min(content.Width, viewLeft + viewport.ViewportWidth);
            double wanted = Math.Max(Math.Max(8, viewLeft - 72), Math.Min(local.X - dragGrab, Math.Max(viewLeft - 72, viewRight - CardWidth + 72)));
            // 超过 6px 才算真的拖动：鼠标按下时的轻微手抖（1~2px 很常见）不应把「单击夹子钉到桌面」吞掉。
            if (Math.Abs(wanted - dragCard.MountX) > 6) dragMoved = true;
            dipCenter = wanted + CardWidth / 2;
            dragCard.Place(wanted, true);
            dragCard.Frame(0, ropeMotion.Position, LineWidth, viewport.HorizontalOffset, viewport.ActualWidth);
            StartRendering();
        }

        // 光标拖到视口左右 44px 内就持续滚动绳子，离开边缘或松手即停。
        // 每帧按时间推进（速度 760px/秒），弹簧继续跟随目标，因此滚动和夹子位移都是连续的。
        const double DragScrollSpeed = 760;
        void UpdateDragEdgeScroll()
        {
            if (dragCard == null) { dragScrollDirection = 0; return; }
            var cursor = WindowsNative.Cursor();
            Point local = content.PointFromScreen(new Point(cursor.X, cursor.Y));
            double relative = local.X - viewport.HorizontalOffset;
            double width = viewport.ViewportWidth;
            if (relative < 44) dragScrollDirection = -1;
            else if (relative > width - 44) dragScrollDirection = 1;
            else dragScrollDirection = 0;
        }

        void EndCardDrag(ShotCard card)
        {
            if (dragCard != card) return;
            dragScrollDirection = 0; suppressSnap = true;
            Panel.SetZIndex(card, 0);
            card.Grip.ReleaseMouseCapture();
            dragCard = null;
            if (!dragMoved) return;
            suppressGripClick = card;
            StartRendering();
            manualOffsets[card.Item.Path] = card.MountX;
            PersistCardOffsets();
            LayoutCards();
        }

        // 单击夹子 = 把这张图贴到桌面。拖动刚结束时会先由 EndCardDrag 打上抑制标记（这里消费掉），
        // 避免「拖完夹子顺手把图贴出去」。标记会在每次按下时重置，不会残留到下一次单击。
        void GripClick(ShotCard card)
        {
            if (suppressGripClick == card) { suppressGripClick = null; return; }
            var ignored = ReleasePhoto(card);
        }

        void PersistCardOffsets()
        {
            var state = gallery.Store.State;
            var previous = state.CardOffsets;
            state.CardOffsets = new Dictionary<string, double>(manualOffsets, StringComparer.OrdinalIgnoreCase);
            try { gallery.Store.Save(); }
            catch { state.CardOffsets = previous; }
        }

        internal CaptureItem[] HangingItems()
        {
            return gallery.Items.Where(item => cards.ContainsKey(item.Path) && !cards[item.Path].Detached).ToArray();
        }

        // 该截图是否正处于"贴到桌面"状态（卡片已从绳子取下，钉在桌面上）。
        internal bool IsPinned(string path) { return pins.ContainsKey(path); }

        public void ApplyTheme(LineTheme theme)
        {
            Appearance = theme;
            RopeLine.Apply(rope, theme);
            basketImage.Source = PictureBasket.Source(theme);
            basketImage.Height = 112 * basketImage.Source.Height / basketImage.Source.Width;
            envelopeImage.Apply(theme); textClip.Apply(theme, 2);
            // 篮子和信封是浅色的实物图案，深色底上太扎眼，压一点亮度让它融进背景。
            double accessory = LightTheme.Dark ? 0.72 : 1.0;
            basketImage.Opacity = accessory; envelopeImage.Opacity = accessory;
            Point hook = PictureBasket.Hook(theme); basketTurn.CenterX = hook.X; basketTurn.CenterY = hook.Y;
            drawnWidth = -1; DrawRope();
            for (int i = 0; i < gallery.Items.Count; i++) cards[gallery.Items[i].Path].ApplyTheme(theme, i);
            foreach (var flight in flights) flight.ApplyTheme(theme);
            foreach (var entry in pins) entry.Value.ApplyTheme(theme, cards[entry.Key].ColorIndex);
        }

        void DrawRope()
        {
            double dipX = dipAmount > 0.02 ? dipCenter - viewport.HorizontalOffset : double.NaN;
            if (Math.Abs(drawnWidth - LineWidth) < 0.01 && Math.Abs(drawnSag - ropeMotion.Position) < 0.01
                && Math.Abs(drawnDip - dipAmount) < 0.02 && Math.Abs(drawnDipCenter - dipX) < 0.4) return;
            drawnWidth = LineWidth;
            drawnSag = ropeMotion.Position;
            drawnDip = dipAmount;
            drawnDipCenter = dipX;
            var geometry = new PathGeometry();
            var shape = new PathFigure { StartPoint = new Point(0, 18) };
            if (drawnDip <= 0.02 || double.IsNaN(dipX))
            {
                shape.Segments.Add(new QuadraticBezierSegment(new Point(drawnWidth / 2, 18 + 2 * drawnSag), new Point(drawnWidth, 18), true));
            }
            else
            {
                // 拖动夹子时，绳索在被按住的位置被压出一个坑：沿曲线采样后叠加一个高斯凹陷。
                var poly = new PolyLineSegment();
                const int steps = 56;
                for (int i = 1; i <= steps; i++)
                {
                    double x = drawnWidth * i / steps;
                    double along = x / Math.Max(1, drawnWidth);
                    double y = 18 + 4 * drawnSag * along * (1 - along);
                    double delta = x - dipX;
                    y += drawnDip * Math.Exp(-(delta * delta) / (2 * DipRadius * DipRadius));
                    poly.Points.Add(new Point(x, y));
                }
                shape.Segments.Add(poly);
            }
            geometry.Figures.Add(shape);
            rope.Data = geometry;
            Point hook = PictureBasket.Hook(Appearance);
            double fraction = (PhotoWidth + 14 + hook.X) / LineWidth;
            double offset = 4 * drawnSag * fraction * (1 - fraction);
            double basketTop = 18 + offset - hook.Y;
            Canvas.SetTop(BasketButton, basketTop);
            double textFraction = (PhotoWidth + 204) / LineWidth;
            double textTop = 18 + 4 * drawnSag * textFraction * (1 - textFraction) - 7;
            Canvas.SetTop(TextButton, textTop);
            double countTop = basketTop + basketImage.Height + 6 - textTop;
            textCounter.Margin = new Thickness(0, countTop, 0, 0);
            envelopeImage.Height = Math.Max(24, Math.Min(86, countTop - 26));
        }

        internal void UpdateTexts()
        {
            UpdateAccessories();
            textCount.Text = gallery.Store.State.Texts.Count.ToString();
            AutomationProperties.SetItemStatus(TextButton, textCount.Text + " 条文字");
            if (Display != null) LayoutCards();
        }

        void UpdateAccessories()
        {
            // 篮子和信封常驻挂在绳上，不再跟随收集开关隐藏；信封在最右，篮子在它左边。
            BasketButton.Visibility = basketHolder.Visibility = Visibility.Visible;
            TextButton.Visibility = textHolder.Visibility = Visibility.Visible;
            textHolder.Margin = new Thickness(0, 0, 24, 0);
            basketHolder.Margin = new Thickness(0, 0, 136, 0);
            viewport.Margin = new Thickness(24, 0, 24 + AccessoryWidth, 0);
            drawnWidth = -1;
        }

        void UpdateEdgeMask()
        {
            double fade = Math.Min(0.08, 18 / PhotoWidth);
            edgeMask.GradientStops[0].Color = viewport.HorizontalOffset > 1 ? Colors.Transparent : Colors.White;
            edgeMask.GradientStops[1].Offset = fade;
            edgeMask.GradientStops[2].Offset = 1 - fade;
            edgeMask.GradientStops[3].Color = viewport.HorizontalOffset < ScrollLimit - 1 ? Colors.Transparent : Colors.White;
            // 遮罩只挂在绳子上：绳子在窗口两端优雅渐隐，而卡片内部的夹子永远不淡出（由逐卡淡出接管）。
            line.OpacityMask = ScrollLimit > 0 ? edgeMask : null;
        }

        public void Toggle()
        {
            viewingName = null;
            viewingWindow = IntPtr.Zero;
            if (Revealed) Reveal(false, false, 0);
            else { MoveToDisplay(); Reveal(true, true, 0); }
        }

        public void FollowImageViewer(string path)
        {
            viewingName = System.IO.Path.GetFileName(path);
            viewingWindow = IntPtr.Zero;
            viewerSearchUntil = DateTime.UtcNow.AddSeconds(5);
            awaySince = DateTime.MaxValue;
        }

        bool ViewingImage(DateTime now)
        {
            if (viewingName == null) return false;
            IntPtr foreground = WindowsNative.GetForegroundWindow();
            if (viewingWindow == IntPtr.Zero)
            {
                if (foreground != Handle && WindowsNative.WindowTitle(foreground).IndexOf(viewingName, StringComparison.OrdinalIgnoreCase) >= 0)
                    viewingWindow = foreground;
                else if (now < viewerSearchUntil) return true;
                else { viewingName = null; return false; }
            }
            if (!WindowsNative.IsWindowVisible(viewingWindow))
            {
                viewingName = null;
                viewingWindow = IntPtr.Zero;
                Reveal(true, false, 0);
                return true;
            }
            return foreground == viewingWindow;
        }

        public void ShowNewCapture(CaptureItem item, Point? origin)
        {
            MoveToDisplay();
            Reveal(true, false, 2);
            // 新卡片已在可视区内就不滚动，绳子纹丝不动；只有落点在视野外时才做最小滚动把它带进来，
            // 不再居中跳转——避免"新图一挂上去，原有夹子看起来整体位移"。
            ShotCard target;
            if (cards.TryGetValue(item.Path, out target))
            {
                double viewLeft = Math.Max(0, Math.Min(ScrollLimit, scrollMotion.Target));
                double viewRight = viewLeft + viewport.ViewportWidth;
                if (target.MountX < viewLeft + 12)
                    scrollMotion.Target = Math.Max(0, target.MountX - 12);
                else if (target.MountX + CardWidth > viewRight - 12)
                    scrollMotion.Target = Math.Max(0, Math.Min(ScrollLimit, target.MountX + CardWidth + 12 - viewport.ViewportWidth));
            }
            // 卡片还没建出来时不要把视野甩到最右端（挂图后"突然跳到最右边"的来源之一），保持当前视野即可。
            else scrollMotion.Target = Math.Max(0, Math.Min(ScrollLimit, scrollMotion.Target));
            ropeMotion.Velocity += 8;
            if (!Revealed || !origin.HasValue || flights.Count(f => !f.Falling && !f.Collecting) >= 2) return;
            ShotCard card = cards[item.Path];
            if (card.Detached) return;
            card.BeginFlight();
            var bounds = Display.Bounds;
            double scale = Math.Min(1, Math.Min(bounds.Width * 0.7 / item.Image.PixelWidth, bounds.Height * 0.7 / item.Image.PixelHeight));
            double width = item.Image.PixelWidth * scale, height = item.Image.PixelHeight * scale;
            double left = Math.Max(bounds.Left, Math.Min(bounds.Right - width, origin.Value.X - width));
            double top = Math.Max(bounds.Top, Math.Min(bounds.Bottom - height, origin.Value.Y - height));
            Rect start = new Rect(left, top, width, height);
            if (item.Image.PixelWidth == bounds.Width && item.Image.PixelHeight == bounds.Height)
                start = new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            PlayFlight(new ShotFlight(Display, card, start, false, card.Land));
        }

        public void ShowDiscard(CaptureItem item)
        {
            ShotCard card;
            if (!Revealed || !cards.TryGetValue(item.Path, out card) || card.Flying) return;
            PlayFlight(new ShotFlight(Display, card, card.PhotoRect(), true, delegate { }));
        }

        public void ShowCollect(CaptureItem item)
        {
            if (!gallery.Store.State.CollectClipboard) return;
            ShotCard card;
            if (!Revealed || !cards.TryGetValue(item.Path, out card) || card.Flying) return;
            peekUntil = DateTime.UtcNow.AddSeconds(2);
            PlayFlight(new ShotFlight(Display, card, card.PhotoRect(), false, BasketReceived, null, delegate
            {
                Point mouth = basketImage.PointToScreen(PictureBasket.Mouth(Appearance));
                double scale = WindowsNative.MonitorScale(Display);
                return new Rect(mouth.X - 28 * scale, mouth.Y, 56 * scale, 16 * scale);
            }));
        }

        void BasketReceived()
        {
            if (closing || !Revealed || !gallery.Store.State.CollectClipboard) return;
            basketMotion.Velocity = Math.Max(-48, basketMotion.Velocity - 32);
            basketCounterMotion.Velocity = Math.Min(2, basketCounterMotion.Velocity + 1.2);
            StartRendering();
        }

        public void ShowEditedImage(string path, Rect origin, BitmapSource picture)
        {
            ShotCard card;
            if (closing || !cards.TryGetValue(path, out card) || card.Detached || card.Flying) return;
            MoveToDisplay();
            Reveal(true, false, 2);
            if (!Revealed) return;
            card.BeginFlight();
            LayoutCards();
            viewport.UpdateLayout();
            scrollMotion.Target = Math.Max(0, Math.Min(content.Width - viewport.ViewportWidth, card.MountX + 82 - viewport.ViewportWidth / 2));
            PlayFlight(new ShotFlight(Display, card, origin, false, delegate { card.Land(); ropeMotion.Velocity += 12; }, picture));
        }

        void PlayFlight(ShotFlight flight)
        {
            flights.Add(flight);
            flight.Closed += delegate { flights.Remove(flight); };
            flight.Show();
        }

        async Task ReleasePhoto(ShotCard card)
        {
            if (card.Releasing || card.Flying || card.Detached) return;
            Rect origin = card.PhotoRect();
            card.OpenClip();
            awaySince = DateTime.MaxValue;
            try
            {
                var picture = Task.Run(() => PinnedImageWindow.ReadPicture(card.Item.Path));
                await Task.Delay(40);
                var image = await picture;
                if (closing || card.IsDisposed) return;
                var window = new PinnedImageWindow(card.Item, origin, ((RotateTransform)card.RenderTransform).Angle, image, Appearance, card.ColorIndex);
                pins.Add(card.Item.Path, window);
                window.Closed += delegate
                {
                    pins.Remove(card.Item.Path);
                    if (closing || card.IsDisposed) return;
                    card.Detached = false;
                    card.ResetClip();
                    card.BeginFlight();
                    var bounds = window.ReturnBounds;
                    MoveToDisplay();
                    Reveal(true, false, 3);
                    LayoutCards();
                    viewport.UpdateLayout();
                    scrollMotion.Target = Math.Max(0, Math.Min(content.Width - viewport.ViewportWidth, card.MountX + 82 - viewport.ViewportWidth / 2));
                    PlayFlight(new ShotFlight(Display, card, bounds, false, card.Land, window.Picture));
                };
                card.DetachPhoto();
                ropeMotion.Velocity -= 24;
                LayoutCards();
                window.Show();
            }
            catch (Exception error) { gallery.Store.Log(error); card.ResetClip(); }
        }

        public void Reveal(bool shown, bool pin, double seconds)
        {
            pinned = pin;
            peekUntil = DateTime.UtcNow.AddSeconds(seconds);
            awaySince = DateTime.MaxValue;
            if (shown && WindowsNative.FullScreen(Display, Handle)) return;
            if (shown == Revealed) return;
            if (shown) avoidingCaption = false;
            Revealed = shown;
            revealMotion.Target = shown ? 0 : HiddenY;
            hideFrom = revealMotion.Position;
            hideTime = 0;
            StartRendering();
            if (!shown)
            {
                scrollHover.Stop(); trackingScrollHover = false;
                Preview.Dismiss();
                if (RopeHidden != null) RopeHidden();
                foreach (var flight in flights.ToArray()) flight.Close();
                basketMotion.Snap(0); basketTurn.Angle = 0;
                basketCounterMotion.Snap(1); basketCounterScale.ScaleX = basketCounterScale.ScaleY = 1;
                envelopeImage.Reset();
                WindowsNative.PassMouse(Handle, true);
            }
            else
            {
                if (nextGust == default(DateTime)) nextGust = DateTime.UtcNow.AddSeconds(7 + random.NextDouble() * 17);
                foreach (var card in cards.Values) card.Nudge(1.2);
            }
        }

        void StartRendering()
        {
            if (rendering) return;
            rendering = true;
            lastFrame = clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += RenderFrame;
        }

        void StopRendering()
        {
            if (!rendering) return;
            rendering = false;
            CompositionTarget.Rendering -= RenderFrame;
        }

        bool NeedsRendering
        {
            get
            {
                return !revealMotion.Settled || !ropeMotion.Settled || !scrollMotion.Settled || !basketMotion.Settled
                    || !basketCounterMotion.Settled || (gallery.Store.State.CollectTextClipboard && envelopeImage.Animating)
                    || dipAmount > 0.05 || (dragCard != null && dipAmount < DipDepth - 0.05)
                    || cards.Values.Any(card => card.Animating);
            }
        }

        void RenderFrame(object sender, EventArgs args)
        {
            double now = clock.Elapsed.TotalSeconds;
            double elapsed = now - lastFrame;
            lastFrame = now;
            if (Revealed) revealMotion.Step(elapsed);
            else
            {
                hideTime += elapsed;
                double t = Math.Min(1, hideTime / 0.22);
                double remaining = 1 - t;
                revealMotion.Position = hideFrom + (HiddenY - hideFrom) * (1 - remaining * remaining * remaining);
                revealMotion.Velocity = 3 * (HiddenY - hideFrom) * remaining * remaining / 0.22;
                if (t == 1) revealMotion.Snap(HiddenY);
            }
            slide.Y = revealMotion.Position;
            if (!Revealed)
            {
                if (hideTime >= 0.22) StopRendering();
                return;
            }
            ropeMotion.Step(elapsed);
            double dipTarget = dragCard != null ? DipDepth : 0;
            if (Math.Abs(dipTarget - dipAmount) > 0.05) dipAmount += (dipTarget - dipAmount) * Math.Min(1, elapsed * 14);
            else dipAmount = dipTarget;
            foreach (var item in cards.Values) { item.DipCenter = dipCenter; item.DipAmount = dipAmount; }
            DrawRope();
            basketMotion.Step(elapsed); basketTurn.Angle = basketMotion.Position;
            basketCounterMotion.Step(elapsed); basketCounterScale.ScaleX = basketCounterScale.ScaleY = basketCounterMotion.Position;
            if (gallery.Store.State.CollectTextClipboard) envelopeImage.Frame(elapsed);
            // 拖夹子贴到视口边缘时按帧推进滚动（到绳首/绳尾即停），夹子位置同帧跟随光标，滚动连续不跳格。
            if (dragCard != null)
            {
                UpdateDragEdgeScroll();
                if (dragScrollDirection != 0)
                {
                    double limit = ScrollLimit;
                    double target = scrollMotion.Target + dragScrollDirection * DragScrollSpeed * elapsed;
                    scrollMotion.Target = Math.Max(0, Math.Min(limit, target));
                    if (dragScrollDirection < 0 && scrollMotion.Target <= 0) dragScrollDirection = 0;
                    if (dragScrollDirection > 0 && scrollMotion.Target >= limit) dragScrollDirection = 0;
                    ApplyDragPosition();
                }
            }
            scrollMotion.Target = Math.Max(0, Math.Min(scrollMotion.Target, ScrollLimit));
            scrollMotion.Step(elapsed);
            if (scrollMotion.Position < 0 || scrollMotion.Position > ScrollLimit)
            {
                scrollMotion.Position = Math.Max(0, Math.Min(scrollMotion.Position, ScrollLimit));
                scrollMotion.Velocity = 0;
            }
            // 停稳后吸附：把停点推到不切卡的位置，避免夹子半个身子卡在视口边缘、想抓它还得再滚一下。
            if (dragCard == null && !suppressSnap && Math.Abs(scrollMotion.Velocity) < 2 && Math.Abs(scrollMotion.Position - scrollMotion.Target) < 0.6)
            {
                double snapped = SnapOffset(scrollMotion.Target);
                if (Math.Abs(snapped - scrollMotion.Target) > 0.5)
                {
                    scrollMotion.Target = snapped;
                    scrollMotion.Step(elapsed);
                }
            }
            viewport.ScrollToHorizontalOffset(scrollMotion.Position);
            double scrollVelocity = Revealed && elapsed > 0 ? (viewport.HorizontalOffset - lastScrollOffset) / elapsed : 0;
            lastScrollOffset = viewport.HorizontalOffset;
            foreach (var card in cards.Values)
            {
                card.ScrollWith(scrollVelocity);
                card.Frame(elapsed, ropeMotion.Position, LineWidth, viewport.HorizontalOffset, viewport.ActualWidth);
            }
            if (!NeedsRendering) StopRendering();
        }

        // 滚动停点的吸附：静止时，落在视口边缘的卡片至少要露出 needed 像素（夹子完整可见、抓得住）。
        // 卡片间距 172px 比卡宽 164 只多 8px，“完全不许切”几乎无解——原来那版规则太严，会把停点顶到
        // 绳子的两端（看起来像整页跳）。现在改成就近吸附：只在当前停点附近（reach 以内）找候选点，
        // 取最干净的一个；附近找不到就原样不动，绝不来回跳页。
        const double ClipVisibleNeeded = 100;
        const double SnapReach = 220;

        double SnapOffset(double target)
        {
            double limit = ScrollLimit, viewW = viewport.ViewportWidth;
            if (limit <= 2 || viewW <= 0) return target;
            var candidates = new List<double> { target, 0, limit };
            foreach (var card in cards.Values)
            {
                if (card.Flying || card.Detached) continue;
                double x = card.MountX;
                candidates.Add(x);                                       // 整卡贴在视口左缘
                candidates.Add(x + CardWidth - ClipVisibleNeeded);       // 左缘只露 needed
                candidates.Add(x + CardWidth);                           // 整卡退到左缘外
                candidates.Add(x - viewW);                               // 整卡贴在视口右缘
                candidates.Add(x + ClipVisibleNeeded - viewW);           // 右缘只露 needed
                candidates.Add(x + CardWidth - viewW);                   // 整卡退到右缘外
            }
            double best = target, bestDistance = double.MaxValue;
            foreach (var candidate in candidates)
            {
                double o = Math.Max(0, Math.Min(limit, candidate));
                double distance = Math.Abs(o - target);
                if (distance > SnapReach || distance >= bestDistance || !EdgeClean(o)) continue;
                bestDistance = distance; best = o;
            }
            return best;
        }

        // 停点是否干净：没有卡片被视口边缘切掉一大半（露出不足 needed 像素）。
        bool EdgeClean(double o)
        {
            double viewW = viewport.ViewportWidth;
            foreach (var card in cards.Values)
            {
                if (card.Flying || card.Detached) continue;
                double x = card.MountX, right = o + viewW;
                if (o > x && o < x + CardWidth && x + CardWidth - o < ClipVisibleNeeded - 0.5) return false;
                if (right > x && right < x + CardWidth && right - x < ClipVisibleNeeded - 0.5) return false;
            }
            return true;
        }

        // WPF 层的滚轮处理（窗口级）：命中窗口内任何可命中元素时都会走到这里。
        void ScrollByWheel(double delta)
        {
            Preview.Dismiss();
            trackingScrollHover = true; scrollHover.Stop(); scrollHover.Start();
            suppressSnap = false;
            double target = Math.Max(0, Math.Min(ScrollLimit, scrollMotion.Target - delta));
            if (Math.Abs(target - scrollMotion.Target) > 0.01)
            {
                scrollMotion.Target = target;
                StartRendering();
                nextGust = DateTime.UtcNow.AddSeconds(7 + random.NextDouble() * 17);
            }
        }

        // 低级鼠标钩子：透明分层窗口的空白像素（alpha=0）在系统命中测试里直接穿透到下层窗口，
        // 落在绳子横带空白处的滚轮 WPF 根本收不到。这里在带内接管滚轮并驱动横向滚动：
        // 指针下面若已是本应用的窗口（卡片、篮子、预览等）则放行交给它们；
        // 否则消费滚轮事件（防止滚到绳子后面的窗口），带外与其他事件一律原样放行。
        IntPtr WatchMouse(int code, IntPtr word, IntPtr info)
        {
            if (code >= 0 && word.ToInt32() == WindowsNative.MouseWheelMessage && Revealed && !Capturing && ScrollLimit > 2)
            {
                var data = (WindowsNative.MouseHookStruct)System.Runtime.InteropServices.Marshal.PtrToStructure(info, typeof(WindowsNative.MouseHookStruct));
                WindowsNative.Rectangle bounds;
                WindowsNative.GetWindowRect(Handle, out bounds);
                bool inside = data.Point.X >= bounds.Left && data.Point.X < bounds.Right && data.Point.Y >= bounds.Top && data.Point.Y < bounds.Bottom;
                if (inside)
                {
                    IntPtr under = WindowsNative.WindowAt(data.Point);
                    if (under == IntPtr.Zero || (under != Handle && WindowsNative.WindowProcess(under) != processId))
                    {
                        double delta = (short)((data.MouseData >> 16) & 0xFFFF);
                        Dispatcher.BeginInvoke(new Action(delegate { ScrollByWheel(delta); }));
                        return new IntPtr(1);
                    }
                }
            }
            return WindowsNative.NextMouseHook(mouseHook, code, word, info);
        }

        bool OverControl(WindowsNative.Point cursor)
        {
            Point local = PointFromScreen(new Point(cursor.X, cursor.Y));
            var photoArea = viewport.TransformToAncestor(this).TransformBounds(new Rect(viewport.RenderSize));
            IEnumerable<FrameworkElement> targets = cards.Values.Where(c => !c.Flying && !c.Detached).SelectMany(c => new FrameworkElement[] { c.Surface, c.Grip });
            if (hint.IsVisible) targets = targets.Concat(new[] { hint });
            targets = targets.Concat(new[] { BasketButton, TextButton }.Where(button => button.IsVisible));
            foreach (var target in targets)
            {
                var bounds = target.TransformToAncestor(this).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
                if (target != BasketButton && target != TextButton) bounds.Intersect(photoArea);
                if (bounds.Contains(local)) return true;
            }
            // 绳子内容超出一屏时，整个绳带（视口横带全高）都可接收滚轮，不必精确悬停在卡片上；
            // 没有内容溢出时不扩展，保持空档处的点击穿透到桌面。
            if (ScrollLimit > 2 && photoArea.Contains(local)) return true;
            return false;
        }

        void Tick(object sender, EventArgs args)
        {
            if (Capturing) return;
            var cursor = WindowsNative.Cursor();
            var screen = SelectedDisplay();
            bool down = (WindowsNative.GetAsyncKeyState(1) & 0x8000) != 0;
            bool nearTop = cursor.X >= screen.Bounds.Left && cursor.X < screen.Bounds.Right
                && cursor.Y >= screen.Bounds.Top && cursor.Y <= Math.Max(screen.Bounds.Top + 2, screen.WorkingArea.Top - 1);
            var interval = TimeSpan.FromMilliseconds(Revealed || nearTop ? 30 : 100);
            if (pointer.Interval != interval) pointer.Interval = interval;
            bool over = Revealed && OverControl(cursor);
            var now = DateTime.UtcNow;
            bool viewing = ViewingImage(now);
            bool busy = cards.Values.Any(c => c.Busy);
            if ((Revealed || nearTop || avoidingCaption) && !busy && !viewing && !ChoosingAppearance && !ChoosingBasket && !ChoosingTexts
                && WindowsNative.OverCaptionButtons(screen, cursor, Handle))
            {
                edgeSince = DateTime.MaxValue; previousDown = down;
                if (Revealed) { avoidingCaption = true; Reveal(false, false, 0); }
                return;
            }
            if (avoidingCaption && !down && hideTime >= 0.22)
            {
                avoidingCaption = false;
                if (cursor.X >= Display.Bounds.Left && cursor.X < Display.Bounds.Right
                    && cursor.Y >= Display.WorkingArea.Top && cursor.Y < Display.WorkingArea.Top + 220 * WindowsNative.MonitorScale(Display))
                    Reveal(true, false, 0);
            }
            if (!nearTop) suppressEdge = false;
            if (nearTop && down && !previousDown && !over && !viewing)
            {
                suppressEdge = true;
                viewingName = null;
                viewingWindow = IntPtr.Zero;
                Reveal(false, false, 0);
            }
            previousDown = down;
            if (nearTop && !down && !suppressEdge && !WindowsNative.FullScreen(screen, Handle))
            {
                if (edgeSince == DateTime.MaxValue) edgeSince = now;
                if (now - edgeSince >= TimeSpan.FromMilliseconds(320) && (!Revealed || screen.DeviceName != Display.DeviceName))
                {
                    MoveToDisplay();
                    Reveal(true, false, 0);
                }
            }
            else edgeSince = DateTime.MaxValue;
            if (WindowsNative.FullScreen(Display, Handle)) { Reveal(false, false, 0); return; }
            // 拖夹子时鼠标会离开夹子本身，这里必须保持窗口可接收鼠标，否则拖到一半就断了。
            bool holdingCard = dragCard != null;
            WindowsNative.PassMouse(Handle, !Revealed || ((!over && !busy) && !holdingCard));
            if (!Revealed) return;
            if (!rendering && NeedsRendering) StartRendering();
            if (cards.Count > 0 && Math.Abs(scrollMotion.Velocity) < 8 && Math.Abs(scrollMotion.Target - viewport.HorizontalOffset) < 0.5 && !busy && now >= nextGust)
            {
                nextGust = now.AddSeconds(7 + random.NextDouble() * 17);
                int direction = random.Next(2) == 0 ? -1 : 1;
                foreach (var card in cards.Values) card.Breeze(random.NextDouble() * 0.35, direction * (1.6 + random.NextDouble() * 1.8));
                StartRendering();
            }
            WindowsNative.Rectangle bounds;
            WindowsNative.GetWindowRect(Handle, out bounds);
            bool inside = cursor.X >= bounds.Left && cursor.X < bounds.Right && cursor.Y >= Display.Bounds.Top && cursor.Y < bounds.Bottom;
            if (inside) pinned = false;
            if (inside || pinned || busy || viewing || holdingCard || ChoosingAppearance || ChoosingBasket || ChoosingTexts || now < peekUntil) awaySince = DateTime.MaxValue;
            else
            {
                if (awaySince == DateTime.MaxValue) awaySince = now;
                if (now - awaySince > TimeSpan.FromMilliseconds(1200)) Reveal(false, false, 0);
            }
        }

        public void ShowCopied(string path) { if (cards.ContainsKey(path)) cards[path].FlashCopied(); }
        internal ShotCard CardFor(string path) { return cards[path]; }
        internal IEnumerable<ShotFlight> MotionWindows { get { return flights; } }
        internal IEnumerable<PinnedImageWindow> PinWindows { get { return pins.Values; } }
        internal bool Animating { get { return rendering; } }
    }

    internal sealed class ShotCard : Canvas, IDisposable
    {
        public readonly CaptureItem Item;
        public readonly Border Surface = new Border();
        internal readonly Button Grip = new Button();
        readonly TranslateTransform clipLift = new TranslateTransform();
        readonly ScaleTransform clipScale = new ScaleTransform(1, 1);
        readonly SpringMotion clipMotion = new SpringMotion(0.25, 0.8, 0);
        readonly ClothesClip clip = new ClothesClip();
        readonly Image photo = new Image();
        readonly Button close = new Button();
        readonly Border copied = new Border();
        readonly RotateTransform tilt = new RotateTransform(0, 82, 8);
        Point position;
        readonly ScaleTransform photoScale = new ScaleTransform(1, 1);
        readonly SpringMotion xMotion = new SpringMotion(0.55, 0.78, 0);
        readonly SpringMotion drop = new SpringMotion(0.42, 0.72, -46) { Target = 0 };
        readonly SpringMotion swing = new SpringMotion(2 * Math.PI / Math.Sqrt(46), 2.6 / (2 * Math.Sqrt(46)), 16) { Target = 0 };
        readonly SpringMotion scaleMotion = new SpringMotion(0.3, 0.6, 1);
        readonly DispatcherTimer hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(480) };
        readonly DispatcherTimer flash = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        readonly Action<ShotAction, CaptureItem> act;
        readonly ImageMenu menu;
        internal ImageMenu ActionMenu { get { return menu; } }
        Point start;
        bool pressed, held, dragging, placed, hovering, gripHover;
        double entryTime, breezeDelay = -1, breezeAngle, chrome = 1;
        // 照片边缘渐隐遮罩：只淡出靠窗口边缘的一条，其余部分保持不透明（夹子不参与遮罩）。
        readonly LinearGradientBrush photoMask = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
            GradientStops = new GradientStopCollection { new GradientStop(Colors.White, 0), new GradientStop(Colors.White, 1), new GradientStop(Colors.White, 1), new GradientStop(Colors.White, 1) }
        };
        ImageContent displayed;
        public bool Busy { get { return pressed || dragging || Releasing || menu.IsOpen; } }
        internal bool Animating
        {
            get
            {
                return !xMotion.Settled || !drop.Settled || !swing.Settled || !scaleMotion.Settled || !clipMotion.Settled
                    || breezeDelay >= 0 || entryTime < 0.16 || scaleMotion.Target != (pressed ? 0.95 : (hovering && !Releasing ? 1.035 : 1))
                    || clipMotion.Target != (gripHover || Releasing ? 1 : 0);
            }
        }
        public bool Flying { get; private set; }
        public bool Detached { get; set; }
        public bool Releasing { get; private set; }
        public bool IsDisposed { get; private set; }
        public readonly double Tilt;
        public LineTheme Appearance { get; private set; }
        public int ColorIndex { get; private set; }
        internal double MountX { get { return xMotion.Target; } }
        // 绳子在该卡片处的局部下凹（内容坐标 + 深度），由 ShelfWindow 每帧下发。
        internal double DipCenter = double.NaN;
        internal double DipAmount;

        public ShotCard(CaptureItem item, Action<ShotAction, CaptureItem> act, Gallery gallery, Func<CaptureItem[]> hangingItems)
        {
            Item = item;
            Appearance = LineTheme.All[0];
            this.act = act;
            Width = 164;
            Height = 178;
            RenderTransform = tilt;
            Tilt = (item.Path.GetHashCode() & 7) * 0.7 - 2.45;
            Opacity = 0;
            Surface.RenderTransform = photoScale;
            Surface.RenderTransformOrigin = new Point(0.5, 0);
            Surface.Background = new SolidColorBrush(LightTheme.CardColor);
            Surface.BorderBrush = new SolidColorBrush(LightTheme.CardEdge);
            Surface.BorderThickness = new Thickness(1);
            Surface.CornerRadius = new CornerRadius(12);
            Surface.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 4, Opacity = LightTheme.Dark ? 0.4 : 0.2, Color = LightTheme.ShadowColor };
            var imageLayer = new Grid();
            photo.Stretch = Stretch.Uniform;
            photo.Margin = new Thickness(6);
            imageLayer.Children.Add(photo);
            close.Content = LightTheme.CloseIcon();
            close.Style = LightTheme.ButtonStyle;
            close.MinWidth = 0;
            close.Width = close.Height = 22;
            close.Padding = new Thickness(0);
            close.Margin = new Thickness(3);
            close.HorizontalAlignment = HorizontalAlignment.Left;
            close.VerticalAlignment = VerticalAlignment.Top;
            close.Focusable = false;
            close.Opacity = 0;
            close.ToolTip = FileRules.IsInside(item.Path, gallery.Store.Inbox) ? "删除图片（移到回收站）" : "移除记录（保留原文件）";
            close.Click += delegate { act(ShotAction.Trash, item); };
            imageLayer.Children.Add(close);
            Surface.Child = imageLayer;
            Children.Add(Surface);
            var clipTransform = new TransformGroup();
            clipTransform.Children.Add(clipScale); clipTransform.Children.Add(clipLift);
            clip.RenderTransform = clipTransform;
            clip.RenderTransformOrigin = new Point(0.5, 0.5);
            clip.HorizontalAlignment = HorizontalAlignment.Center;
            clip.VerticalAlignment = VerticalAlignment.Top;
            clip.Margin = new Thickness(0, 4, 0, 0);
            Grip.Width = 30; Grip.Height = 34;
            // 底色必须是「几乎透明」而不是 Transparent：本窗口是透明分层窗口，
            // alpha=0 的像素（Transparent）不参与系统层命中测试，点击会直接穿透到桌面；
            // 而夹子图案 ClothesClip 自身 IsHitTestVisible=false，整个按钮就会一个可点像素都没有。
            // 这里用 3% 不透明度的白底，肉眼看不见，但整块热区都能接收到鼠标事件。
            Grip.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(LightTheme.Sheet("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='#08FFFFFF'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Top'/></Border></ControlTemplate>"));
            Grip.Focusable = false;
            Grip.Cursor = Cursors.Hand;
            Grip.ToolTip = new ToolTip { Content = "贴到桌面", Style = LightTheme.HintStyle,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Right, HorizontalOffset = 6 };
            ToolTipService.SetInitialShowDelay(Grip, 800);
            ToolTipService.SetBetweenShowDelay(Grip, 0);
            ToolTipService.SetShowDuration(Grip, 2500);
            System.Windows.Automation.AutomationProperties.SetName(Grip, "贴到桌面");
            Grip.Content = clip;
            Grip.MouseEnter += delegate { gripHover = true; };
            Grip.MouseLeave += delegate { gripHover = false; };
            Children.Add(Grip);
            SetLeft(Grip, 68); SetTop(Grip, -4);
            copied.Width = 84;
            copied.Padding = new Thickness(10, 4, 10, 4);
            copied.Background = Brushes.White;
            copied.BorderBrush = LightTheme.Brush("#DCE1E9");
            copied.BorderThickness = new Thickness(1);
            copied.CornerRadius = new CornerRadius(12);
            copied.Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.18 };
            copied.Child = new TextBlock { Text = "✓ 已复制", FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = LightTheme.Brush("#20408E"), TextAlignment = TextAlignment.Center };
            copied.Opacity = 0;
            copied.IsHitTestVisible = false;
            Children.Add(copied);
            flash.Tick += delegate { flash.Stop(); copied.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.2))); };
            Surface.MouseEnter += delegate { hovering = true; close.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.18))); };
            Surface.MouseLeave += delegate { hovering = false; close.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.18))); };
            Surface.MouseLeftButtonDown += Down;
            Surface.MouseLeftButtonUp += Up;
            Surface.MouseMove += Move;
            Surface.LostMouseCapture += delegate { hold.Stop(); pressed = false; };
            hold.Tick += delegate { hold.Stop(); held = true; pressed = false; Surface.ReleaseMouseCapture(); act(ShotAction.Edit, item); };
            menu = new ImageMenu(delegate(ShotAction action) { act(action, Item); }, FileRules.IsInside(item.Path, gallery.Store.Inbox));
            Surface.MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                var hanging = hangingItems();
                menu.SetBulk(Array.IndexOf(hanging, Item), hanging.Length);
                menu.PlacementTarget = Surface;
                WindowsNative.SetForegroundWindow(new WindowInteropHelper(Window.GetWindow(this)).Handle);
                menu.IsOpen = true;
            };
            UpdateImage();
        }

        public void ApplyTheme(LineTheme theme, int colorIndex)
        {
            Appearance = theme; ColorIndex = colorIndex;
            clip.Apply(theme, colorIndex);
        }

        public bool UpdateImage()
        {
            if (displayed == Item.Image) return false;
            displayed = Item.Image;
            menu.SetDetails(displayed.PixelWidth + " × " + displayed.PixelHeight);
            photo.Source = displayed.Thumbnail;
            double scale = Math.Min(142.0 / displayed.Thumbnail.PixelWidth, 98.0 / displayed.Thumbnail.PixelHeight);
            photo.Width = displayed.Thumbnail.PixelWidth * scale;
            photo.Height = displayed.Thumbnail.PixelHeight * scale;
            photo.Clip = new RectangleGeometry(new Rect(0, 0, photo.Width, photo.Height), 7, 7);
            Surface.Width = photo.Width + 14;
            Surface.Height = photo.Height + 14;
            SetLeft(Surface, (Width - Surface.Width) / 2);
            SetTop(Surface, 20);
            SetLeft(copied, (Width - copied.Width) / 2);
            SetTop(copied, Surface.Height + 30);
            return true;
        }

        public void Place(double x, bool immediately)
        {
            if (!placed || immediately) xMotion.Snap(x);
            else xMotion.Target = x;
            placed = true;
        }

        public void Frame(double elapsed, double sag, double lineWidth, double scrollOffset, double viewportWidth)
        {
            if (IsDisposed) return;
            xMotion.Step(elapsed);
            drop.Step(elapsed);
            swing.Step(elapsed);
            if (breezeDelay >= 0)
            {
                breezeDelay -= elapsed;
                if (breezeDelay < 0 && !Flying && !Busy) Nudge(breezeAngle);
            }
            scaleMotion.Target = pressed ? 0.95 : (hovering && !Releasing ? 1.035 : 1);
            scaleMotion.Step(elapsed);
            clipMotion.Target = gripHover || Releasing ? 1 : 0;
            clipMotion.Step(elapsed);
            clipLift.Y = -0.8 * clipMotion.Position;
            clipScale.ScaleX = clipScale.ScaleY = 1 + 0.06 * clipMotion.Position;
            photoScale.ScaleX = photoScale.ScaleY = scaleMotion.Position;
            double fraction = Math.Max(0, Math.Min(1, (xMotion.Position + 82 - scrollOffset) / lineWidth));
            position.X = xMotion.Position;
            position.Y = 18 + 4 * sag * fraction * (1 - fraction) - 8 + drop.Position;
            if (DipAmount > 0.02 && !double.IsNaN(DipCenter))
            {
                double delta = xMotion.Position + 82 - DipCenter;
                position.Y += DipAmount * Math.Exp(-(delta * delta) / (2 * 78 * 78));
            }
            Canvas.SetLeft(this, position.X);
            Canvas.SetTop(this, position.Y);
            tilt.Angle = Tilt + swing.Position;
            entryTime += elapsed;
            Opacity = Flying ? 0 : Math.Min(1, entryTime / 0.16);
            // 窗口两端的渐隐只作用于照片本体：夹子保持完全可见、可命中，随时能拖回来。
            // 渐隐用渐变遮罩实现——只有靠可视区边缘的一条照片区域淡出，其余保持完全不透明。
            // 可视区以 viewport 为准（左侧缩进 24、右侧缩进 284 给篮子/信封），左右对称。
            Surface.Opacity = Detached ? 0 : chrome;
            if (Detached || viewportWidth < 8)
            {
                if (Surface.OpacityMask != null) Surface.OpacityMask = null;
            }
            else
            {
                double left = xMotion.Position - scrollOffset;
                double width = Surface.ActualWidth;
                if (width < 8)
                {
                    if (Surface.OpacityMask != null) Surface.OpacityMask = null;
                }
                else
                {
                    const double edge = 44;
                    double lf = Math.Max(0, Math.Min(1, (edge - left) / width));
                    double rf = Math.Max(0, Math.Min(1, (left + width - (viewportWidth - edge)) / width));
                    if (lf <= 0 && rf <= 0)
                    {
                        if (Surface.OpacityMask != null) Surface.OpacityMask = null;
                    }
                    else
                    {
                        var stops = photoMask.GradientStops;
                        stops[0].Offset = 0; stops[0].Color = lf > 0 ? Colors.Transparent : Colors.White;
                        stops[1].Offset = lf; stops[1].Color = Colors.White;
                        stops[2].Offset = 1 - rf; stops[2].Color = Colors.White;
                        stops[3].Offset = 1; stops[3].Color = rf > 0 ? Colors.Transparent : Colors.White;
                        Surface.OpacityMask = photoMask;
                    }
                }
            }
        }

        public void Breeze(double delay, double degrees) { breezeDelay = delay; breezeAngle = degrees; }
        public void ScrollWith(double velocity) { swing.Target = Flying || Busy ? 0 : Math.Max(-2.5, Math.Min(2.5, -velocity / 200)); }
        public void Nudge(double degrees) { if (!Flying && !IsDisposed) swing.Velocity += degrees * 8; }

        public void OpenClip()
        {
            ((ToolTip)Grip.ToolTip).IsOpen = false;
            ToolTipService.SetIsEnabled(Grip, false);
            Releasing = true; Grip.IsEnabled = false;
            clip.Open();
        }
        public void DetachPhoto()
        {
            Releasing = false; Detached = true;
            Surface.Opacity = 0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.12)) { BeginTime = TimeSpan.FromSeconds(0.10) });
        }
        public void ResetClip()
        {
            BeginAnimation(OpacityProperty, null);
            Surface.Opacity = 1;
            ToolTipService.SetIsEnabled(Grip, true);
            Releasing = false; Grip.IsEnabled = true;
            clip.Reset();
        }

        public void BeginFlight()
        {
            Releasing = false;
            Flying = true;
            drop.Snap(0);
            swing.Snap(0);
            entryTime = 0;
            Opacity = 0;
        }

        public void Land()
        {
            if (IsDisposed) return;
            Flying = false;
            entryTime = 0;
            Nudge(2.2);
        }

        public Rect PhotoRect()
        {
            var parent = (Canvas)Parent;
            double x = position.X + (Width - Surface.Width) / 2, y = position.Y + 20;
            return new Rect(parent.PointToScreen(new Point(x, y)), parent.PointToScreen(new Point(x + Surface.Width, y + Surface.Height)));
        }

        void Down(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (e.ClickCount == 2) { hold.Stop(); held = true; act(ShotAction.Open, Item); return; }
            start = e.GetPosition(this);
            pressed = true;
            held = false;
            Surface.CaptureMouse();
            hold.Start();
        }

        void Up(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            hold.Stop();
            bool copy = pressed && !held && !dragging;
            pressed = false;
            Surface.ReleaseMouseCapture();
            if (copy) act(ShotAction.Copy, Item);
        }

        void Move(object sender, MouseEventArgs e)
        {
            if (!pressed || held || dragging || e.LeftButton != MouseButtonState.Pressed) return;
            Point position = e.GetPosition(this);
            if (Math.Abs(position.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            hold.Stop();
            pressed = false;
            dragging = true;
            chrome = 0.45;
            Surface.ReleaseMouseCapture();
            var payload = new DataObject(DataFormats.FileDrop, new[] { Item.Path });
            var preview = new DragPreview(Item.Image.Thumbnail);
            try { preview.Show(); DragDrop.DoDragDrop(Surface, payload, DragDropEffects.Copy | DragDropEffects.Move); }
            finally { preview.Close(); dragging = false; chrome = 1; }
        }

        public void FlashCopied()
        {
            copied.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.15)));
            Nudge(3);
            flash.Stop(); flash.Start();
        }
        public void Dispose() { IsDisposed = true; hold.Stop(); flash.Stop(); menu.IsOpen = false; }
    }

    internal sealed class DragPreview : Window
    {
        readonly DispatcherTimer follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        IntPtr handle;

        public DragPreview(System.Windows.Media.Imaging.BitmapSource image)
        {
            double scale = Math.Min(126.0 / image.PixelWidth, 86.0 / image.PixelHeight);
            Width = image.PixelWidth * scale + 12;
            Height = image.PixelHeight * scale + 12;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowActivated = false;
            ShowInTaskbar = false;
            Topmost = true;
            IsHitTestVisible = false;
            Opacity = 0.8;
            Content = new Border { CornerRadius = new CornerRadius(10), Background = Brushes.White, Padding = new Thickness(6), Child = new Image { Source = image } };
            SourceInitialized += delegate
            {
                handle = new WindowInteropHelper(this).Handle;
                WindowsNative.PrepareOverlay(handle);
                WindowsNative.PassMouse(handle, true);
                Follow();
                follow.Start();
            };
            follow.Tick += delegate { Follow(); };
            Closed += delegate { follow.Stop(); };
        }

        void Follow()
        {
            var cursor = WindowsNative.Cursor();
            double scale = WindowsNative.MonitorScale(WindowsNative.ScreenAtPointer());
            WindowsNative.SetWindowPos(handle, new IntPtr(-1), cursor.X + 18, cursor.Y + 20, (int)(Width * scale), (int)(Height * scale), 0x10);
        }
    }
}
