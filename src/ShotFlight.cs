using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace ScreenLine
{
    internal sealed class ShotFlight : Window
    {
        readonly ShotCard card;
        readonly Rect screenOrigin;
        readonly Canvas stage = new Canvas(), frame = new Canvas();
        readonly Border border = new Border();
        readonly Image photo = new Image();
        readonly ClothesClip clip = new ClothesClip();
        readonly RotateTransform turn = new RotateTransform();
        readonly DropShadowEffect shadow = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 4 };
        readonly Stopwatch clock = new Stopwatch();
        readonly Action landed;
        readonly Func<Rect> basketTarget;
        Rect origin;
        // 落点在起飞那一刻算一次并固定：绳子若还在滚动/摆动，card.PhotoRect() 的屏幕坐标会变，
        // 每帧重算会让飞行图追着绳子跑（挂回绳子时看着像绳子抖了一下）。
        Rect destination;
        // 时长与抛物线拱高按实际飞行距离缩放：贴图就在绳子下方时不拖沓也不绕远路，
        // 贴在屏幕另一端时才给足时间和一点弧度。
        double duration = 0.6;
        double arc = 30;
        bool completed, arrived;
        public bool Falling { get; private set; }
        public bool Collecting { get { return basketTarget != null; } }

        public ShotFlight(Forms.Screen screen, ShotCard card, Rect origin, bool falling, Action landed, BitmapSource image = null, Func<Rect> basketTarget = null)
        {
            this.card = card;
            ApplyTheme(card.Appearance);
            screenOrigin = origin;
            this.landed = landed;
            this.basketTarget = basketTarget;
            Falling = falling;
            double scale = WindowsNative.MonitorScale(screen);
            Width = screen.Bounds.Width / scale;
            Height = screen.Bounds.Height / scale;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            LightTheme.SoftwareRender(this);
            ShowActivated = false;
            ShowInTaskbar = false;
            Topmost = true;
            IsHitTestVisible = false;
            Content = stage;
            stage.Children.Add(frame);
            frame.RenderTransform = turn;
            photo.Source = image ?? card.Item.Image.Thumbnail;
            photo.Stretch = Stretch.Fill;
            border.Child = photo;
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 225, 233));
            border.Effect = shadow;
            frame.Children.Add(border);
            frame.Children.Add(clip);
            SourceInitialized += delegate
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                WindowsNative.PrepareOverlay(handle);
                WindowsNative.PassMouse(handle, true);
                WindowsNative.SetWindowPos(handle, new IntPtr(-1), screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height, 0x10);
                this.origin = Local(screenOrigin);
                if (Falling) duration = 0.55;
                if (!Falling && !Collecting)
                {
                    destination = Local(card.PhotoRect());
                    double dx = destination.X + destination.Width / 2 - (origin.X + origin.Width / 2);
                    double dy = destination.Y + destination.Height / 2 - (origin.Y + origin.Height / 2);
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    duration = 0.44 + 0.16 * Math.Min(1, distance / 650);
                    arc = Math.Min(26, distance * 0.09);
                }
                clock.Start();
                Render(null, EventArgs.Empty);
                CompositionTarget.Rendering += Render;
            };
            Closed += delegate
            {
                CompositionTarget.Rendering -= Render;
                Complete();
            };
        }

        Rect Local(Rect rectangle)
        {
            return new Rect(PointFromScreen(rectangle.TopLeft), PointFromScreen(rectangle.BottomRight));
        }

        public void ApplyTheme(LineTheme theme) { clip.Apply(theme, card.ColorIndex); }

        void Complete()
        {
            if (completed) return;
            completed = true;
            if (Collecting && !arrived) return;
            landed();
        }

        void Render(object sender, EventArgs args)
        {
            if (!Falling && !Collecting && card.IsDisposed) { Close(); return; }
            double seconds = clock.Elapsed.TotalSeconds;
            if (Collecting) { RenderCollection(Math.Min(1, seconds / 0.64)); return; }
            double progress = Math.Min(1, seconds / duration);
            // 五次曲线让起飞与抵达的速度、加速度都平滑归零。
            double ease = progress * progress * progress * (10 + progress * (-15 + 6 * progress));
            Rect target = Falling ? origin : destination;
            double width, height, x, y, angle, clipOpacity;
            if (Falling)
            {
                width = origin.Width; height = origin.Height;
                x = origin.X + width / 2;
                y = origin.Y + 520 * progress * progress;
                angle = card.Tilt + (card.Tilt < 0 ? -32 : 32) * progress * progress;
                clipOpacity = 1;
                Opacity = 1 - progress * progress;
            }
            else
            {
                width = origin.Width + (target.Width - origin.Width) * ease;
                height = origin.Height + (target.Height - origin.Height) * ease;
                x = origin.X + origin.Width / 2 + (target.X + target.Width / 2 - origin.X - origin.Width / 2) * ease;
                y = origin.Y + (target.Y - origin.Y) * ease - arc * Math.Sin(Math.PI * ease);
                angle = card.Tilt * ease;
                // 桌面用的是图钉，挂回绳子才换成晾衣夹：只有夹子在途中渐入。
                double t = Math.Max(0, Math.Min(1, (ease - 0.12) / 0.7));
                clipOpacity = t * t * (3 - 2 * t);
            }
            turn.Angle = angle;
            frame.Width = border.Width = width;
            frame.Height = border.Height = height;
            Canvas.SetLeft(frame, x - width / 2);
            Canvas.SetTop(frame, y);
            turn.CenterX = width / 2;
            turn.CenterY = -12;
            // 白边、圆角、阴影、照片内边距全程保持"挂好"的样子：起飞第一帧就能和桌面贴图
            // 像素级接上，不再是先融成一张裸图、飞到一半又长出相框。相框颜色从贴图的白
            // 平滑过渡到绳上卡片的主题色，深色模式下落地也不会突然变色。
            double blend = Falling ? 1 : ease;
            border.Background = new SolidColorBrush(Mix(Colors.White, LightTheme.CardColor, blend));
            border.BorderBrush = new SolidColorBrush(Mix(Color.FromRgb(220, 225, 233), LightTheme.CardEdge, blend));
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(12);
            photo.Margin = new Thickness(6);
            photo.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(1, width - 14), Math.Max(1, height - 14)), 7, 7);
            // 阴影模糊从桌面贴图的 16 过渡到绳上卡片的 14：两端各自对齐，交接时轮廓不跳。
            shadow.BlurRadius = Falling ? 14 : 16 - 2 * blend;
            shadow.Opacity = 0.2;
            clip.Opacity = clipOpacity;
            Canvas.SetLeft(clip, width / 2 - 8);
            Canvas.SetTop(clip, -20);
            if (progress < 1) return;
            // 落地只关窗，不在这里调 Complete()。Close() 之后窗口还要等一次渲染才真正从屏幕
            // 移除，若此刻就让绳上卡片 Opacity=1，会有一帧「飞行窗 + 卡片」两个不透明窗口叠在
            // 一起：阴影叠成双份、晾衣夹画了两遍，紧接着飞行窗消失又恢复原样——就是挂上之后
            // 闪一下的根源。改由 Closed 事件里的 Complete() 在窗口已销毁后接手，全程没有重叠帧。
            // 也不能用「飞行窗淡出 + 卡片淡入」的交叉过渡：上下叠放的两个窗口 alpha 相乘不互补
            // （合成 = C·(1-t+t²) + 背景·t(1-t)，t=0.5 时会透出约 25% 背景），中间必然暗一下。
            Close();
        }

        static Color Mix(Color from, Color to, double t)
        {
            return Color.FromArgb(
                (byte)(from.A + (to.A - from.A) * t),
                (byte)(from.R + (to.R - from.R) * t),
                (byte)(from.G + (to.G - from.G) * t),
                (byte)(from.B + (to.B - from.B) * t));
        }

        void RenderCollection(double progress)
        {
            Rect mouth = Local(basketTarget());
            double ease = progress * progress * progress * (10 + progress * (-15 + 6 * progress));
            double smallWidth = Math.Min(22, Math.Max(10, origin.Width * 0.15));
            double smallHeight = smallWidth * origin.Height / origin.Width;
            double width = origin.Width + (smallWidth - origin.Width) * ease;
            double height = origin.Height + (smallHeight - origin.Height) * ease;
            double startX = origin.X + origin.Width / 2, endX = mouth.X + mouth.Width / 2;
            double x = startX + (endX - startX) * ease;
            double lift = Math.Min(38, Math.Max(10, origin.Y - 10));
            double y = origin.Y + origin.Height / 2 + (mouth.Y + smallHeight / 2 + 4 - origin.Y - origin.Height / 2) * ease
                - lift * Math.Sin(Math.PI * ease);
            double top = y - height / 2;
            frame.Width = border.Width = width; frame.Height = border.Height = height;
            Canvas.SetLeft(frame, x - width / 2); Canvas.SetTop(frame, top);
            turn.CenterX = width / 2; turn.CenterY = height / 2;
            turn.Angle = card.Tilt * (1 - ease) + Math.Sign(endX - startX) * 10 * Math.Sin(Math.PI * ease);
            border.Background = LightTheme.Brush("#FFFFFF"); border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(Math.Min(9, width * 0.09));
            photo.Margin = new Thickness(Math.Min(6, width * 0.04));
            shadow.Opacity = 0.16 * (1 - ease); clip.Opacity = 0;
            Opacity = 1 - 0.8 * Math.Max(0, (progress - 0.82) / 0.18);
            frame.Clip = null;
            // 图片跨过篮口后逐渐被篮身遮住，而不是在篮子前方淡出。
            if (progress > 0.68 && Math.Abs(x - endX) < mouth.Width * 0.6)
                frame.Clip = new RectangleGeometry(new Rect(0, 0, width, Math.Max(0, Math.Min(height, mouth.Y - top))));
            if (progress < 1) return;
            // 同上：等飞行窗真正关掉后，由 Closed 事件里的 Complete() 通知篮子接收，
            // 避免篮口那一帧同时出现飞行图和落袋后的计数刷新。
            arrived = true; Close();
        }
    }
}
