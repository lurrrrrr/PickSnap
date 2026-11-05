using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScreenLine
{
    // ContextMenu 的鼠标捕获可能丢失：用屏幕坐标兜底，不依赖 MouseLeave。
    internal sealed class MenuDismissal
    {
        static MenuDismissal active;
        readonly ContextMenu menu;
        readonly Func<bool> overSubmenu;
        readonly Action close;
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        readonly Stopwatch clock = new Stopwatch();
        readonly MenuDismissalPolicy policy = new MenuDismissalPolicy();
        IntPtr foreground;
        WindowsNative.Point keyboardCursor;
        bool keyboard;

        internal MenuDismissal(ContextMenu menu, Func<bool> overSubmenu, Action close)
        {
            this.menu = menu; this.overSubmenu = overSubmenu; this.close = close;
            menu.Opened += delegate {
                if (active != null && active != this) active.close();
                active = this; keyboard = false; foreground = WindowsNative.GetForegroundWindow();
                policy.Reset(); clock.Restart(); timer.Start();
            };
            menu.Closed += delegate { timer.Stop(); clock.Stop(); if (active == this) active = null; };
            menu.PreviewKeyDown += delegate { keyboard = true; keyboardCursor = WindowsNative.Cursor(); };
            timer.Tick += delegate { Poll(); };
        }

        void Poll()
        {
            if (!menu.IsOpen) { timer.Stop(); return; }
            var cursor = WindowsNative.Cursor();
            if (keyboard && (Math.Abs(cursor.X - keyboardCursor.X) > 3 || Math.Abs(cursor.Y - keyboardCursor.Y) > 3)) keyboard = false;
            bool inside = Contains(menu, cursor) || (overSubmenu != null && overSubmenu());
            bool pressed = (WindowsNative.GetAsyncKeyState(1) & 0x8000) != 0 || (WindowsNative.GetAsyncKeyState(2) & 0x8000) != 0;
            IntPtr current = WindowsNative.GetForegroundWindow();
            if (policy.ShouldClose(clock.ElapsedMilliseconds, inside, keyboard, pressed, current != IntPtr.Zero && current != foreground)) close();
        }

        internal static bool Contains(FrameworkElement element, WindowsNative.Point cursor)
        {
            if (element == null || !element.IsVisible || element.ActualWidth <= 0) return false;
            var source = PresentationSource.FromVisual(element);
            if (source == null || source.CompositionTarget == null) return false;
            var transform = source.CompositionTarget.TransformToDevice;
            Point corner = element.PointToScreen(new Point(0, 0));
            return cursor.X >= corner.X && cursor.X <= corner.X + element.ActualWidth * transform.M11 &&
                   cursor.Y >= corner.Y && cursor.Y <= corner.Y + element.ActualHeight * transform.M22;
        }
    }

    internal sealed class MenuDismissalPolicy
    {
        long outsideSince;
        bool entered;
        internal void Reset() { outsideSince = -1; entered = false; }
        internal bool ShouldClose(long elapsed, bool inside, bool keyboard, bool pressed, bool switchedApp)
        {
            if (elapsed >= 250 && (switchedApp || (!inside && pressed))) return true;
            if (inside) { entered = true; outsideSince = -1; return false; }
            if (keyboard) { outsideSince = -1; return false; }
            if (outsideSince < 0) outsideSince = elapsed;
            // 刚从托盘图标打开时给鼠标进入面板留时间；进入后离开 450ms 即收起。
            return elapsed >= (entered ? 250 : 1200) && elapsed - outsideSince >= 450;
        }
    }
}
