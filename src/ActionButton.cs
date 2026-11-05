using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenLine
{
    // 长文案或运行时改变的按钮文字，不能被调用处遗留的固定宽度截断。
    internal sealed class ActionButton : Button
    {
        protected override void OnContentChanged(object oldContent, object newContent)
        {
            base.OnContentChanged(oldContent, newContent); FitText();
        }
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == FontSizeProperty || e.Property == FontFamilyProperty || e.Property == FontWeightProperty
                || e.Property == FontStyleProperty || e.Property == FontStretchProperty || e.Property == PaddingProperty
                || e.Property == BorderThicknessProperty || e.Property == ForegroundProperty) FitText();
        }
        void FitText()
        {
            var label = Content as string;
            if (!string.IsNullOrEmpty(label) && Foreground != null)
            {
                var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection,
                    new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                double width = Math.Ceiling(text.WidthIncludingTrailingWhitespace + Padding.Left + Padding.Right + BorderThickness.Left + BorderThickness.Right + 4);
                if (MinWidth < width) SetCurrentValue(MinWidthProperty, width);
                if (label.Length > 1)
                {
                    double height = Math.Ceiling(text.Height + Padding.Top + Padding.Bottom + BorderThickness.Top + BorderThickness.Bottom + 2);
                    if (MinHeight < height) SetCurrentValue(MinHeightProperty, height);
                }
            }
        }
    }
}
