using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenLine
{
    internal sealed class TextReaderWindow : Window
    {
        internal readonly TextBox FullText;
        public TextReaderWindow(TextItem item, StateStore store)
        {
            Title = "阅读全文 · PickSnap"; Width = 680; Height = 560; MinWidth = 420; MinHeight = 300;
            var screen = WindowsNative.ScreenAtPointer(); double scale = WindowsNative.MonitorScale(screen);
            Width = Math.Min(Width, screen.WorkingArea.Width / scale - 24); Height = Math.Min(Height, screen.WorkingArea.Height / scale - 24); MinWidth = Math.Min(MinWidth, Width); MinHeight = Math.Min(MinHeight, Height);
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Topmost = true; ShowInTaskbar = false;
            FontFamily = LightTheme.UiFont; Background = LightTheme.Brush("#FFFFFF"); Foreground = LightTheme.Brush("#283343");
            LightTheme.Polish(this);
            var body = new Grid { Margin = new Thickness(24) }; body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition()); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.Children.Add(new TextBlock { Text = "文字详情", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
            FullText = new TextBox { Text = item.Content, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16), FontSize = 14, Background = LightTheme.Brush("#F5F7FA"), Foreground = Foreground, BorderBrush = LightTheme.Brush("#DCE2EA") };
            FullText.Style = LightTheme.FieldStyle;
            Loaded += delegate { FullText.Select(0, 0); FullText.ScrollToHome(); };
            System.Windows.Automation.AutomationProperties.SetName(FullText, "完整文字内容"); Grid.SetRow(FullText, 1); body.Children.Add(FullText);
            var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) }; footer.Children.Add(new TextBlock { Text = item.Content.Length + " 字符 · 可选择文字复制", Foreground = LightTheme.Brush("#65717F"), VerticalAlignment = VerticalAlignment.Center });
            var copy = LightTheme.Button("复制全文", delegate { }, "primary"); copy.HorizontalAlignment = HorizontalAlignment.Right;
            copy.Click += async delegate { copy.IsEnabled = false; try { var data = new DataObject(); data.SetText(item.Content); data.SetData(ClipboardRules.OwnFormat, "1"); await WindowsClipboard.Write(data); copy.Content = "已复制"; } catch (Exception error) { store.Log(error); copy.Content = "复制失败，重试"; } finally { copy.IsEnabled = true; } };
            footer.Children.Add(copy); Grid.SetRow(footer, 2); body.Children.Add(footer); Content = body;
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        }
    }
}
