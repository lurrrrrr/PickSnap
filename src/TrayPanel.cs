using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ScreenLine
{
    internal sealed class TrayPanel : ContextMenu
    {
        static readonly ResourceDictionary styles = (ResourceDictionary)XamlReader.Parse(LightTheme.Sheet(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='Action' TargetType='Button'>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='#283343'/>
    <Setter Property='Height' Value='34'/><Setter Property='Padding' Value='10,0'/>
    <Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Border x:Name='Surface' Background='{TemplateBinding Background}' CornerRadius='8' BorderBrush='Transparent' BorderThickness='1'>
        <ContentPresenter Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='Tag' Value='primary'><Setter Property='Background' Value='#EDF4FF'/><Setter Property='Foreground' Value='#2463AE'/></Trigger>
        <Trigger Property='Tag' Value='keyboard'><Setter TargetName='Surface' Property='BorderBrush' Value='#6E97CB'/></Trigger>
        <Trigger Property='Tag' Value='open'><Setter TargetName='Surface' Property='Background' Value='#DFE6EE'/></Trigger>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#DFE6EE'/></Trigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='primary'/><Condition Property='IsMouseOver' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Background' Value='#D6E4F7'/></MultiTrigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Background' Value='#E5ECF5'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>"));

        internal readonly Button VisibilityButton, CaptureButton, BasketButton, TextButton, SettingsButton, ExitButton, AddButton;
        internal readonly CheckBox PicturesSwitch, TextSwitch;
        readonly TextBlock toggleShortcut = new TextBlock(), captureShortcut = new TextBlock();
        internal TrayPanel()
        {
            Width = 216; FocusVisualStyle = null; FontFamily = new FontFamily("Microsoft YaHei"); FontSize = 13;
            Foreground = LightTheme.Brush("#283343"); HasDropShadow = false; SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display); LightTheme.SoftwareRenderMenu(this);
            var panel = new StackPanel { Margin = new Thickness(4) };
            KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
            var heading = new Grid { Height = 28, Margin = new Thickness(12, 0, 12, 0) };
            heading.Children.Add(new TextBlock { Text = "PickSnap", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            heading.Children.Add(new TextBlock { Text = "v" + AppUpdates.CurrentVersion, FontSize = 11, Foreground = LightTheme.Brush("#65717F"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(heading); panel.Children.Add(Divider());
            VisibilityButton = Action("显示绳子", "show", toggleShortcut); panel.Children.Add(VisibilityButton);
            BasketButton = Action("图片篮子", "basket"); panel.Children.Add(BasketButton);
            TextButton = Action("文字信封", "envelope"); panel.Children.Add(TextButton);
            panel.Children.Add(Divider());
            AddButton = Action("添加图片", "add"); panel.Children.Add(AddButton);
            CaptureButton = Action("区域截图", "capture", captureShortcut); panel.Children.Add(CaptureButton);
            panel.Children.Add(Divider());
            PicturesSwitch = CollectionRow("收集图片", "add"); TextSwitch = CollectionRow("收集文字", "envelope");
            panel.Children.Add(new Border { Height = 28, Child = (Grid)PicturesSwitch.Tag }); panel.Children.Add(new Border { Height = 28, Child = (Grid)TextSwitch.Tag }); panel.Children.Add(Divider());
            SettingsButton = Action("设置", "settings"); panel.Children.Add(SettingsButton);
            ExitButton = Action("退出", "exit"); panel.Children.Add(ExitButton);
            var surface = new FrameworkElementFactory(typeof(Border));
            surface.SetValue(Border.MarginProperty, new Thickness(12)); surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            surface.SetValue(Border.BackgroundProperty, LightTheme.Brush("#FFFFFF")); surface.SetValue(Border.BorderBrushProperty, LightTheme.Brush("#E0E4EA")); surface.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            surface.SetValue(Border.EffectProperty, new System.Windows.Media.Effects.DropShadowEffect { Color = LightTheme.ShadowColor, BlurRadius = 18, ShadowDepth = 4, Opacity = LightTheme.Dark ? .3 : .14 });
            var content = new FrameworkElementFactory(typeof(ContentPresenter)); Items.Add(panel);
            content.SetBinding(ContentPresenter.ContentProperty, new Binding("Items[0]") { RelativeSource = RelativeSource.TemplatedParent }); surface.AppendChild(content);
            Template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = surface };
            Opened += delegate { ClearKeyboardFocus(); VisibilityButton.Focus(); Opacity = 0; BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(.12))); };
            new MenuDismissal(this, null, Close);
            PreviewMouseMove += delegate { ClearKeyboardFocus(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Escape) { Close(); args.Handled = true; return; }
                Dispatcher.BeginInvoke(new Action(delegate { foreach (var button in ActionButtons()) button.Tag = button.IsKeyboardFocused ? "keyboard" : null; }));
            };
        }
        Button[] ActionButtons() { return new[] { VisibilityButton, CaptureButton, BasketButton, TextButton, SettingsButton, ExitButton, AddButton }; }
        void ClearKeyboardFocus() { foreach (var button in ActionButtons()) if ((string)button.Tag == "keyboard") button.Tag = null; }
        internal void OnAction(Button button, Action action) { button.Click += delegate { Close(); action(); }; }
        internal void SetShortcuts(string toggle, string capture)
        {
            toggleShortcut.Text = toggle; captureShortcut.Text = capture;
            toggleShortcut.ToolTip = toggle; captureShortcut.ToolTip = capture;
            AutomationProperties.SetHelpText(VisibilityButton, toggle); AutomationProperties.SetHelpText(CaptureButton, capture);
        }
        internal void SyncCollection(bool pictures, bool text) { PicturesSwitch.IsChecked = pictures; TextSwitch.IsChecked = text; }
        internal void OnCollectionToggle(CheckBox box, Func<bool> read, Action<bool> change)
        {
            box.IsChecked = read();
            box.Click += delegate { try { change(box.IsChecked == true); } catch (Exception error) { box.IsChecked = read(); MessageBox.Show(error.Message, "PickSnap"); } };
        }
        static CheckBox CollectionRow(string title, string icon)
        {
            var box = new SwitchBox { Style = LightTheme.SwitchStyle, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            AutomationProperties.SetName(box, title);
            var label = new Grid { Margin = new Thickness(10, 0, 0, 0) }; label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); label.ColumnDefinitions.Add(new ColumnDefinition()); label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            label.Children.Add(LightTheme.Icon(icon, 16)); var text = new TextBlock { Text = title, LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(text, 1); label.Children.Add(text); Grid.SetColumn(box, 2); label.Children.Add(box);
            label.Background = Brushes.Transparent; label.Cursor = Cursors.Hand; label.MouseLeftButtonUp += delegate { if (box.IsMouseOver) return; box.IsChecked = box.IsChecked != true; box.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); }; box.Tag = label; return box;
        }
        internal void SetVisibility(bool shown) { ((TextBlock)((Grid)VisibilityButton.Content).Children[1]).Text = shown ? "收起绳子" : "显示绳子"; AutomationProperties.SetName(VisibilityButton, shown ? "收起绳子" : "显示绳子"); }
        internal void Close() { IsOpen = false; }
        static Button Action(string title, string icon, TextBlock shortcut = null)
        {
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(LightTheme.Icon(icon, 16));
            var label = new TextBlock { Text = title, LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(label, 1); grid.Children.Add(label);
            if (shortcut != null) { shortcut.FontSize = 11; shortcut.Foreground = LightTheme.Brush("#65717F"); shortcut.Margin = new Thickness(6, 0, 0, 0); shortcut.MaxWidth = 64; shortcut.TextTrimming = TextTrimming.CharacterEllipsis; shortcut.LineHeight = 18; shortcut.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; shortcut.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(shortcut, 2); grid.Children.Add(shortcut); }
            var button = new Button { Content = grid, Style = (Style)styles["Action"], Height = 28 };
            AutomationProperties.SetName(button, title); return button;
        }

        static Border Divider() { return new Border { Height = 1, Background = LightTheme.Brush("#ECEFF3"), Margin = new Thickness(10, 3, 10, 3) }; }
    }
}
