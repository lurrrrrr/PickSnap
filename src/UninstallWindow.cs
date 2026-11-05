using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ScreenLine
{
    internal sealed class UninstallWindow : Window
    {
        internal readonly RadioButton KeepData, RemoveData;
        internal readonly CheckBox ConfirmDeletion;
        internal readonly Button UninstallButton;
        public UninstallWindow(Func<bool, Task<bool>> uninstall, string dataPath)
        {
            Title = "卸载 PickSnap"; Width = 540; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Topmost = true;
            FontFamily = LightTheme.UiFont; Background = LightTheme.Brush("#FFFFFF"); Foreground = LightTheme.Brush("#283343"); LightTheme.Polish(this);
            var body = new StackPanel { Margin = new Thickness(28) };
            body.Children.Add(new TextBlock { Text = "卸载 PickSnap", FontSize = 24, FontWeight = FontWeights.SemiBold });
            body.Children.Add(new TextBlock { Text = Maintenance.IsInstalled ? "选择是否保留图片、文字和设置。" : "退出后，请删除 PickSnap 所在文件夹以移除程序。", TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 10, 0, 20) });
            KeepData = new RadioButton { Foreground = LightTheme.Brush("#283343"), Content = "保留图片、文字与设置（推荐）", IsChecked = true, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) };
            RemoveData = new RadioButton { Foreground = LightTheme.Brush("#283343"), Content = "删除当前数据", FontSize = 14, Margin = new Thickness(0, 18, 0, 8) };
            body.Children.Add(KeepData); body.Children.Add(new TextBlock { Text = "图片、文字和设置会保留，下次安装可继续使用。", TextWrapping = TextWrapping.Wrap, Foreground = LightTheme.Brush("#65717F"), Margin = new Thickness(20, 0, 0, 0) });
            body.Children.Add(RemoveData); body.Children.Add(new TextBlock { Text = "删除 PickSnap 保存的图片、文字和设置。", TextWrapping = TextWrapping.Wrap, Foreground = LightTheme.Brush("#65717F"), Margin = new Thickness(20, 0, 0, 8) });
            ConfirmDeletion = new CheckBox { Content = new TextBlock { Text = "我确认删除当前图片、文字和设置，此操作无法撤销", TextWrapping = TextWrapping.Wrap }, Visibility = Visibility.Collapsed, Foreground = LightTheme.Brush("#B74E4A"), Margin = new Thickness(20, 4, 0, 4) }; body.Children.Add(ConfirmDeletion);
            var location = new StackPanel { Margin = new Thickness(0, 18, 0, 20) };
            location.Children.Add(new TextBlock { Text = "数据保存位置", FontSize = 13, Foreground = LightTheme.Brush("#283343") });
            var locationPath = new TextBlock { Text = "保留到：" + Maintenance.RetainedDataPath, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = LightTheme.Brush("#65717F") };
            location.Children.Add(locationPath); body.Children.Add(location);
            var status = new TextBlock { Text = "", FontSize = 12, LineHeight = 20, TextWrapping = TextWrapping.Wrap, Foreground = LightTheme.Brush("#65717F"), Margin = new Thickness(0, 0, 0, 18) }; body.Children.Add(status);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = LightTheme.Button("取消", Close); cancel.Margin = new Thickness(0, 0, 10, 0); actions.Children.Add(cancel);
            UninstallButton = LightTheme.Button("卸载并保留数据", delegate { }, "danger-primary"); actions.Children.Add(UninstallButton); body.Children.Add(actions); LightTheme.FitDialog(this, body);
            foreach (var button in new[] { cancel, UninstallButton })
            {
                button.FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei"); button.FontWeight = FontWeights.Normal;
                button.Height = 36; button.Padding = new Thickness(16, 0, 16, 0);
                button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center;
                var label = new FrameworkElementFactory(typeof(TextBlock));
                label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
                label.SetValue(TextBlock.LineStackingStrategyProperty, LineStackingStrategy.BlockLineHeight);
                label.SetValue(TextBlock.LineHeightProperty, 18.0);
                button.ContentTemplate = new DataTemplate { VisualTree = label };
            }
            Action update = delegate { bool remove = RemoveData.IsChecked == true;
                locationPath.Text = remove ? "将删除：" + dataPath : "保留到：" + Maintenance.RetainedDataPath; ConfirmDeletion.Visibility = remove ? Visibility.Visible : Visibility.Collapsed; UninstallButton.IsEnabled = (!remove || ConfirmDeletion.IsChecked == true); UninstallButton.Content = remove ? "卸载并删除当前数据" : "卸载并保留数据"; };
            KeepData.Checked += delegate { update(); }; RemoveData.Checked += delegate { update(); }; ConfirmDeletion.Checked += delegate { update(); }; ConfirmDeletion.Unchecked += delegate { update(); }; update();
            bool busy = false;
            UninstallButton.Click += async delegate { if (busy) return; busy = true; UninstallButton.IsEnabled = cancel.IsEnabled = false; try { if (await uninstall(RemoveData.IsChecked == true)) { busy = false; cancel.IsEnabled = true; Close(); } else status.Text = "卸载未开始，请先完成图片编辑。"; } catch (Exception error) { status.Text = "卸载未开始：" + error.Message; } finally { busy = false; cancel.IsEnabled = true; update(); } };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (busy && !cancel.IsEnabled) e.Cancel = true; };
        }
    }
}
