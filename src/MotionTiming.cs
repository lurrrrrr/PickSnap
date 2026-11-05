using System;
using System.Windows;

namespace ScreenLine
{
    // 同一种动作共用节奏；时长随距离小幅增长，避免近处慢悠悠、远处突然窜过去。
    internal static class MotionTiming
    {
        public const double CardFade = 0.10;
        public const double CardSwing = 12;
        public const double BasketSwing = 8;
        public const double PinDrop = 0.32;
        public const double PinSettle = 0.10;

        public static double Progress(double value)
        {
            double t = Math.Max(0, Math.Min(1, value));
            return t * t * (3 - 2 * t);
        }

        // 起止处的高度与速度均为零，不让拱形轨迹在接到夹子时突然改变方向。
        public static double Arc(double value)
        {
            double t = Math.Max(0, Math.Min(1, value));
            double bend = t * (1 - t);
            return 16 * bend * bend;
        }

        public static double TravelSeconds(Rect from, Rect to, bool fresh)
        {
            double dx = to.X + to.Width / 2 - from.X - from.Width / 2;
            double dy = to.Y + to.Height / 2 - from.Y - from.Height / 2;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            return (fresh ? 0.30 : 0.28) + Math.Min(fresh ? 0.10 : 0.14, distance / 3000);
        }
    }
}
