using System;
using System.Drawing;

namespace ScreenLine
{
    internal static class RevealPolicy
    {
        public static bool NearTop(Rectangle screen, Rectangle work, int x, int y, double scale)
        {
            return x >= screen.Left && x < screen.Right && y >= screen.Top && y <= Math.Max(screen.Top + Math.Max(2, (int)Math.Ceiling(5 * scale)), work.Top - 1);
        }

        internal static bool CoversScreen(Rectangle screen, Rectangle window, bool maximized, bool hasCaption)
        {
            return !(maximized && hasCaption) && window.Left <= screen.Left && window.Top <= screen.Top && window.Right >= screen.Right && window.Bottom >= screen.Bottom;
        }

        public static bool Contains(Rectangle screen, int x, string zone)
        {
            if (x < screen.Left || x >= screen.Right) return false;
            double fraction = (x - screen.Left) / (double)Math.Max(1, screen.Width);
            if (zone == "full") return true;
            if (zone == "left") return fraction >= 0.08 && fraction <= 0.40;
            return fraction >= 0.30 && fraction <= 0.70;
        }
    }

    internal sealed class RevealDwell
    {
        DateTime since = DateTime.MaxValue;
        string area;
        internal void Reset() { since = DateTime.MaxValue; area = null; }
        internal bool Ready(bool allowed, string currentArea, DateTime now, int delayMs)
        {
            if (!allowed) { Reset(); return false; }
            if (since == DateTime.MaxValue || area != currentArea) { since = now; area = currentArea; }
            return now - since >= TimeSpan.FromMilliseconds(delayMs);
        }
    }
}
