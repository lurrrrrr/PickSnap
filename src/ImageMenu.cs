using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ScreenLine
{
    internal sealed class ImageMenu : ContextMenu
    {
        static readonly Style actionStyle = (Style)XamlReader.Parse(LightTheme.Sheet(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
  <Setter Property='Background' Value='Transparent'/><Setter Property='Height' Value='32'/><Setter Property='Padding' Value='10,0'/>
  <Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
    <Border x:Name='Surface' Background='{TemplateBinding Background}' CornerRadius='7' BorderBrush='Transparent' BorderThickness='1'>
      <ContentPresenter Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='Background' Value='#DFE6EE'/></Trigger>
      <Trigger Property='Tag' Value='hover'><Setter TargetName='Surface' Property='Background' Value='#DFE6EE'/></Trigger>
      <Trigger Property='Tag' Value='danger-hover'><Setter TargetName='Surface' Property='Background' Value='#FFF0ED'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>"));

        // 宿主里的四行（放进篮子/批量放进篮子/移到回收站/批量移到回收站）必须全是 MenuItem：
        // 往 Menu.Items 里塞 Button 会被 MenuBase 自动包一层「系统默认 MenuItem」，
        // 键盘选中时画出 Aero2 系统蓝高亮，包装层自带内边距还会把行距和图标位置带偏
        // ——这就是「图标不对齐、选中态不统一」的根源。
        // 四行共用同一套模板：同一图标画法（Path 18×18）、同一内边距、同一悬停底色；
        // 批量行只是多了箭头列和 Popup。
        static readonly string stackIcon = "M12,3 L21,8 L12,13 L3,8 Z M3,12 L12,17 L21,12 M3,16 L12,21 L21,16";
        static readonly Style rowStyle = RowStyle("#283343", "#DFE6EE", LightMenu.IconGeometry("clear").ToString(), false, null);
        static readonly Style trashRowStyle = RowStyle("#BB514E", "#FFF0ED", LightMenu.IconGeometry("trash").ToString(), false, null);
        static readonly Style bulkStyle = RowStyle("#283343", "#DFE6EE", stackIcon, true, "#65717F");
        static readonly Style trashBulkStyle = RowStyle("#BB514E", "#FFF0ED", stackIcon, true, "#BB514E");
        static readonly Style bulkActionStyle = BulkRowStyle("#283343", "#DFE6EE");
        static readonly Style trashBulkActionStyle = BulkRowStyle("#BB514E", "#FFF0ED");

        // 宿主行模板：图标 18×18 + 文字 +（批量行才有的）箭头列；悬停/键盘选中同一种底色。
        static Style RowStyle(string text, string hover, string iconData, bool submenu, string arrow)
        {
            string chevron = submenu
                ? "<Path Grid.Column='2' Data='M9,4 L16,12 L9,20' Stroke='" + arrow + "' StrokeThickness='1.65' StrokeStartLineCap='Round' StrokeEndLineCap='Round' Width='12' Height='12' Stretch='Uniform'/>"
                : "";
            string popup = submenu
                ? "<Popup x:Name='PART_Popup' Placement='Right' AllowsTransparency='True' Focusable='False' IsOpen='{TemplateBinding IsSubmenuOpen}'>" +
                  "<Border Width='180' Padding='4' Background='#FFFFFF' BorderBrush='#E0E4EA' BorderThickness='1' CornerRadius='9'>" +
                  "<StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/></Border></Popup>"
                : "";
            string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='MenuItem'>
  <Setter Property='Foreground' Value='__TEXT__'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'>
    <Grid>
      <Border x:Name='Row' Height='32' Padding='10,0' CornerRadius='7' Background='Transparent'>
        <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='18'/><ColumnDefinition Width='*'/><ColumnDefinition Width='12'/></Grid.ColumnDefinitions>
          <Path Data='__ICON__' Stroke='__TEXT__' StrokeThickness='1.65' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' Width='18' Height='18' Stretch='Uniform'/>
          <ContentPresenter Grid.Column='1' ContentSource='Header' Margin='10,0,0,0' VerticalAlignment='Center'/>
          __CHEVRON__
        </Grid>
      </Border>
      __POPUP__
    </Grid>
    <ControlTemplate.Triggers>
      <Trigger Property='Tag' Value='hover'><Setter TargetName='Row' Property='Background' Value='__HOVER__'/></Trigger>
      <Trigger Property='IsSubmenuOpen' Value='True'><Setter TargetName='Row' Property='Background' Value='__HOVER__'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>";
            xaml = xaml.Replace("__CHEVRON__", chevron).Replace("__POPUP__", popup)
                .Replace("__ICON__", iconData).Replace("__TEXT__", text).Replace("__HOVER__", hover);
            return (Style)XamlReader.Parse(LightTheme.Sheet(xaml));
        }

        static Style BulkRowStyle(string text, string hover)
        {
            string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='MenuItem'>
  <Setter Property='Foreground' Value='__TEXT__'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'>
    <Border x:Name='Row' Height='32' Padding='12,0' CornerRadius='7' Background='Transparent'>
      <ContentPresenter ContentSource='Header' VerticalAlignment='Center'/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Row' Property='Background' Value='__HOVER__'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>";
            return (Style)XamlReader.Parse(LightTheme.Sheet(xaml.Replace("__TEXT__", text).Replace("__HOVER__", hover)));
        }

        readonly TextBlock details;
        // Actions 只装顶部四行（面板里的普通 Button）；宿主里的行都是 MenuItem，见 Host()。
        internal readonly Dictionary<ShotAction, Button> Actions = new Dictionary<ShotAction, Button>();
        internal readonly MenuItem BulkMenu, BulkTrash;
        internal readonly Dictionary<ShotAction, MenuItem> BulkActions = new Dictionary<ShotAction, MenuItem>();
        readonly MenuItem removeRow, trashRow;
        // 行高亮用轮询驱动：ContextMenu 会捕获鼠标，访问过"批量放进篮子"子菜单后
        // MouseEnter/MouseLeave 不再派发给行元素，事件驱动的悬停从此失效。
        readonly DispatcherTimer watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        Button hovered;
        bool bulkByMouse, trashByMouse;

        internal ImageMenu(Action<ShotAction> execute, bool managed)
        {
            Width = 220; FontFamily = LightTheme.UiFont; FontSize = 12;
            Foreground = LightTheme.Brush("#283343"); HasDropShadow = false; SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            var panel = new StackPanel();
            KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetDirectionalNavigation(panel, KeyboardNavigationMode.Cycle);
            var heading = new Grid { Margin = new Thickness(10, 5, 10, 7) };
            heading.Children.Add(new TextBlock { Text = "图片", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = LightTheme.Brush("#536172") });
            details = new TextBlock { FontSize = 11, Foreground = LightTheme.Brush("#65717F"), HorizontalAlignment = HorizontalAlignment.Right };
            heading.Children.Add(details); panel.Children.Add(heading);
            panel.Children.Add(Action("打开", "open", ShotAction.Open, execute));
            panel.Children.Add(Action("编辑", "edit", ShotAction.Edit, execute));
            panel.Children.Add(Action("保存副本", "save", ShotAction.Save, execute));
            panel.Children.Add(Action("打开位置", "folder", ShotAction.Locate, execute));
            panel.Children.Add(new Border { Height = 1, Background = LightTheme.Brush("#E8EBEF"), Margin = new Thickness(6, 4, 6, 4) });
            // 顺序跟随动线分组：放进篮子的两个动作挨在一起，回收站的两个动作挨在一起；
            // 子项只写范围（左侧全部/右侧全部/仅保留这一张/全部），动词由父行表达，命名就统一了。
            removeRow = PlainRow("放进篮子", ShotAction.Remove, rowStyle, execute);
            BulkMenu = Bulk("批量放进篮子", bulkStyle, bulkActionStyle, delegate(bool value) { bulkByMouse = value; }, execute, new[] {
                new KeyValuePair<ShotAction, string>(ShotAction.RemoveLeft, "左侧全部"),
                new KeyValuePair<ShotAction, string>(ShotAction.RemoveRight, "右侧全部"),
                new KeyValuePair<ShotAction, string>(ShotAction.RemoveOthers, "仅保留这一张"),
                new KeyValuePair<ShotAction, string>(ShotAction.RemoveAll, "全部") });
            trashRow = PlainRow(managed ? "移到回收站" : "移除记录（保留原图）", ShotAction.Trash, trashRowStyle, execute);
            // 批量删除只给收集文件夹里的副本：外部原图的「删除」其实是移除记录，批量做没有意义。
            if (managed)
                BulkTrash = Bulk("批量移到回收站", trashBulkStyle, trashBulkActionStyle, delegate(bool value) { trashByMouse = value; }, execute, new[] {
                    new KeyValuePair<ShotAction, string>(ShotAction.TrashLeft, "左侧全部"),
                    new KeyValuePair<ShotAction, string>(ShotAction.TrashRight, "右侧全部"),
                    new KeyValuePair<ShotAction, string>(ShotAction.TrashOthers, "仅保留这一张"),
                    new KeyValuePair<ShotAction, string>(ShotAction.TrashAll, "全部") });
            panel.Children.Add(Host());
            var surface = LightTheme.Card(panel); surface.Padding = new Thickness(4);
            Items.Add(surface);
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetBinding(ContentPresenter.ContentProperty, new Binding("Items[0]") { RelativeSource = RelativeSource.TemplatedParent });
            Template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = content };
            PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Escape) { IsOpen = false; args.Handled = true; }
                else if (args.OriginalSource == this && (args.Key == Key.Down || args.Key == Key.Up || args.Key == Key.Tab))
                {
                    bool previous = args.Key == Key.Up || (args.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                    MenuItem target = previous ? (BulkTrash != null ? BulkTrash : trashRow) : removeRow;
                    target.Focus(); args.Handled = true;
                }
                else if (args.Key == Key.Space)
                {
                    var button = args.OriginalSource as Button;
                    if (button == null) return;
                    if (!args.IsRepeat) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    args.Handled = true;
                }
            };
            Opened += delegate { Focus(); watch.Start(); };
            Closed += delegate
            {
                BulkMenu.IsSubmenuOpen = false; if (BulkTrash != null) BulkTrash.IsSubmenuOpen = false;
                watch.Stop(); SetHover(null); ClearRowHover();
            };
            watch.Tick += delegate(object sender, EventArgs args)
            {
                if (!IsOpen) { watch.Stop(); SetHover(null); ClearRowHover(); return; }
                var cursor = WindowsNative.Cursor();
                SetHover(Actions.Values.FirstOrDefault(row => Contains(row, cursor)));
                PollRow(removeRow, cursor);
                PollRow(trashRow, cursor);
                // 子菜单打开后 MouseLeave 在鼠标捕获下收不到，250ms 关闭定时器不会启动，
                // 行会一直显示选中底色——轮询兜底强制收起；行高亮本身只认 Tag 与 IsSubmenuOpen。
                bulkByMouse = Poll(BulkMenu, cursor, bulkByMouse);
                if (BulkTrash != null) trashByMouse = Poll(BulkTrash, cursor, trashByMouse);
            };
        }

        // 宿主里的单动作行（放进篮子 / 移到回收站）：与批量行同一套 MenuItem 模板，
        // 只是没有箭头和子菜单；悬停与键盘选中都走 Tag='hover' 触发器。
        MenuItem PlainRow(string title, ShotAction action, Style style, Action<ShotAction> execute)
        {
            var row = new MenuItem { Header = title, Style = style, FontFamily = FontFamily, FontSize = FontSize };
            AutomationProperties.SetName(row, title);
            row.Click += delegate { IsOpen = false; execute(action); };
            row.GotKeyboardFocus += delegate { row.Tag = "hover"; };
            row.LostKeyboardFocus += delegate { row.Tag = null; };
            row.PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Enter || args.Key == Key.Space) { IsOpen = false; execute(action); args.Handled = true; }
            };
            return row;
        }

        void PollRow(MenuItem row, WindowsNative.Point cursor)
        {
            row.Tag = Contains(row, cursor) ? "hover" : null;
        }

        void ClearRowHover()
        {
            removeRow.Tag = null; trashRow.Tag = null;
        }

        // 轮询维护一个子菜单的悬停底色与「鼠标移开后自动收起」，返回该子菜单是否由鼠标展开。
        static bool Poll(MenuItem menu, WindowsNative.Point cursor, bool byMouse)
        {
            bool over = Contains(menu, cursor);
            menu.Tag = over ? "hover" : null;
            if (byMouse && menu.IsSubmenuOpen && !over && menu.Template != null)
            {
                var popup = menu.Template.FindName("PART_Popup", menu) as Popup;
                var host = popup == null ? null : popup.Child as FrameworkElement;
                if (host == null || !popup.IsOpen || !Contains(host, cursor)) { menu.IsSubmenuOpen = false; byMouse = false; }
            }
            return byMouse;
        }

        // 带子菜单的批量行：鼠标进出、键盘右方向键展开、延迟收起，两处（取下/删除）共用一套。
        MenuItem Bulk(string header, Style style, Style rowStyle, Action<bool> mouse, Action<ShotAction> execute, KeyValuePair<ShotAction, string>[] entries)
        {
            var menu = new MenuItem { Header = header, Style = style, FontFamily = FontFamily, FontSize = FontSize };
            foreach (var entry in entries)
            {
                ShotAction action = entry.Key;
                var child = new MenuItem { Header = entry.Value, Style = rowStyle };
                child.Click += delegate { IsOpen = false; execute(action); };
                // 子菜单的自动收起不靠 MouseLeave + 定时器：置灰的 MenuItem 不参与命中
                // 测试，鼠标经过它时 MouseEnter 收不到，上一项的离开计时器照样跑完，
                // 子菜单会在光标还停在弹层里时自己收掉。统一交给 watch 轮询里的 Poll()——
                // 光标在行上或在弹层内都算停留，只有真正离开才收。
                BulkActions.Add(action, child); menu.Items.Add(child);
            }
            menu.MouseEnter += delegate { mouse(true); menu.IsSubmenuOpen = true; };
            // 行底色一律由轮询写入的 Tag 驱动；IsHighlighted 在鼠标捕获下会残留为 true
            // （MouseLeave 派发不到），模板里已不再引用它。键盘 Tab 聚焦时单独同步 Tag。
            menu.GotKeyboardFocus += delegate { menu.Tag = "hover"; };
            menu.LostKeyboardFocus += delegate { menu.Tag = null; };
            menu.PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.OriginalSource == menu && (args.Key == Key.Right || args.Key == Key.Enter || args.Key == Key.Space))
                {
                    mouse(false);
                    menu.IsSubmenuOpen = true;
                    foreach (var child in menu.Items) { var item = child as MenuItem; if (item != null && item.IsEnabled) { item.Focus(); break; } }
                    args.Handled = true;
                }
            };
            return menu;
        }

        // 四行动作（放进篮子、批量放进篮子、移到回收站、批量移到回收站）必须共用一个 Menu 宿主：
        // 分成两个宿主时，WPF 的菜单模式会抓住鼠标，先被用过的那个宿主之后，
        // 另一行再也收不到 MouseEnter，子菜单就永远打不开（表现就是悬停毫无反应）。
        // 宿主里只允许 MenuItem / Separator：其他元素会被 MenuBase 自动包一层系统
        // 默认 MenuItem，带来系统蓝高亮、多余内边距和图标位移。
        Menu Host()
        {
            var host = new Menu { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
            var stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
            host.ItemsPanel = new ItemsPanelTemplate(stack);
            host.Items.Add(removeRow);
            host.Items.Add(BulkMenu);
            // 分组分隔必须用 Separator 而不是普通 Border：MenuBase 把 Separator 当原生容器，
            // 不可聚焦、键盘导航直接跳过；普通 Border 包在 ContentPresenter 里会被焦点框
            // 选中，菜单里凭空多出一行蓝色高亮的空行。
            var groupLine = new FrameworkElementFactory(typeof(Border));
            groupLine.SetValue(Border.HeightProperty, 1.0);
            groupLine.SetValue(Border.BackgroundProperty, LightTheme.Brush("#E8EBEF"));
            host.Items.Add(new Separator
            {
                Margin = new Thickness(6, 4, 6, 4),
                Focusable = false,
                Template = new ControlTemplate(typeof(Separator)) { VisualTree = groupLine }
            });
            host.Items.Add(trashRow);
            if (BulkTrash != null) host.Items.Add(BulkTrash);
            return host;
        }

        // 光标是否落在元素的屏幕矩形内（菜单处于打开状态，PointToScreen 可用）。
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

        // 通过 Tag 触发器设置顶部四行（面板内 Button）的悬停底色。
        void SetHover(Button row)
        {
            if (hovered == row) return;
            if (hovered != null) hovered.Tag = null;
            hovered = row;
            if (hovered != null) hovered.Tag = "hover";
        }

        internal void SetDetails(string value) { details.Text = value; }

        internal void SetBulk(int index, int count)
        {
            SetBulkState(ShotAction.RemoveLeft, index > 0);
            SetBulkState(ShotAction.RemoveRight, index >= 0 && index < count - 1);
            SetBulkState(ShotAction.RemoveOthers, index >= 0 && count > 1);
            SetBulkState(ShotAction.RemoveAll, count > 0);
            SetBulkState(ShotAction.TrashLeft, index > 0);
            SetBulkState(ShotAction.TrashRight, index >= 0 && index < count - 1);
            SetBulkState(ShotAction.TrashOthers, index >= 0 && count > 1);
            SetBulkState(ShotAction.TrashAll, count > 0);
        }

        void SetBulkState(ShotAction action, bool enabled)
        {
            MenuItem item;
            // 不满足条件（比如右键的就是最右一张，「右侧全部」无图可选）时置灰：
            // 保持菜单形状稳定、便于记忆位置，灰字+40% 透明表明当前不可选。
            if (BulkActions.TryGetValue(action, out item)) item.IsEnabled = enabled;
        }

        Button Action(string title, string icon, ShotAction action, Action<ShotAction> execute)
        {
            var color = LightTheme.Brush("#283343");
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            // 图标用与宿主行完全相同的画法（Path 18×18 Stretch=Uniform），保证所有行的
            // 图标大小、粗细、位置像素级一致；Viewbox 会把描边也算进缩放，图标明显偏小。
            label.Children.Add(new Path { Data = LightMenu.IconGeometry(icon), Stroke = color, StrokeThickness = 1.65, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 18, Height = 18, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center });
            label.Children.Add(new TextBlock { Text = title, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var button = new Button {
                Content = label, Style = actionStyle, Foreground = color,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(button, title);
            button.Click += delegate { IsOpen = false; execute(action); };
            Actions.Add(action, button); return button;
        }

    }
}
