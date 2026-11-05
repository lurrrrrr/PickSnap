using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ScreenLine
{
    internal sealed class UpdateWindow : Window
    {
        // 把 GitHub Release 的 Markdown 正文渲染成接近网页的排版：# 标题、- 列表、**加粗**、[文字](链接)。
        // 之前直接把原文塞进 TextBlock，用户看到的是「- 」「## 」这类记号本身。
        static UIElement NotesView(string markdown)
        {
            var list = new StackPanel();
            foreach (var raw in (markdown ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) { list.Children.Add(new Border { Height = 6 }); continue; }
                string bullet = null;
                if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("• ")) { bullet = "•"; line = line.Substring(2).Trim(); }
                if (line.StartsWith("## ")) { line = line.Substring(3).Trim(); bullet = null; }
                else if (line.StartsWith("### ")) { line = line.Substring(4).Trim(); bullet = null; }
                else if (line.StartsWith("# ")) line = line.Substring(2).Trim();
                if (bullet != null)
                {
                    var row = new Grid { Margin = new Thickness(2, 0, 0, 5) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var dot = new TextBlock { Text = "•", FontSize = 13, Foreground = LightTheme.Brush("#8390A1"), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top };
                    Grid.SetColumn(dot, 0);
                    var text = new TextBlock { FontSize = 13, LineHeight = 21, TextWrapping = TextWrapping.Wrap, Foreground = LightTheme.Brush("#343D49") };
                    Grid.SetColumn(text, 1);
                    FillInlines(text, line);
                    row.Children.Add(dot); row.Children.Add(text);
                    list.Children.Add(row);
                    continue;
                }
                bool heading = raw.TrimStart().StartsWith("#");
                var block = new TextBlock { FontSize = heading ? 14 : 13, LineHeight = heading ? 22 : 21, TextWrapping = TextWrapping.Wrap,
                    FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = LightTheme.Brush(heading ? "#283343" : "#343D49"), Margin = new Thickness(0, 0, 0, 5) };
                FillInlines(block, line);
                list.Children.Add(block);
            }
            return list;
        }

        static void FillInlines(TextBlock block, string text)
        {
            // 链接只保留文字：更新窗口里链接点击走「查看发布说明」，不重复入口。
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1");
            var parts = text.Split(new[] { "**" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                var run = new Run(parts[i]);
                if (i % 2 == 1) { run.FontWeight = FontWeights.SemiBold; run.Foreground = LightTheme.Brush("#283343"); }
                block.Inlines.Add(run);
            }
        }

        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        readonly TextBlock status = new TextBlock { Foreground = LightTheme.Brush("#65717F"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 8) };
        readonly ProgressBar progress = new ProgressBar { Height = 5, Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 16) };
        readonly Button install;
        string downloaded;
        bool busy, handingOff;

        public UpdateWindow(UpdateRelease release, AppUpdates updates, Func<string, Task<bool>> apply, Action skip = null, Func<Task<bool>> prepare = null)
        {
            Title = "更新 PickSnap"; Width = 560; SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei"); UseLayoutRounding = true;
            AutomationProperties.SetAutomationId(this, "AppUpdate");
            var body = new StackPanel();
            var heading = new Grid();
            heading.Children.Add(new TextBlock { Text = "发现新版本", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = LightTheme.Brush("#283343") });
            var close = LightTheme.Button("", Close, "quiet"); close.Content = LightTheme.CloseIcon();
            close.MinWidth = 0; close.Width = close.Height = 28; close.Padding = new Thickness(0); close.HorizontalAlignment = HorizontalAlignment.Right;
            AutomationProperties.SetName(close, "关闭更新窗口"); heading.Children.Add(close); body.Children.Add(heading);
            body.Children.Add(new TextBlock { Text = "PickSnap " + AppUpdates.CurrentVersion + " → " + release.Version, FontSize = 14, Foreground = LightTheme.Brush("#65717F"), Margin = new Thickness(0, 10, 0, 20) });
            if (AppUpdates.RunsFromTemporaryFolder())
            {
                // 从压缩软件的临时目录直接运行（例如在压缩包里双击 exe）：这种目录重启即清空，
                // 更新装进去等于白装。先给出明确指引并停用安装，不让用户下载完再撞上报错。
                body.Children.Add(new Border {
                    Background = LightTheme.Brush("#FCF3E2"), CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 14),
                    Child = new TextBlock { Text = "看起来你是直接在压缩包里运行 PickSnap。请先把压缩包解压到固定文件夹，运行其中的 Install.cmd 完成安装，再从这里更新。",
                        FontSize = 12.5, LineHeight = 20, TextWrapping = TextWrapping.Wrap, Foreground = LightTheme.Brush("#9A6B1F") }
                });
            }
            var notesContent = String.IsNullOrWhiteSpace(release.Notes)
                ? (UIElement)new TextBlock { Text = "新版本已准备好。", FontSize = 13, LineHeight = 22, TextWrapping = TextWrapping.Wrap, Foreground = LightTheme.Brush("#343D49") }
                : NotesView(release.Notes);
            body.Children.Add(new ScrollViewer { MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = notesContent });
            status.Text = "更新完成后会自动重新打开，图片、文字和设置保留。"; body.Children.Add(status); body.Children.Add(progress);
            var actions = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (skip != null)
            {
                // 跳过此版本：只影响自动检查的提醒，手动检查仍然可以看到这个版本。
                var details = LightTheme.Button("查看发布说明", delegate { Process.Start(new ProcessStartInfo(release.Url) { UseShellExecute = true }); }, "quiet");
                var skipButton = LightTheme.Button("跳过此版本", delegate { try { skip(); Close(); } catch (Exception error) { status.Text = "跳过设置保存失败：" + error.Message; } }, "quiet");
                skipButton.Margin = new Thickness(14, 0, 0, 0);
                var lefts = new StackPanel { Orientation = Orientation.Horizontal };
                lefts.Children.Add(details); lefts.Children.Add(skipButton);
                actions.Children.Add(lefts);
            }
            else
            {
                var details = LightTheme.Button("查看发布说明", delegate { Process.Start(new ProcessStartInfo(release.Url) { UseShellExecute = true }); }, "quiet");
                details.HorizontalAlignment = HorizontalAlignment.Left; actions.Children.Add(details);
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            Grid.SetRow(buttons, 1);
            var later = LightTheme.Button("稍后再说", Close, "quiet"); later.Margin = new Thickness(0, 0, 8, 0); buttons.Children.Add(later);
            install = LightTheme.Button("更新并重启", delegate { }, "primary"); buttons.Children.Add(install);
            AutomationProperties.SetName(install, "下载更新并自动重启安装");
            if (AppUpdates.RunsFromTemporaryFolder()) install.IsEnabled = false;
            install.Click += async delegate
            {
                if (busy) return;
                busy = true; install.IsEnabled = false;
                try
                {
                    if (downloaded == null)
                    {
                        if (prepare != null && !await prepare()) { status.Text = "请先完成图片编辑，再继续更新。"; return; }
                        progress.Visibility = Visibility.Visible; status.Text = "正在下载更新…"; later.Content = "取消下载";
                        downloaded = await updates.DownloadAsync(release, new Progress<int>(delegate(int percent) { if (cancellation.IsCancellationRequested || !IsVisible || downloaded != null || !busy || handingOff) return; progress.Value = percent; status.Text = percent < 100 ? "正在下载更新 " + percent + "%" : "正在校验安装包…"; }), cancellation.Token);
                    }
                    cancellation.Token.ThrowIfCancellationRequested();
                    handingOff = true; later.IsEnabled = close.IsEnabled = false;
                    status.Text = "正在准备重启…";
                    if (!await apply(downloaded))
                    {
                        handingOff = false; status.Text = "更新包已下载，完成图片编辑后可继续安装。";
                        install.Content = "继续更新";
                    }
                }
                catch (OperationCanceledException) { if (IsVisible) status.Text = "下载已取消或超时，请重试。"; }
                catch (Exception error) { handingOff = false; status.Text = "更新未完成：" + error.Message; install.Content = downloaded == null ? "重试下载" : "重试安装"; }
                finally { busy = false; install.IsEnabled = !AppUpdates.RunsFromTemporaryFolder(); later.IsEnabled = close.IsEnabled = true; later.Content = "稍后再说"; if (!IsVisible && !handingOff && downloaded != null) AppUpdates.Discard(downloaded); }
            };
            actions.Children.Add(buttons); body.Children.Add(actions);
            var card = LightTheme.Card(body); card.Padding = new Thickness(24); LightTheme.FitDialog(this, card);
            Closed += delegate { cancellation.Cancel(); if (!handingOff && downloaded != null) AppUpdates.Discard(downloaded); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (handingOff) e.Cancel = true; };
        }
    }
}
