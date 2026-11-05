using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenLine
{
    internal sealed class ShortcutWindow : Window
    {
        internal readonly TextBox Input = new TextBox { IsReadOnly = true, Padding = new Thickness(12, 10, 12, 10), FontSize = 16 };
        internal readonly Button SaveButton;
        readonly TextBlock error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = LightTheme.Brush("#B74E4A"), Margin = new Thickness(0, 10, 0, 14) };
        internal uint Modifiers, VirtualKey;

        internal static string CommonShortcutWarning(uint modifiers, uint key)
        {
            if (modifiers == 2 && new uint[] { 0x41, 0x43, 0x46, 0x4E, 0x4F, 0x50, 0x53, 0x54, 0x56, 0x57, 0x58, 0x59, 0x5A }.Contains(key))
                return "这是常用的软件快捷键，保存后可能影响复制、粘贴等操作。建议加上 Alt 或 Shift。";
            if ((modifiers & 8) != 0 || modifiers == 1 && (key == 0x73 || key == 9) || modifiers == 6 && key == 0x1B)
                return "这组按键可能被 Windows 或其他软件使用，建议换一组；无法注册时会保留原快捷键。";
            return "";
        }

        internal static string Label(uint modifiers, uint key)
        {
            return ((modifiers & 2) != 0 ? "Ctrl+" : "") + ((modifiers & 1) != 0 ? "Alt+" : "")
                + ((modifiers & 4) != 0 ? "Shift+" : "") + ((modifiers & 8) != 0 ? "Win+" : "") + KeyInterop.KeyFromVirtualKey((int)key);
        }

        internal ShortcutWindow(uint modifiers, uint key, Func<uint, uint, bool> save, string action = "截图", Func<string> saveError = null)
        {
            Title = action + "快捷键 · PickSnap"; Width = 440; Height = 310; ResizeMode = ResizeMode.NoResize;
            FontFamily = LightTheme.UiFont; Background = LightTheme.Brush("#FFFFFF"); Foreground = LightTheme.Brush("#283343"); LightTheme.Polish(this);
            WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false; Topmost = true;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei");
            var body = new StackPanel { Margin = new Thickness(24) };
            body.Children.Add(new TextBlock { Text = action + "快捷键", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = LightTheme.Brush("#283343"), Margin = new Thickness(0, 0, 0, 12) });
            body.Children.Add(new TextBlock { Text = "点击下方，按下新的组合键", Margin = new Thickness(0, 0, 0, 10), Foreground = LightTheme.Brush("#65717F") });
            Modifiers = modifiers; VirtualKey = key; Input.Text = Label(modifiers, key); body.Children.Add(Input); body.Children.Add(error);
            Input.Style = LightTheme.FieldStyle;
            SaveButton = LightTheme.Button("保存", delegate
            {
                if (save(Modifiers, VirtualKey)) Close(); else error.Text = saveError == null ? "组合键已被占用，请换一组。" : saveError();
            }, "primary");
            SaveButton.HorizontalAlignment = HorizontalAlignment.Right; body.Children.Add(SaveButton); Content = body;
            Input.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Tab) return;
                if (e.Key == Key.Escape) { e.Handled = true; Close(); return; }
                e.Handled = true;
                Key pressed = e.Key == Key.System ? e.SystemKey : e.Key;
                if (pressed == Key.LeftCtrl || pressed == Key.RightCtrl || pressed == Key.LeftAlt || pressed == Key.RightAlt || pressed == Key.LeftShift || pressed == Key.RightShift || pressed == Key.LWin || pressed == Key.RWin) return;
                uint mods = (uint)Keyboard.Modifiers;
                // WPF 的 Alt/Control/Shift/Windows 位与 RegisterHotKey 一致。
                if (mods == 0 || pressed == Key.Escape || pressed == Key.Tab) { error.Text = "请使用 Ctrl、Alt、Shift 或 Win 搭配其他按键。"; return; }
                Modifiers = mods; VirtualKey = (uint)KeyInterop.VirtualKeyFromKey(pressed); Input.Text = Label(mods, VirtualKey); error.Text = CommonShortcutWarning(mods, VirtualKey);
            };
            Loaded += delegate { Activate(); WindowsNative.ActivateEditor(new System.Windows.Interop.WindowInteropHelper(this).Handle); Input.Focus(); };
        }
    }
}
