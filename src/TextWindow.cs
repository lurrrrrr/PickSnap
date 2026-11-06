using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ScreenLine
{
    internal static class TextEnvelope
    {
        static readonly Dictionary<string, BitmapSource> images = new Dictionary<string, BitmapSource>();
        static readonly Int32Rect[] bounds = {
            new Int32Rect(112, 73, 1031, 1103), new Int32Rect(136, 112, 982, 1031),
            new Int32Rect(184, 195, 887, 892), new Int32Rect(127, 90, 1001, 1062),
            new Int32Rect(112, 72, 1031, 1103)
        };

        public static BitmapSource Source(LineTheme theme)
        {
            BitmapSource image;
            if (images.TryGetValue(theme.Id, out image)) return image;
            image = SkinImages.Crop("PickSnap.Envelope." + theme.Id, bounds[Array.IndexOf(LineTheme.All, theme)]);
            images.Add(theme.Id, image);
            return image;
        }
    }

    public sealed class TextTime : IValueConverter
    {
        public object Convert(object value, Type type, object parameter, CultureInfo culture)
        {
            var time = ((DateTime)value).ToLocalTime();
            if (DateTime.Now - time < TimeSpan.FromMinutes(1)) return "刚刚";
            return (time.Date == DateTime.Today ? "今天 " : time.ToString("MM-dd ")) + time.ToString("HH:mm");
        }
        public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }

    internal sealed class TextWindow : Window
    {
        readonly TextCollection texts;
        readonly ShelfWindow shelf;
        readonly TextBlock count = new TextBlock();
        readonly TextBlock feedback = new TextBlock { Foreground = LightTheme.Brush("#65717F"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock empty = new TextBlock { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = LightTheme.Brush("#65717F"), Margin = new Thickness(20) };
        readonly System.Windows.Shapes.Path pointer = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,9 L8,1 L16,9"), Fill = LightTheme.Brush("#FFFFFF"), Stroke = LightTheme.Brush("#E0E4EA"), StrokeThickness = 1, Width = 16, Height = 10, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        Action updateOutline;

        readonly DispatcherTimer feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        // 信封收起后的孤儿面板自毁定时器：收集关着且一条文字都没有时，窗口没有存在意义。
        readonly DispatcherTimer orphanTimer = new DispatcherTimer();
        readonly Button undoButton = new Button { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        readonly TextEditHistory history;
        readonly Button undoOptions = new Button(), redoButton = new Button();
        internal readonly TextBox Search = new TextBox();
        internal readonly ListBox List = new ListBox();
        internal readonly Button SelectAllButton, DeleteSelectedButton;
        bool refreshing, confirming;
        string checkedSignature;
        internal int VisibleCount { get { return List.Items.Count; } }
        internal string Feedback { get { return feedback.Text; } }
        bool closed;

        public TextWindow(TextCollection texts, ShelfWindow shelf)
        {
            this.texts = texts; this.shelf = shelf; history = new TextEditHistory(texts);
            foreach (var item in texts.Items) item.IsMarked = false;
            Title = "文字 · PickSnap"; Owner = shelf;
            Width = 460; Height = 456;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            LightTheme.SoftwareRender(this);
            ShowInTaskbar = false; Topmost = true;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei"); FontSize = 14;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            AutomationProperties.SetAutomationId(this, "TextCollection");

            var root = new Grid(); Content = root;
            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
            body.RowDefinitions.Add(new RowDefinition());
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
            var card = LightTheme.Card(body); card.Margin = new Thickness(16, 11, 16, 16); card.Padding = new Thickness(0); card.CornerRadius = new CornerRadius(12);
            body.SizeChanged += delegate { body.Clip = new RectangleGeometry(new Rect(0, 0, body.ActualWidth, body.ActualHeight), 11, 11); };
            card.Effect = null;
            pointer.Stroke = card.BorderBrush;
            pointer.Fill = card.Background;
            pointer.StrokeThickness = card.BorderThickness.Top;
            pointer.StrokeLineJoin = PenLineJoin.Round;
            root.Children.Add(card); root.Children.Add(pointer);
            // Clip the window surface itself, not only its children: transparent
            // corners must never expose a rectangular backing surface.
            updateOutline = delegate {
                var shape = new GeometryGroup { FillRule = FillRule.Nonzero };
                shape.Children.Add(new RectangleGeometry(new Rect(16, 11, Math.Max(0, root.ActualWidth - 32), Math.Max(0, root.ActualHeight - 27)), 12, 12));
                if (pointer.Visibility == Visibility.Visible) {
                    var tip = pointer.Data.Clone(); tip.Transform = new TranslateTransform(pointer.Margin.Left, pointer.Margin.Top); shape.Children.Add(tip);
                    shape.Children.Add(tip.GetWidenedPathGeometry(new Pen(pointer.Stroke, pointer.StrokeThickness + 1)));
                }
                root.Clip = shape;
            };

            root.SizeChanged += delegate { updateOutline(); };

            var header = new Grid { Margin = new Thickness(18, 0, 14, 0) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var instruction = new TextBlock { Text = "单击选择，双击阅读全文", FontSize = 11, Foreground = LightTheme.Brush("#65717F"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(6, 0, 10, 0) };
            Grid.SetColumn(instruction, 1); header.Children.Add(instruction);
            var heading = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            heading.Children.Add(new TextBlock { Text = "文字", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = LightTheme.Brush("#283343") });
            count.FontSize = 12; count.Foreground = LightTheme.Brush("#536172");
            heading.Children.Add(new Border { Child = count, Background = LightTheme.Brush("#F0F3F7"), CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
            undoButton.Content = HistoryIcon(false, undoButton); undoButton.ToolTip = "撤销一条删除（Ctrl+Z）";
            undoButton.Style = LightTheme.ButtonStyle;
            undoButton.FontSize = 12; undoButton.MinWidth = 0; undoButton.Padding = new Thickness(8, 2, 8, 2);
            undoButton.Height = 26; undoButton.Width = 26; undoButton.Foreground = LightTheme.Brush("#185FA5");
            undoButton.Click += delegate { RunUndo(); };
            heading.Children.Add(undoButton);
            undoOptions.Content = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L4,4 L8,0"), Stroke = LightTheme.Brush("#65717F"), StrokeThickness = 1.5, Width = 8, Height = 4 }; undoOptions.ToolTip = "更多撤销方式";
            redoButton.Content = HistoryIcon(true, redoButton); redoButton.ToolTip = "重做删除（Ctrl+Y）";
            foreach (var button in new[] { undoOptions, redoButton }) { button.Style = LightTheme.ButtonStyle; button.Tag = "quiet"; button.MinWidth = 0; button.Width = 26; button.Height = 26; button.Padding = new Thickness(0); button.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(button); }
            undoButton.Tag = "quiet"; undoButton.Padding = new Thickness(0);
            undoOptions.Click += delegate
            {
                var menu = new ContextMenu { FontFamily = LightTheme.UiFont };
                var one = new MenuItem { Header = "撤销一条删除", IsEnabled = history.PendingCount > 0 }; one.Click += delegate { RunUndo(); }; menu.Items.Add(one);
                var all = new MenuItem { Header = "撤销本批删除（" + history.PendingCount + " 条）", IsEnabled = history.PendingCount > 0 }; all.Click += delegate { RunUndo(true); }; menu.Items.Add(all);
                menu.PlacementTarget = undoOptions; menu.IsOpen = true;
            };
            redoButton.Click += delegate { try { int redone = history.Redo(); ShowFeedback("已重新删除 " + redone + " 条"); } catch (Exception error) { texts.Store.Log(error); ShowFeedback("重做未完成，请重试"); } UpdateHistory(); };
            AutomationProperties.SetName(undoButton, "撤销一条删除"); AutomationProperties.SetName(undoOptions, "更多撤销方式"); AutomationProperties.SetName(redoButton, "重做删除");
            header.Children.Add(heading);
            var close = LightTheme.Button("", Close, "quiet"); close.Content = LightTheme.CloseIcon();
            close.Width = close.Height = 28; close.MinWidth = close.MinHeight = 0; close.VerticalAlignment = VerticalAlignment.Center; close.Padding = new Thickness(0); close.HorizontalAlignment = HorizontalAlignment.Right;
            AutomationProperties.SetName(close, "关闭文字"); Grid.SetColumn(close, 2); header.Children.Add(close); body.Children.Add(header);

            var searchPanel = new Grid { Margin = new Thickness(20, 0, 30, 10) };
            Search.BorderThickness = new Thickness(0); Search.Background = Brushes.Transparent; Search.Padding = new Thickness(13, 8, 13, 8);
            Search.VerticalContentAlignment = VerticalAlignment.Center; Search.FontSize = 13; Search.Foreground = LightTheme.Brush("#283343");
            AutomationProperties.SetName(Search, "搜索文字");
            searchPanel.Children.Add(new Border { CornerRadius = new CornerRadius(7), BorderBrush = LightTheme.Brush("#DCE2EA"), BorderThickness = new Thickness(1), Child = Search });
            var placeholder = new TextBlock { Text = "搜索文字", IsHitTestVisible = false, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Foreground = LightTheme.Brush("#8390A1") };
            searchPanel.Children.Add(placeholder);
            // 占位符只在「未聚焦且为空」时显示：一旦点进搜索框就隐藏，避免和插入光标叠在一起。
            Action syncPlaceholder = delegate { placeholder.Visibility = Search.Text.Length == 0 && !Search.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed; };
            Search.TextChanged += delegate { confirming = false; syncPlaceholder(); Refresh(); };
            Search.GotKeyboardFocus += delegate { syncPlaceholder(); };
            Search.LostKeyboardFocus += delegate { syncPlaceholder(); };
            // 焦点可能在 Loaded 之后才被设置，延迟一拍再校准一次。
            Search.Loaded += delegate { Dispatcher.BeginInvoke(new Action(syncPlaceholder), System.Windows.Threading.DispatcherPriority.Loaded); };
            Grid.SetRow(searchPanel, 1); body.Children.Add(searchPanel);

            List.BorderThickness = new Thickness(0); List.Background = Brushes.Transparent; List.Margin = new Thickness(10, 0, 10, 10);
            List.FontFamily = FontFamily; List.FontSize = 14;
            List.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            List.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            List.SetValue(ScrollViewer.CanContentScrollProperty, true);
            VirtualizingPanel.SetIsVirtualizing(List, true); VirtualizingPanel.SetVirtualizationMode(List, VirtualizationMode.Recycling);
            VirtualizingPanel.SetScrollUnit(List, ScrollUnit.Pixel);
            List.ItemContainerStyle = (Style)XamlReader.Parse(LightTheme.Sheet(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ListBoxItem'>
              <Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Padding' Value='0'/><Setter Property='Margin' Value='0,0,0,6'/><Setter Property='Background' Value='Transparent'/>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'><Border x:Name='Row' Background='{TemplateBinding Background}' CornerRadius='7' BorderBrush='Transparent' BorderThickness='1'><ContentPresenter/></Border>
              <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Row' Property='Background' Value='#F4F7FB'/></Trigger>
              <DataTrigger Binding='{Binding IsMarked}' Value='True'><Setter TargetName='Row' Property='Background' Value='#EDF4FF'/><Setter TargetName='Row' Property='BorderBrush' Value='#CFE1F5'/></DataTrigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>"));
            List.ItemTemplate = (DataTemplate)XamlReader.Parse(LightTheme.Sheet(@"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:local='clr-namespace:ScreenLine;assembly=__ASSEMBLY__'>
              <DataTemplate.Resources><local:TextTime x:Key='TextTime'/><Geometry x:Key='CopyIcon'>__COPY__</Geometry><Geometry x:Key='TrashIcon'>__TRASH__</Geometry><Style x:Key='IconButton' TargetType='Button'>
                <Setter Property='Width' Value='28'/><Setter Property='Height' Value='30'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Foreground' Value='#647386'/><Setter Property='Background' Value='Transparent'/>
                <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='Surface' Background='{TemplateBinding Background}' CornerRadius='5'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
                <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#E7EEF8'/><Setter Property='Foreground' Value='#2463AE'/></Trigger>
                <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#6E97CB'/><Setter TargetName='Surface' Property='BorderThickness' Value='1'/></Trigger>
                <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='delete'/><Condition Property='IsMouseOver' Value='True'/></MultiTrigger.Conditions><Setter Property='Foreground' Value='#B74E4A'/><Setter TargetName='Surface' Property='Background' Value='#FFF0ED'/></MultiTrigger>
                </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style></DataTemplate.Resources>
              <Border Padding='12,10,8,10'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='64'/></Grid.ColumnDefinitions>
                <CheckBox IsChecked='{Binding IsMarked, Mode=TwoWay}' Tag='select' AutomationProperties.Name='选择文字' Width='22' Height='22' HorizontalAlignment='Left' VerticalAlignment='Top' Cursor='Hand'>
                  <CheckBox.Template><ControlTemplate TargetType='CheckBox'>
                    <Border Background='Transparent'>
                      <Border x:Name='Mark' Width='18' Height='18' CornerRadius='9' Background='#FFFFFF' BorderBrush='#8BA4C4' BorderThickness='1' Opacity='0.65' HorizontalAlignment='Center' VerticalAlignment='Center'>
                        <Path x:Name='Tick' Width='10' Height='7' Data='M0,3 L3,6 L9,0' Stroke='White' StrokeThickness='1.6' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/>
                      </Border>
                    </Border>
                    <ControlTemplate.Triggers>
                      <DataTrigger Binding='{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=ListBoxItem}}' Value='True'><Setter TargetName='Mark' Property='Opacity' Value='1'/></DataTrigger>
                      <DataTrigger Binding='{Binding IsSelected, RelativeSource={RelativeSource AncestorType=ListBoxItem}}' Value='True'><Setter TargetName='Mark' Property='Opacity' Value='1'/></DataTrigger>
                      <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Mark' Property='Opacity' Value='1'/></Trigger>
                      <Trigger Property='IsChecked' Value='True'><Setter TargetName='Mark' Property='Opacity' Value='1'/><Setter TargetName='Mark' Property='Background' Value='#2463AE'/><Setter TargetName='Mark' Property='BorderBrush' Value='#2463AE'/><Setter TargetName='Tick' Property='Visibility' Value='Visible'/></Trigger>
                      <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate></CheckBox.Template>
                </CheckBox>
                <StackPanel Margin='28,0,8,0'>
                  <Grid><TextBlock x:Name='Preview' Text='{Binding Content}' TextWrapping='Wrap' TextTrimming='CharacterEllipsis' MaxHeight='44' LineHeight='22' Foreground='#283343'/>
                  <TextBox x:Name='FullText' Text='{Binding Content, Mode=OneWay}' IsReadOnly='True' TextWrapping='Wrap' VerticalScrollBarVisibility='Disabled' BorderThickness='0' Padding='0' Background='Transparent' Foreground='#283343' Visibility='Collapsed'/></Grid>
                  <TextBlock Text='{Binding CreatedUtc, Converter={StaticResource TextTime}}' FontSize='11' Foreground='#8390A1' Margin='0,5,0,0'/>
                </StackPanel>
                <StackPanel Grid.Column='1' Orientation='Horizontal' VerticalAlignment='Top'>
                  <Button Tag='copy' ToolTip='复制' AutomationProperties.Name='复制文字' Style='{StaticResource IconButton}' Margin='0,0,4,0'><Viewbox Width='16' Height='16'><Path Data='{StaticResource CopyIcon}' Stroke='{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}' StrokeThickness='1.65' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/></Viewbox></Button>
                  <Button Tag='delete' ToolTip='删除' AutomationProperties.Name='删除文字' Style='{StaticResource IconButton}'><Viewbox Width='16' Height='16'><Path Data='{StaticResource TrashIcon}' Stroke='{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}' StrokeThickness='1.65' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/></Viewbox></Button>
                </StackPanel>
              </Grid></Border>

            </DataTemplate>".Replace("__ASSEMBLY__", Assembly.GetExecutingAssembly().GetName().Name)
                .Replace("__COPY__", LightMenu.IconGeometry("copy").ToString(CultureInfo.InvariantCulture))
                .Replace("__TRASH__", LightMenu.IconGeometry("trash").ToString(CultureInfo.InvariantCulture))));
            List.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                var item = RowFromClick(e.OriginalSource as DependencyObject);
                if (item == null) return;
                e.Handled = true; HandleRowClick(item, e.ClickCount, IsTruncatedPreview(e.OriginalSource as DependencyObject), e.Timestamp);
            };
            List.AddHandler(Button.ClickEvent, new RoutedEventHandler(RowAction));
            List.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, new RoutedEventHandler(SelectionChanged));
            List.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, new RoutedEventHandler(SelectionChanged));
            var listPanel = new Grid(); listPanel.Children.Add(List); listPanel.Children.Add(empty);
            Grid.SetRow(listPanel, 2); body.Children.Add(listPanel);
            var batch = new Grid { Margin = new Thickness(18, 0, 18, 6) };
            // 底部两枚按钮统一为 128×34 的实体按钮：全选走白底描边样式，删除所选走 danger 样式（可用=深红、禁用=浅粉），与图片篮子一致。
            SelectAllButton = LightTheme.Button("全选", SelectAll, "select"); SelectAllButton.HorizontalAlignment = HorizontalAlignment.Left;
            DeleteSelectedButton = LightTheme.Button("删除所选", DeleteSelected, "danger");
            SelectAllButton.Width = DeleteSelectedButton.Width = 128;
            SelectAllButton.Height = DeleteSelectedButton.Height = 36;
            DeleteSelectedButton.HorizontalAlignment = HorizontalAlignment.Right;
            feedback.HorizontalAlignment = HorizontalAlignment.Center; feedback.MaxWidth = 120; feedback.TextTrimming = TextTrimming.CharacterEllipsis; batch.Children.Add(feedback);
            batch.Children.Add(SelectAllButton); batch.Children.Add(DeleteSelectedButton); Grid.SetRow(batch, 3); body.Children.Add(batch);

            feedbackTimer.Tick += delegate { feedback.Text = "";  feedbackTimer.Stop(); };
            // 到点复核一次：期间用户撤销了删除或重新开启收集的话，条件不再成立就继续留着。
            orphanTimer.Tick += delegate
            {
                orphanTimer.Stop();
                if (!closed && !texts.CollectClipboard && texts.Items.Count == 0) Close();
            };
            SourceInitialized += delegate { Position(); };
            Loaded += delegate { Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Position)); };
            DpiChanged += delegate { Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Position)); };
            shelf.SizeChanged += ShelfMoved; shelf.LocationChanged += ShelfMoved; shelf.RopeHidden += Close;
            Deactivated += delegate { if (IsVisible) Close(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (!Search.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z) { RunUndo(); e.Handled = true; } else if (!Search.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y) { redoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; } else if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
            texts.Changed += Refresh;
            Closed += delegate { closed = true; texts.Changed -= Refresh; shelf.SizeChanged -= ShelfMoved; shelf.LocationChanged -= ShelfMoved; shelf.RopeHidden -= Close; feedbackTimer.Stop(); orphanTimer.Stop(); };
            Refresh(); UpdateHistory();
        }

        void ShelfMoved(object sender, EventArgs e) { Position(); }

        internal void Position()
        {
            if (closed) return;
            var area = shelf.Display.WorkingArea;
            double dpi = WindowsNative.MonitorScale(shelf.Display);
            var anchor = shelf.TextAnchor;
            Width = Math.Min(460, area.Width / dpi - 16);
            Height = Math.Min(456, Math.Max(180, (area.Bottom - Math.Max(area.Top, anchor.Y)) / dpi - 8));
            double left = Math.Max(area.Left + 8 * dpi, Math.Min(area.Right - (Width + 8) * dpi, anchor.X - (Width - 76) * dpi));
            double top = Math.Max(area.Top + 4 * dpi, Math.Min(area.Bottom - Height * dpi, anchor.Y));
            // 信封没摆出来时窗口退到屏幕右上，小三角指向的是虚空，藏掉。
            pointer.Visibility = shelf.TextButton.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            pointer.Margin = new Thickness(Math.Max(30, Math.Min(Width - 54, (anchor.X - left) / dpi - 8)), 3, 0, 0);
            if (updateOutline != null) updateOutline();
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero) WindowsNative.SetWindowPos(handle, new IntPtr(-1), (int)Math.Round(left), (int)Math.Round(top), (int)Math.Round(Width * dpi), (int)Math.Round(Height * dpi), 0x10);
        }

        void Refresh()
        {
            if (closed) return;
            var selected = List.SelectedItem;
            var scroll = FindScroll(List);
            double offset = scroll == null ? 0 : scroll.VerticalOffset;
            string query = Search.Text.Trim();
            refreshing = true;
            var visible = texts.Items.Where(item => item.Content.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0).ToArray();
            foreach (var item in texts.Items.Except(visible)) item.IsMarked = false;
            List.ItemsSource = visible;
            refreshing = false; UpdateBatch();
            List.SelectedItem = selected;
            count.Text = texts.Items.Count.ToString();
            empty.Text = texts.Items.Count == 0 ? texts.CollectClipboard
                ? "还没有文字\n复制的文字会自动出现在这里"
                : "尚未开启文字收集\n在托盘「剪贴板收集」中开启「文字」" : "没有找到匹配的文字";
            empty.Visibility = List.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            // 文字清空且收集也关着时信封会收起（ShowEnvelope 为假），窗口就只剩一块
            // 「尚未开启」的空壳——自己收掉。正在展示可撤销的删除反馈时多等一拍，
            // 让用户来得及点「撤销」；平时稍候即关。
            if (!texts.CollectClipboard && texts.Items.Count == 0)
            {
                orphanTimer.Interval = history.PendingCount > 0 ? TimeSpan.FromSeconds(5.5) : TimeSpan.FromMilliseconds(700);
                if (!orphanTimer.IsEnabled) orphanTimer.Start();
            }
            else orphanTimer.Stop();
            // 文字增删会改变信封的显隐（清空且收集关着时信封收起），锚点跟着变，重算一次位置。
            Position();
            if (scroll != null) scroll.ScrollToVerticalOffset(offset);
        }

        void SelectionChanged(object sender, RoutedEventArgs e) { if (!refreshing) UpdateBatch(); }

        void UpdateBatch()
        {
            var items = List.Items.Cast<TextItem>().ToArray();
            int selected = items.Count(item => item.IsMarked);
            string signature = string.Join("|", items.Where(item => item.IsMarked).Select(item => item.Id).OrderBy(id => id));
            if (signature != checkedSignature) confirming = false;
            checkedSignature = signature;
            SelectAllButton.IsEnabled = items.Length > 0;
            SelectAllButton.Content = selected == items.Length && selected > 0 ? "取消全选" : "全选";
            DeleteSelectedButton.IsEnabled = selected > 0;
            DeleteSelectedButton.Content = confirming ? "确认删除 " + selected + " 条" : selected == 0 ? "删除所选" : "删除所选（" + selected + "）";
        }

        void SelectAll()
        {
            var items = List.Items.Cast<TextItem>().ToArray();
            bool mark = !items.All(item => item.IsMarked);
            refreshing = true; foreach (var item in items) item.IsMarked = mark; refreshing = false;
            UpdateBatch();
        }

        void DeleteSelected()
        {
            var items = List.Items.Cast<TextItem>().Where(item => item.IsMarked).ToArray();
            if (items.Length == 0) return;
            if (!confirming) { confirming = true; DeleteSelectedButton.Content = "确认删除 " + items.Length + " 条"; return; }
            try
            {
                history.Delete(items); ShowFeedback("已删除 " + items.Length + " 条"); UpdateHistory();
            }
            catch (Exception error) { texts.Store.Log(error); ShowFeedback("删除未完成，请重试"); UpdateBatch(); }
        }

        static ScrollViewer FindScroll(DependencyObject value)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(value); i++)
            {
                var child = VisualTreeHelper.GetChild(value, i);
                var scroll = child as ScrollViewer;
                if (scroll != null) return scroll;
                scroll = FindScroll(child); if (scroll != null) return scroll;
            }
            return null;
        }

        internal TextItem RowFromClick(DependencyObject source)
        {
            while (source != null && source != List)
            {
                if (source is System.Windows.Controls.Primitives.ButtonBase) return null;
                var row = source as ListBoxItem;
                if (row != null) return row.DataContext as TextItem;
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }
        TextItem previousClickItem;
        int previousClickTime;
        bool previousClickOnPreview;
        internal bool IsTruncatedPreview(DependencyObject source)
        {
            while (source != null && !(source is ListBoxItem))
            {
                var preview = source as TextBlock;
                if (preview != null && preview.Name == "Preview" && preview.ActualWidth > 0)
                {
                    var text = new FormattedText(preview.Text, CultureInfo.CurrentUICulture, preview.FlowDirection,
                        new Typeface(preview.FontFamily, preview.FontStyle, preview.FontWeight, preview.FontStretch),
                        preview.FontSize, preview.Foreground, VisualTreeHelper.GetDpi(preview).PixelsPerDip);
                    text.MaxTextWidth = preview.ActualWidth; text.LineHeight = preview.LineHeight;
                    return text.Height > preview.MaxHeight + 0.5;
                }
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }
        internal void HandleRowClick(TextItem item, int clickCount, bool onTruncatedPreview = false, int timestamp = 0)
        {
            int elapsed = unchecked(timestamp - previousClickTime);
            bool read = clickCount == 2 && previousClickItem == item && previousClickOnPreview && onTruncatedPreview
                && elapsed >= 0 && elapsed <= Math.Min(260, System.Windows.Forms.SystemInformation.DoubleClickTime);
            previousClickItem = item; previousClickTime = timestamp; previousClickOnPreview = onTruncatedPreview;
            if (read) { previousClickOnPreview = false; OpenReader(item); return; }
            item.IsMarked = !item.IsMarked; UpdateBatch();
        }
        void OpenReader(TextItem item)
        {
            new TextReaderWindow(item, texts.Store) { Owner = shelf, WindowStartupLocation = WindowStartupLocation.CenterScreen }.Show();
            Close();
        }

        async void RowAction(object sender, RoutedEventArgs e)
        {
            var button = e.OriginalSource as Button;
            if (button == null) return;
            var item = button.DataContext as TextItem;
            if (item == null) return;
            e.Handled = true;
            button.IsEnabled = false;
            try
            {
                if ((string)button.Tag == "delete")
                {
                    history.Delete(new[] { item }); ShowFeedback("已删除"); UpdateHistory();
                }
                else
                {
                    var data = new DataObject(); data.SetText(item.Content, TextDataFormat.UnicodeText); data.SetData(ClipboardRules.OwnFormat, "1");
                    await WindowsClipboard.Write(data); ShowFeedback("已复制");
                }
            }
            catch (Exception error) { texts.Store.Log(error); ShowFeedback("操作未完成，请重试"); }
            finally { button.IsEnabled = true; }
        }

        void ShowFeedback(string value)
        {
            if (closed) return;

            feedbackTimer.Interval = TimeSpan.FromSeconds(2);
            feedback.Text = value; feedbackTimer.Stop(); feedbackTimer.Start();
        }

        void UpdateHistory()
        {
            undoButton.Visibility = Visibility.Visible; undoButton.IsEnabled = undoOptions.IsEnabled = history.PendingCount > 0; redoButton.IsEnabled = history.CanRedo;
            if (!closed && !texts.CollectClipboard && texts.Items.Count == 0 && history.PendingCount > 0)
            {
                orphanTimer.Stop(); orphanTimer.Interval = TimeSpan.FromSeconds(5.5); orphanTimer.Start();
            }
        }
        static UIElement HistoryIcon(bool redo, Button button)
        {
            var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(redo ? "M15,4 L20,9 L15,14 M20,9 H10 Q4,9 4,16 V20" : "M9,4 L4,9 L9,14 M4,9 H14 Q20,9 20,16 V20"), StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
            var canvas = new Canvas { Width = 24, Height = 24 }; canvas.Children.Add(path); return new Viewbox { Width = 17, Height = 17, Child = canvas };
        }
        void RunUndo(bool wholeBatch = false)
        {
            try { int restored = history.Undo(wholeBatch); if (restored > 0) ShowFeedback("已恢复 " + restored + " 条"); }
            catch (Exception error) { texts.Store.Log(error); ShowFeedback("恢复未完成，请重试"); }
            UpdateHistory();
        }
    }
}
