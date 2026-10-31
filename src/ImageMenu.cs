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
      <Trigger Property='Tag' Value='hover'><Setter TargetName='Surface' Property='Background' Value='#DFE6EE'/></Trigger>
      <Trigger Property='Tag' Value='danger-hover'><Setter TargetName='Surface' Property='Background' Value='#FFF0ED'/></Trigger>
      <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#6E97CB'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>"));

        static readonly Style bulkStyle = (Style)XamlReader.Parse(LightTheme.Sheet(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='MenuItem'>
  <Setter Property='Foreground' Value='#283343'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'>
    <Grid>
      <Border x:Name='Row' Height='32' Padding='10,0' CornerRadius='7' Background='Transparent'>
        <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='18'/><ColumnDefinition Width='*'/><ColumnDefinition Width='12'/></Grid.ColumnDefinitions>
          <Path Data='M4,12 H20' Stroke='#283343' StrokeThickness='1.65' Width='18' Height='18' Stretch='Uniform'/>
          <ContentPresenter Grid.Column='1' ContentSource='Header' Margin='10,0,0,0' VerticalAlignment='Center'/>
          <Path Grid.Column='2' Data='M9,4 L16,12 L9,20' Stroke='#65717F' StrokeThickness='1.65' Width='12' Height='12' Stretch='Uniform'/>
        </Grid>
      </Border>
      <Popup x:Name='PART_Popup' Placement='Right' AllowsTransparency='True' Focusable='False' IsOpen='{TemplateBinding IsSubmenuOpen}'>
        <Border Width='180' Padding='4' Background='White' BorderBrush='#E0E4EA' BorderThickness='1' CornerRadius='9'>
          <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
        </Border>
      </Popup>
    </Grid>
    <ControlTemplate.Triggers>
      <Trigger Property='Tag' Value='hover'><Setter TargetName='Row' Property='Background' Value='#DFE6EE'/></Trigger>
      <Trigger Property='IsSubmenuOpen' Value='True'><Setter TargetName='Row' Property='Background' Value='#DFE6EE'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>"));

        static readonly Style bulkActionStyle = (Style)XamlReader.Parse(LightTheme.Sheet(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='MenuItem'>
  <Setter Property='Foreground' Value='#283343'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'>
    <Border x:Name='Row' Height='32' Padding='12,0' CornerRadius='7' Background='Transparent'>
      <ContentPresenter ContentSource='Header' VerticalAlignment='Center'/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Row' Property='Background' Value='#DFE6EE'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>"));

        readonly TextBlock details;
        internal readonly Dictionary<ShotAction, Button> Actions = new Dictionary<ShotAction, Button>();
        internal readonly MenuItem BulkMenu = new MenuItem { Header = "批量取下" };
        internal readonly Dictionary<ShotAction, MenuItem> BulkActions = new Dictionary<ShotAction, MenuItem>();
        readonly DispatcherTimer bulkClose = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        // 行高亮用轮询驱动：ContextMenu 会捕获鼠标，访问过"批量取下"子菜单后
        // MouseEnter/MouseLeave 不再派发给普通 Button 行，事件驱动的悬停从此失效。
        readonly DispatcherTimer watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        Button hovered;
        bool bulkByMouse;

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
            panel.Children.Add(Action("取下放篮子", "clear", ShotAction.Remove, execute));
            var bulk = new Menu { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
            BulkMenu.Style = bulkStyle; BulkMenu.Width = 180;
            BulkMenu.FontFamily = FontFamily; BulkMenu.FontSize = FontSize;
            foreach (var entry in new[] {
                new KeyValuePair<ShotAction, string>(ShotAction.RemoveLeft, "取下左侧全部"),
                new KeyValuePair<ShotAction, string>(ShotAction.RemoveRight, "取下右侧全部"),
                new KeyValuePair<ShotAction, string>(ShotAction.RemoveOthers, "仅保留这一张"),
                new KeyValuePair<ShotAction, string>(ShotAction.RemoveAll, "取下全部") })
            {
                ShotAction action = entry.Key;
                var child = new MenuItem { Header = entry.Value, Style = bulkActionStyle };
                child.Click += delegate { IsOpen = false; execute(action); };
                child.MouseEnter += delegate { bulkClose.Stop(); };
                child.MouseLeave += delegate { bulkClose.Start(); };
                BulkActions.Add(action, child); BulkMenu.Items.Add(child);
            }
            BulkMenu.MouseEnter += delegate { bulkClose.Stop(); bulkByMouse = true; BulkMenu.IsSubmenuOpen = true; };
            BulkMenu.MouseLeave += delegate { if (BulkMenu.IsSubmenuOpen) bulkClose.Start(); };
            // 行底色一律由轮询写入的 Tag 驱动；IsHighlighted 在鼠标捕获下会残留为 true
            // （MouseLeave 派发不到），模板里已不再引用它。键盘 Tab 聚焦时单独同步 Tag。
            BulkMenu.GotKeyboardFocus += delegate { BulkMenu.Tag = "hover"; };
            BulkMenu.LostKeyboardFocus += delegate { BulkMenu.Tag = null; };
            BulkMenu.PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.OriginalSource == BulkMenu && (args.Key == Key.Right || args.Key == Key.Enter || args.Key == Key.Space))
                {
                    bulkByMouse = false;
                    BulkMenu.IsSubmenuOpen = true;
                    foreach (var child in BulkActions.Values) if (child.IsEnabled) { child.Focus(); break; }
                    args.Handled = true;
                }
            };
            bulkClose.Tick += delegate { bulkClose.Stop(); BulkMenu.IsSubmenuOpen = false; };
            bulk.Items.Add(BulkMenu); panel.Children.Add(bulk);
            panel.Children.Add(Action(managed ? "移到回收站" : "移除记录（保留原图）", "trash", ShotAction.Trash, execute));
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
                    Actions[previous ? ShotAction.Trash : ShotAction.Open].Focus(); args.Handled = true;
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
            Closed += delegate { bulkClose.Stop(); BulkMenu.IsSubmenuOpen = false; watch.Stop(); SetHover(null); };
            watch.Tick += delegate(object sender, EventArgs args)
            {
                if (!IsOpen) { watch.Stop(); SetHover(null); return; }
                var cursor = WindowsNative.Cursor();
                SetHover(Actions.Values.FirstOrDefault(row => Contains(row, cursor)));
                bool overBulk = Contains(BulkMenu, cursor);
                // 子菜单打开后 MouseLeave 在鼠标捕获下收不到，250ms 关闭定时器不会启动，
                // 行会一直显示选中底色——轮询兜底强制收起；行高亮本身只认 Tag 与 IsSubmenuOpen。
                if (bulkByMouse && BulkMenu.IsSubmenuOpen && !overBulk)
                {
                    var popup = BulkMenu.Template.FindName("PART_Popup", BulkMenu) as Popup;
                    var host = popup == null ? null : popup.Child as FrameworkElement;
                    if (host == null || !popup.IsOpen || !Contains(host, cursor)) { BulkMenu.IsSubmenuOpen = false; bulkClose.Stop(); }
                }
                BulkMenu.Tag = overBulk ? "hover" : null;
            };
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

        // 通过 Tag 触发器设置行底色：普通行 #DFE6EE，回收站行 #FFF0ED。
        void SetHover(Button row)
        {
            if (hovered == row) return;
            if (hovered != null) hovered.Tag = ReferenceEquals(hovered, Actions[ShotAction.Trash]) ? "danger" : null;
            hovered = row;
            if (hovered != null) hovered.Tag = ReferenceEquals(hovered, Actions[ShotAction.Trash]) ? "danger-hover" : "hover";
        }

        internal void SetDetails(string value) { details.Text = value; }

        internal void SetBulk(int index, int count)
        {
            BulkActions[ShotAction.RemoveLeft].IsEnabled = index > 0;
            BulkActions[ShotAction.RemoveRight].IsEnabled = index >= 0 && index < count - 1;
            BulkActions[ShotAction.RemoveOthers].IsEnabled = index >= 0 && count > 1;
            BulkActions[ShotAction.RemoveAll].IsEnabled = count > 0;
        }

        Button Action(string title, string icon, ShotAction action, Action<ShotAction> execute)
        {
            var color = LightTheme.Brush(action == ShotAction.Trash ? "#BB514E" : "#283343");
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new Viewbox { Width = 18, Height = 18, Child = new Path { Data = LightMenu.IconGeometry(icon), Stroke = color, StrokeThickness = 1.65, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round } });
            label.Children.Add(new TextBlock { Text = title, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var button = new Button {
                Content = label, Style = actionStyle, Tag = action == ShotAction.Trash ? "danger" : null,
                Foreground = color,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(button, title);
            button.Click += delegate { IsOpen = false; execute(action); };
            Actions.Add(action, button); return button;
        }

    }
}
