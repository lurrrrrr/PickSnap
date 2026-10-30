using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScreenLine
{
    internal sealed class LightNotice : Window
    {
        readonly DispatcherTimer timer;

        // plain=true：纯文字轻提示（无标题行、无关闭按钮），按秒数自动消失——用于「已是最新版」这类一两句话的确认。
        public LightNotice(string message, int seconds = 5, bool plain = false)
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            Title = "拾帧提示";
            AutomationProperties.SetAutomationId(this, "LightNotice");
            Width = plain ? 300 : 380; SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true;
            FontFamily = LightTheme.UiFont;
            LightTheme.Polish(this);
            var content = new StackPanel();
            if (!plain)
            {
                var heading = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                heading.Children.Add(new TextBlock { Text = "PickSnap · 拾帧", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = LightTheme.Brush("#283343"), VerticalAlignment = VerticalAlignment.Center });
                var close = LightTheme.Button("×", Close, "quiet");
                close.Width = close.Height = 24; close.MinWidth = 0; close.Padding = new Thickness(0); close.FontSize = 18;
                close.HorizontalAlignment = HorizontalAlignment.Right;
                AutomationProperties.SetName(close, "关闭提示");
                heading.Children.Add(close); content.Children.Add(heading);
            }
            content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 21, Foreground = LightTheme.Brush("#65717F"), HorizontalAlignment = plain ? HorizontalAlignment.Center : HorizontalAlignment.Left });
            var card = LightTheme.Card(content); card.Padding = plain ? new Thickness(18, 10, 18, 11) : new Thickness(20, 16, 20, 18);
            Content = card;
            SourceInitialized += delegate { WindowsNative.PrepareOverlay(new WindowInteropHelper(this).Handle); };
            Loaded += delegate { Left = SystemParameters.WorkArea.Right - ActualWidth - 8; Top = SystemParameters.WorkArea.Bottom - ActualHeight - 8; timer.Start(); };
            // 带关闭按钮的提示悬停时暂停倒计时；纯文字轻提示不暂停——它常弹在光标落点附近，
            // 一暂停就永远不走，看起来像「停留特别久」。
            if (!plain)
            {
                MouseEnter += delegate { timer.Stop(); };
                MouseLeave += delegate { timer.Start(); };
            }
            timer.Tick += delegate { Close(); };
            Closed += delegate { timer.Stop(); };
        }
    }
}
