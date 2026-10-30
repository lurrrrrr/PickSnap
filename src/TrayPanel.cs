using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ScreenLine
{
    internal sealed class TrayPanel : ContextMenu
    {
        static readonly ResourceDictionary styles = (ResourceDictionary)XamlReader.Parse(LightTheme.Sheet(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='Action' TargetType='Button'>
    <Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='#283343'/>
    <Setter Property='Height' Value='34'/><Setter Property='Padding' Value='10,0'/>
    <Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Border x:Name='Surface' Background='{TemplateBinding Background}' CornerRadius='8' BorderBrush='Transparent' BorderThickness='1'>
        <ContentPresenter Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='Tag' Value='primary'><Setter Property='Background' Value='#EDF4FF'/><Setter Property='Foreground' Value='#2463AE'/></Trigger>
        <Trigger Property='Tag' Value='open'><Setter TargetName='Surface' Property='Background' Value='#DFE6EE'/></Trigger>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#DFE6EE'/></Trigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='primary'/><Condition Property='IsMouseOver' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Background' Value='#D6E4F7'/></MultiTrigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Background' Value='#E5ECF5'/></Trigger>
        <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#6E97CB'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Quick' TargetType='Button'>
    <!-- 快捷操作格：图标在上、文字在下，悬停时衬一块浅色圆角底，对应 Win11 菜单顶部的动作行。 -->
    <Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='#283343'/>
    <Setter Property='Margin' Value='1,0'/>
    <Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Border x:Name='Surface' Background='{TemplateBinding Background}' CornerRadius='6' BorderBrush='Transparent' BorderThickness='1'>
        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#D6DFEA'/><Setter TargetName='Surface' Property='BorderBrush' Value='#C4D1E0'/></Trigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Background' Value='#C8D4E2'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Toggle' TargetType='CheckBox'>
    <Setter Property='Foreground' Value='#283343'/><Setter Property='Height' Value='34'/>
    <Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
      <Border x:Name='Surface' Background='Transparent' CornerRadius='8' Padding='10,0' BorderBrush='Transparent' BorderThickness='1'>
        <Grid>
          <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='32'/></Grid.ColumnDefinitions>
          <ContentPresenter VerticalAlignment='Center'/>
          <Border x:Name='Track' Grid.Column='1' Width='32' Height='18' Background='#B8C2D0' BorderBrush='#B8C2D0' BorderThickness='1' CornerRadius='9' VerticalAlignment='Center'>
            <Ellipse x:Name='Thumb' Width='14' Height='14' Fill='White' HorizontalAlignment='Left' Margin='2,0'/>
          </Border>
        </Grid>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsChecked' Value='True'><Setter TargetName='Track' Property='Background' Value='#2463AE'/><Setter TargetName='Track' Property='BorderBrush' Value='#2463AE'/><Setter TargetName='Thumb' Property='HorizontalAlignment' Value='Right'/></Trigger>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#DFE6EE'/></Trigger>
        <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#6E97CB'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>"));

        internal readonly Button VisibilityButton, CaptureButton, BasketButton, TextButton, AppearanceButton, ExitButton, MoreButton, ScreenButton;
        internal readonly CheckBox Pictures, Text, AutoHang;
        internal readonly LightMenu MoreMenu = new LightMenu("更多操作", "");
        internal readonly LightMenu ScreenMenu = new LightMenu("显示屏幕", "") { Width = 272 };
        internal readonly TextBlock ScreenMode = new TextBlock { FontSize = 11, Foreground = LightTheme.Brush("#65717F"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 25, 0) };
        // 子菜单保持侧弹小面板，但交互全部由轮询驱动：ContextMenu 弹窗打开时会捕获鼠标，
        // WPF 的 MouseEnter/MouseLeave 在捕获期间不可靠（这正是此前"悬停卡住/乱弹出"的根因）。
        // 每隔 120ms 用屏幕坐标判定光标在面板/子菜单/表头行的哪块区域内，据此展开、切换和收起。
        readonly DispatcherTimer watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        LightMenu pendingMenu;
        DateTime pendingSince;
        DateTime? awaySince, subAwaySince;

        internal TrayPanel()
        {
            Width = 324; FontFamily = LightTheme.UiFont; FontSize = 12;
            Foreground = LightTheme.Brush("#283343"); HasDropShadow = false; SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            // 入场动效：淡入并从托盘方向轻微下沉归位，替代生硬的瞬显。
            var entrance = new TranslateTransform();
            RenderTransform = entrance;
            var panel = new StackPanel { Margin = new Thickness(6) };
            KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetDirectionalNavigation(panel, KeyboardNavigationMode.Cycle);
            var heading = new Grid { Height = 36, Margin = new Thickness(10, 0, 10, 4) };
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PickSnap.Icon"))
            {
                var logo = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.OrderByDescending(frame => frame.PixelWidth).First();
                heading.Children.Add(new Image { Source = logo, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
            }
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(34, 0, 0, 0) };
            title.Children.Add(new TextBlock { Text = "PickSnap", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock { Text = "· 拾帧", FontSize = 12, Foreground = LightTheme.Brush("#536172"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 1, 0, 0) });
            heading.Children.Add(title);
            var version = new Border { Background = LightTheme.Brush("#F1F4F8"), CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            version.Child = new TextBlock { Text = "v" + AppUpdates.CurrentVersion, FontSize = 10, Foreground = LightTheme.Brush("#65717F") };
            heading.Children.Add(version);
            panel.Children.Add(heading);

            // 快捷操作行：仿 Win11 右键菜单顶部，图标在上、名称在下，一行放下最常用的动作。
            VisibilityButton = Quick("显示绳子", "show");
            CaptureButton = Quick("区域截图", "capture");
            BasketButton = Quick("图片篮子", "folder");
            TextButton = Quick("文字信封", "envelope");
            var quickRow = new Grid { Margin = new Thickness(6, 2, 6, 2) };
            var quicks = new[] { VisibilityButton, CaptureButton, BasketButton, TextButton };
            for (int i = 0; i < quicks.Length; i++)
            {
                quickRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                quicks[i].Margin = new Thickness(2, 0, 2, 0);
                Grid.SetColumn(quicks[i], i); quickRow.Children.Add(quicks[i]);
            }
            panel.Children.Add(quickRow);
            panel.Children.Add(Divider());
            ScreenButton = Action("显示屏幕", "monitor"); ScreenButton.Height = 34;
            var screenArrow = Icon("chevron", false); screenArrow.Width = 13; screenArrow.Height = 13; screenArrow.HorizontalAlignment = HorizontalAlignment.Right;
            ((Grid)ScreenButton.Content).Children.Add(ScreenMode); ((Grid)ScreenButton.Content).Children.Add(screenArrow);
            panel.Children.Add(ScreenButton);
            panel.Children.Add(Divider());
            panel.Children.Add(Section("剪贴板收集"));
            Pictures = Toggle("收集图片", "picture", "收集剪贴板图片"); panel.Children.Add(Pictures);
            Text = Toggle("收集文字", "text", "收集剪贴板文字"); panel.Children.Add(Text);
            panel.Children.Add(Divider());
            AutoHang = Toggle("新图片自动上绳", "show", "新图片自动上绳"); panel.Children.Add(AutoHang);
            MoreButton = Action("更多操作", "more");
            var arrow = Icon("chevron", false); arrow.Width = 13; arrow.Height = 13; arrow.HorizontalAlignment = HorizontalAlignment.Right;
            ((Grid)MoreButton.Content).Children.Add(arrow); panel.Children.Add(MoreButton);
            AppearanceButton = Action("外观", "appearance"); panel.Children.Add(AppearanceButton);
            ExitButton = Action("退出", "exit"); panel.Children.Add(ExitButton);

            var surface = new FrameworkElementFactory(typeof(Border));
            surface.SetValue(Border.MarginProperty, new Thickness(12));
            surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            surface.SetValue(Border.BackgroundProperty, Brushes.White);
            surface.SetValue(Border.BorderBrushProperty, LightTheme.Brush("#E0E4EA"));
            surface.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            surface.SetValue(Border.EffectProperty, new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(38, 57, 76), BlurRadius = 22, ShadowDepth = 6, Opacity = .16 });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            Items.Add(panel);
            content.SetBinding(ContentPresenter.ContentProperty, new Binding("Items[0]") { RelativeSource = RelativeSource.TemplatedParent }); surface.AppendChild(content);
            Template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = surface };
            MoreButton.Click += delegate { OpenSubmenu(MoreMenu, MoreButton); };
            ScreenButton.Click += delegate { OpenSubmenu(ScreenMenu, ScreenButton); };
            watch.Tick += WatchTick;
            ScreenMenu.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(delegate { Close(); }));
            // 勾选类开关（如「悬停放大预览」）点击后面板保持打开，方便连续调整设置；
            // 普通动作点击后照旧收起面板。
            MoreMenu.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(delegate(object sender, RoutedEventArgs args)
            {
                var item = args.OriginalSource as MenuItem;
                if (item == null || !item.IsCheckable) IsOpen = false;
            }));
            PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            {
                var toggle = args.OriginalSource as CheckBox;
                if (args.Key == Key.Space && toggle != null)
                {
                    // 避免原生复选框在空格切换时释放鼠标捕获，导致托盘面板关闭。
                    if (!args.IsRepeat) toggle.IsChecked = toggle.IsChecked != true;
                    args.Handled = true; return;
                }
                if (args.Key != Key.Escape) return;
                if (ScreenMenu.IsOpen) ScreenMenu.IsOpen = false;
                else if (MoreMenu.IsOpen) MoreMenu.IsOpen = false;
                else Close();
                args.Handled = true;
            };
            Closed += delegate
            {
                watch.Stop();
                awaySince = subAwaySince = null; pendingMenu = null;
                MoreButton.Tag = null; ScreenButton.Tag = null;
                MoreMenu.IsOpen = false; ScreenMenu.IsOpen = false;
            };
            Opened += delegate
            {
                VisibilityButton.Focus();
                awaySince = subAwaySince = null; pendingMenu = null;
                watch.Start();
                CubicEase ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                Opacity = 0;
                BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(110)) { EasingFunction = ease });
                entrance.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
            };
        }

        internal void OnAction(Button button, Action action)
        {
            button.Click += delegate { Close(); action(); };
        }

        // 主快捷键提示（Ctrl+Alt+T）挂在「显示绳子」格的 ToolTip 上。
        internal void SetShortcutTip(string tip) { VisibilityButton.ToolTip = tip; }

        internal void OnToggle(CheckBox toggle, bool initial, Action<bool> changed)
        {
            toggle.IsChecked = initial;
            RoutedEventHandler handler = delegate { changed(toggle.IsChecked == true); };
            toggle.Checked += handler; toggle.Unchecked += handler;
        }

        internal void SetScreens(Forms.Screen[] screens, string selected, Action<string> choose)
        {
            int index = Array.FindIndex(screens, screen => screen.DeviceName == selected);
            ScreenMode.Text = string.IsNullOrEmpty(selected) ? "跟随鼠标" : index < 0 ? "主屏（原屏幕未连接）" : "屏幕 " + (index + 1);
            AutomationProperties.SetItemStatus(ScreenButton, ScreenMode.Text);
            while (ScreenMenu.Items.Count > 1) ScreenMenu.Items.RemoveAt(1);
            ScreenMenu.SetDetails(screens.Length + " 块已连接");
            var follow = ScreenMenu.AddAction("跟随鼠标", "show", delegate { choose(null); });
            follow.IsCheckable = true; follow.IsChecked = string.IsNullOrEmpty(selected);
            ScreenMenu.Separator();
            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                var option = ScreenMenu.AddAction("屏幕 " + (i + 1) + (screen.Primary ? " · 主屏" : ""), "monitor", delegate { choose(screen.DeviceName); });
                option.IsCheckable = true; option.IsChecked = screen.DeviceName == selected;
                option.InputGestureText = screen.Bounds.Width + " × " + screen.Bounds.Height;
            }
            ScreenMenu.Footer("跟随鼠标：移到任意屏幕顶部展开\n固定屏幕：始终在所选屏幕显示");
            if (!string.IsNullOrEmpty(selected) && index < 0) ScreenMenu.Footer("原屏幕未连接，暂用主屏；重连后恢复");
        }

        internal void Close()
        {
            watch.Stop();
            awaySince = subAwaySince = null; pendingMenu = null;
            MoreButton.Tag = null; ScreenButton.Tag = null;
            MoreMenu.IsOpen = false; ScreenMenu.IsOpen = false; IsOpen = false;
        }

        // 光标是否落在元素的屏幕矩形内（面板与弹窗都处于打开状态，PointToScreen 可用）。
        // WindowsNative.Point 与 PointToScreen 都是设备像素，ActualWidth 需先乘 DPI 缩放。
        static bool Contains(FrameworkElement element, WindowsNative.Point cursor)
        {
            if (!element.IsVisible || element.ActualWidth <= 0) return false;
            var source = PresentationSource.FromVisual(element);
            double scale = source == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
            Point corner = element.PointToScreen(new Point(0, 0));
            return cursor.X >= corner.X && cursor.X <= corner.X + element.ActualWidth * scale &&
                   cursor.Y >= corner.Y && cursor.Y <= corner.Y + element.ActualHeight * scale;
        }

        void OpenSubmenu(LightMenu menu, Button button)
        {
            LightMenu other = menu == MoreMenu ? ScreenMenu : MoreMenu;
            other.IsOpen = false;
            menu.PlacementTarget = button; menu.Placement = PlacementMode.Left;
            menu.HorizontalOffset = -6; menu.VerticalOffset = -16;
            menu.IsOpen = true;
            pendingMenu = null; subAwaySince = null; awaySince = null;
        }

        // 交互轮询：展开、切换、收起、关闭全部依据光标位置判定，不依赖 MouseEnter/Leave。
        void WatchTick(object sender, EventArgs args)
        {
            if (!IsOpen) { watch.Stop(); return; }
            WindowsNative.Point cursor = WindowsNative.Cursor();
            bool overPanel = Contains(this, cursor);
            bool overMore = MoreMenu.IsOpen && Contains(MoreMenu, cursor);
            bool overScreen = ScreenMenu.IsOpen && Contains(ScreenMenu, cursor);
            bool overHeader = Contains(MoreButton, cursor) || Contains(ScreenButton, cursor);
            LightMenu open = MoreMenu.IsOpen ? MoreMenu : ScreenMenu.IsOpen ? ScreenMenu : null;
            if (overPanel || overMore || overScreen || overHeader)
            {
                awaySince = null;
            }
            else
            {
                DateTime now = DateTime.UtcNow;
                if (awaySince == null) awaySince = now;
                else if ((now - awaySince.Value).TotalMilliseconds > 700) { Close(); return; }
            }
            // 子菜单只属于它的表头行：光标既不在子菜单、也不在对应的表头上时，
            // 即使还在面板内（比如移到了其他行），350ms 后也收起子菜单，表头还原为未选中。
            if (open != null)
            {
                bool overOwn = open == MoreMenu ? overMore || Contains(MoreButton, cursor) : overScreen || Contains(ScreenButton, cursor);
                if (overOwn) subAwaySince = null;
                else
                {
                    DateTime now = DateTime.UtcNow;
                    if (subAwaySince == null) subAwaySince = now;
                    else if ((now - subAwaySince.Value).TotalMilliseconds > 350) { open.IsOpen = false; subAwaySince = null; }
                }
            }
            // 悬停表头 160ms 展开对应子菜单；悬停另一表头时直接切换。
            LightMenu target = Contains(MoreButton, cursor) ? MoreMenu : Contains(ScreenButton, cursor) ? ScreenMenu : null;
            if (target != null)
            {
                if (pendingMenu != target) { pendingMenu = target; pendingSince = DateTime.UtcNow; }
                else if (open != target && (DateTime.UtcNow - pendingSince).TotalMilliseconds >= 160)
                    OpenSubmenu(target, target == MoreMenu ? MoreButton : ScreenButton);
            }
            else pendingMenu = null;
            // 子菜单处于打开状态时，其表头行保持"选中"底色，明确指示小面板属于谁。
            MoreButton.Tag = MoreMenu.IsOpen ? "open" : null;
            ScreenButton.Tag = ScreenMenu.IsOpen ? "open" : null;
        }

        static Button Action(string title, string icon, bool accent = false)
        {
            var button = new Button { Content = Label(title, icon, accent), Style = (Style)styles["Action"] };
            AutomationProperties.SetName(button, title); return button;
        }

        static Button Quick(string title, string icon)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(2, 8, 2, 7) };
            var image = Icon(icon, false, 1.8); image.Width = 19; image.Height = 19; image.HorizontalAlignment = HorizontalAlignment.Center; image.Margin = new Thickness(0, 0, 0, 5);
            stack.Children.Add(image);
            stack.Children.Add(new TextBlock { Text = title, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
            var button = new Button { Content = stack, Style = (Style)styles["Quick"] };
            AutomationProperties.SetName(button, title); return button;
        }

        static TextBlock Section(string title)
        {
            // 分组标题：上下多留呼吸空间，不再贴着分隔线和下面的开关。
            return new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = LightTheme.Brush("#536172"), Margin = new Thickness(12, 9, 0, 7) };
        }

        static CheckBox Toggle(string title, string icon, string name)
        {
            var toggle = new CheckBox { Content = Label(title, icon, false), Style = (Style)styles["Toggle"] };
            AutomationProperties.SetName(toggle, name); return toggle;
        }

        static Grid Label(string title, string icon, bool accent)
        {
            var grid = new Grid();
            var image = Icon(icon, accent); image.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(image);
            grid.Children.Add(new TextBlock { Text = title, Margin = new Thickness(29, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            return grid;
        }

        static Viewbox Icon(string name, bool accent, double thickness = 1.65)
        {
            // 图标用固定的柔和灰（与 LightMenu 一致），主行动作用蓝色点缀；不跟随文字色，避免整体过重。
            var path = new Path { Data = LightMenu.IconGeometry(name), Stroke = LightTheme.Brush(accent ? "#2463AE" : "#647386"), StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            return new Viewbox { Width = 17, Height = 17, Child = path };
        }

        static Border Divider() { return new Border { Height = 1, Background = LightTheme.Brush("#ECEFF3"), Margin = new Thickness(10, 6, 10, 6) }; }
    }
}
