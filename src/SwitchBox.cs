using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ScreenLine
{
    // 初始化和设置同步直接定位；只有用户操作才播放滑动动画。
    internal sealed class SwitchBox : CheckBox
    {
        TranslateTransform slide;
        double Position { get { return IsChecked == true ? 16 : 0; } }
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            var thumb = GetTemplateChild("Thumb") as FrameworkElement;
            slide = thumb == null ? null : thumb.RenderTransform as TranslateTransform;
            Snap();
        }
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == IsCheckedProperty) Snap();
        }
        void Snap()
        {
            if (slide == null) return;
            slide.BeginAnimation(TranslateTransform.XProperty, null); slide.X = Position;
        }
        protected override void OnToggle()
        {
            double from = slide == null ? Position : slide.X;
            base.OnToggle();
            if (slide == null || !IsLoaded || !IsVisible || from == Position) return;
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, Position, TimeSpan.FromMilliseconds(160))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }
    }
}
