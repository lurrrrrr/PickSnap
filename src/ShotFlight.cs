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
        readonly ScaleTransform flightScale = new ScaleTransform(1, 1);
        readonly TranslateTransform flightPosition = new TranslateTransform();
        readonly DropShadowEffect shadow = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 3, Opacity = 0.18 };
        readonly Stopwatch clock = new Stopwatch();
        readonly Action landed;
        readonly Func<Rect> basketTarget;
        // 每帧新建画刷、裁剪几何体都会让 WPF 重建视觉树并触发全屏分层窗口重绘，是掉帧的主要来源。
        // 改成复用实例、每帧只改数值，视觉完全一致。
        readonly SolidColorBrush backgroundBrush = new SolidColorBrush(Colors.Transparent);
        readonly RectangleGeometry photoClip = new RectangleGeometry();
        readonly RectangleGeometry collectionClip = new RectangleGeometry();
        readonly bool fresh;
        double duration, baseWidth, baseHeight;
        const double fade = 0.08;
        Rect origin;
        // 落点在起飞那一刻算一次并固定：绳子若还在滚动/摆动，card.PhotoRect() 的屏幕坐标会变，
        // 每帧重算会让飞行图追着绳子跑（挂回绳子时看着像绳子抖了一下）。
        Rect destination;
        bool completed, arrived;
        public bool Falling { get; private set; }
        public bool Collecting { get { return basketTarget != null; } }

        public ShotFlight(Forms.Screen screen, ShotCard card, Rect origin, bool falling, Action landed, BitmapSource image = null, Func<Rect> basketTarget = null, bool fresh = false)
        {
            this.card = card;
            ApplyTheme(card.Appearance);
            screenOrigin = origin;
            this.landed = landed;
            this.basketTarget = basketTarget;
            Falling = falling;
            this.fresh = fresh;
            double scale = WindowsNative.MonitorScale(screen);
            Width = screen.Bounds.Width / scale;
            Height = screen.Bounds.Height / scale;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowActivated = false;
            ShowInTaskbar = false;
            Topmost = true;
            IsHitTestVisible = false;
            Content = stage;
            stage.Children.Add(frame);
            frame.RenderTransform = new TransformGroup { Children = { flightScale, turn, flightPosition } };
            photo.Source = image ?? card.Item.Image.Thumbnail;
            photo.Stretch = Stretch.Fill;
            border.Child = photo;
            border.BorderBrush = new SolidColorBrush(LightTheme.CardEdge);
            border.Background = backgroundBrush;
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(12);
            photo.Margin = new Thickness(6);
            // 收集动画原本不裁剪照片，保持 photo.Clip 为空；其余路径复用同一份几何体。
            if (!Collecting) photo.Clip = photoClip;
            border.Effect = shadow;
            // 图片布局固定；保留 BitmapSource 原分辨率，避免大贴图挂回时先被小尺寸缓存糊掉。
            frame.Children.Add(border);
            frame.Children.Add(clip);
            Canvas.SetTop(clip, -20);
            SourceInitialized += delegate
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                WindowsNative.PrepareOverlay(handle);
                WindowsNative.PassMouse(handle, true);
                WindowsNative.SetWindowPos(handle, new IntPtr(-1), screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height, 0x10);
                this.origin = Local(screenOrigin);
                if (!Falling && !Collecting) destination = Local(card.PhotoRect(true));
                Rect target = Falling || Collecting ? this.origin : destination;
                baseWidth = Math.Max(1, target.Width); baseHeight = Math.Max(1, target.Height);
                frame.Width = border.Width = baseWidth;
                frame.Height = border.Height = baseHeight;
                photoClip.Rect = new Rect(0, 0, Math.Max(1, baseWidth - 14), Math.Max(1, baseHeight - 14));
                photoClip.RadiusX = photoClip.RadiusY = 7;
                Canvas.SetLeft(clip, baseWidth / 2 - 8);
                duration = Falling ? 0.30 : Collecting
                    ? MotionTiming.TravelSeconds(this.origin, Local(basketTarget()), false)
                    : MotionTiming.TravelSeconds(this.origin, destination, fresh);
                if (Collecting) backgroundBrush.Color = LightTheme.CardColor;
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
            if (Collecting) { RenderCollection(Math.Min(1, seconds / duration)); return; }
            double progress = Math.Min(1, seconds / duration);
            double ease = MotionTiming.Progress(progress);
            Rect target = Falling ? origin : destination;
            double width = origin.Width + (target.Width - origin.Width) * ease;
            double height = origin.Height + (target.Height - origin.Height) * ease;
            double x = origin.X + origin.Width / 2 + (target.X + target.Width / 2 - origin.X - origin.Width / 2) * ease;
            double y = origin.Y + (target.Y - origin.Y) * ease - (fresh ? 8 : 12) * MotionTiming.Arc(progress);
            double chrome = MotionTiming.Progress(progress / 0.65);
            turn.Angle = card.Tilt * ease;
            if (Falling)
            {
                width = origin.Width; height = origin.Height;
                x = origin.X + width / 2;
                y = origin.Y + 180 * progress * progress;
                turn.Angle = card.Tilt + (card.Tilt < 0 ? -12 : 12) * progress * progress;
                chrome = 1;
                Opacity = 1 - progress * progress;
            }
            PlaceFrame(width, height, x - width / 2, y);
            turn.CenterX = width / 2;
            turn.CenterY = -12;
            Color color = LightTheme.CardColor;
            backgroundBrush.Color = Color.FromArgb((byte)(color.A * chrome), color.R, color.G, color.B);
            clip.Opacity = chrome;
            if (progress < 1) return;
            Complete();
            if (Falling) { Close(); return; }
            Opacity = Math.Max(0, 1 - (seconds - duration) / fade);
            if (seconds >= duration + fade) Close();
        }

        void PlaceFrame(double width, double height, double left, double top)
        {
            flightScale.ScaleX = width / baseWidth;
            flightScale.ScaleY = height / baseHeight;
            flightPosition.X = left; flightPosition.Y = top;
        }

        void RenderCollection(double progress)
        {
            Rect mouth = Local(basketTarget());
            double ease = MotionTiming.Progress(progress);
            double smallWidth = Math.Min(22, Math.Max(10, origin.Width * 0.15));
            double smallHeight = smallWidth * origin.Height / origin.Width;
            double width = origin.Width + (smallWidth - origin.Width) * ease;
            double height = origin.Height + (smallHeight - origin.Height) * ease;
            double startX = origin.X + origin.Width / 2, endX = mouth.X + mouth.Width / 2;
            double x = startX + (endX - startX) * ease;
            double lift = Math.Min(18, Math.Max(6, origin.Y - 10));
            double y = origin.Y + origin.Height / 2 + (mouth.Y + smallHeight / 2 + 4 - origin.Y - origin.Height / 2) * ease
                - lift * MotionTiming.Arc(progress);
            double top = y - height / 2;
            PlaceFrame(width, height, x - width / 2, top);
            turn.CenterX = width / 2; turn.CenterY = height / 2;
            turn.Angle = card.Tilt * (1 - ease) + Math.Sign(endX - startX) * 4 * MotionTiming.Arc(progress);
            clip.Opacity = 0;
            Opacity = 1 - 0.8 * Math.Max(0, (progress - 0.82) / 0.18);
            frame.Clip = null;
            // 图片跨过篮口后逐渐被篮身遮住，而不是在篮子前方淡出。
            if (progress > 0.68 && Math.Abs(x - endX) < mouth.Width * 0.6)
            {
                collectionClip.Rect = new Rect(0, 0, baseWidth, Math.Max(0, Math.Min(baseHeight, (mouth.Y - top) / flightScale.ScaleY)));
                frame.Clip = collectionClip;
            }
            if (progress < 1) return;
            arrived = true; Complete(); Close();
        }
    }
}
