using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ScreenLine
{
    internal static class LightTheme
    {
        // 浅色值 → 深色值。所有界面颜色都经过 Brush() 生成，改配色只需要改这张表。
        static readonly Dictionary<string, string> darkMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "#FFFFFF", "#23262B" },
            { "#F0F3F7", "#2B2F36" }, { "#DFE6EE", "#3B434E" }, { "#D6E4F7", "#2C4258" },
            { "#F1F4F8", "#2B2F36" },
            { "#F0F2F5", "#33383F" },
            { "#E8EDF3", "#3A4048" },
            { "#E0E4EA", "#3A4048" },
            { "#DCE2EA", "#3A4048" },
            { "#E8EBEF", "#333940" }, { "#ECEFF3", "#333940" },
            { "#DCE5F1", "#2F3843" },
            { "#283343", "#E7EAF0" },
            { "#343D49", "#DDE2EA" },
            { "#536172", "#A6B0BC" },
            { "#65717F", "#8F99A6" },
            { "#8390A1", "#7C8794" },
            { "#2463AE", "#5B9BE0" },
            { "#1B5498", "#4A87C9" },
            { "#6E97CB", "#6FA8E3" },
            { "#B74E4A", "#E0807B" },
            { "#F4F7FB", "#33383F" },
            { "#EDF4FF", "#2C3A4C" },
            { "#E7EEF8", "#333A44" },
            { "#FFF0ED", "#46292A" },
            { "#EDF0F4", "#333940" },
            { "#647386", "#8A94A2" },
            { "#8BA4C4", "#5A6675" },
            { "#F5F7FA", "#2B2F36" },
            { "#5B6675", "#A6B0BC" },
            // 篮子底部「全选」两态按钮（白底描边 ↔ 实心蓝白字，悬停/按下整体转蓝）与摘要胶囊的深色映射（danger 系一并补齐）。
            { "#D5DDE6", "#4A525C" }, { "#E4EEFB", "#35414F" }, { "#7FA6D6", "#4E79AC" }, { "#1E549B", "#A8CDF5" },
            { "#CFE1F5", "#3A4A5C" }, { "#5E8EC4", "#4469A0" }, { "#17497F", "#4A87C9" }, { "#123D6B", "#3A6EA8" },
            { "#E8F1FB", "#2C3A4C" }, { "#5B7A9E", "#8FB4DE" },
            { "#FCF3E2", "#43382A" }, { "#8A6D35", "#C8A668" }, { "#9A6B1F", "#D9B36A" },
            { "#E4F2E8", "#2C3E31" }, { "#2E7D46", "#7CC48F" }, { "#5B6B7C", "#9AA6B3" },
            { "#FBE3E0", "#46292A" }, { "#A93E3A", "#E0807B" }, { "#E29C96", "#7A5450" },
            { "#F3B6AD", "#55302E" }, { "#CE5C53", "#8A5651" }, { "#8F2622", "#E8938E" },
            { "#EC9E93", "#6A3B37" }, { "#B84A41", "#9C605A" }, { "#771C18", "#D67F79" },
            { "#0F3054", "#24406A" }, { "#3070C2", "#4A87C9" }, { "#F8D3CE", "#462C2A" }, { "#F0AC9F", "#6A3B37" },
            // 「删除所选」可用态改为实心红（选中=深、禁用=浅与 primary 统一），悬停/按下/深色映射一并补齐。
            { "#B5433E", "#D97068" }, { "#C4574F", "#E0807B" }, { "#8C2E28", "#B04A42" }, { "#F2D3CF", "#EFC9C4" },
            // primary/danger 禁用态：浅色填充+灰调文字（不再是实色淡化），深色映射。
            { "#E3EDF8", "#2E3A47" }, { "#C9DBEF", "#3D4C5C" }, { "#88A6CB", "#7E9BC0" },
            { "#F9E2DF", "#462C2A" }, { "#ECC5C0", "#5C3B37" }, { "#D49A93", "#C98D85" },
            { "#5E6E85", "#A9B8CB" },
        };

        static bool dark;

        // 深色模式下卡片、面板的底色与描边（半透明，叠在透明窗口上更自然）。
        public static bool Dark { get { return dark; } }

        public static void UseDark(bool value) { dark = value; }

        // 把设置里的 auto/light/dark 解析成实际配色。
        public static bool Resolve(string mode)
        {
            if (string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase)) return false;
            return SystemPrefersDark();
        }

        // 跟随 Windows 的「应用主题」设置：AppsUseLightTheme = 0 表示深色。
        public static bool SystemPrefersDark()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value is int && (int)value == 0;
                }
            }
            catch { return false; }
        }

        // 供各处内联 XAML 模板调用：浅色原样返回，深色时把十六进制色值换成深色目录里的对应值。
        public static string Sheet(string xaml)
        {
            return dark ? Recolor(xaml) : xaml;
        }

        static string Recolor(string text)
        {
            return Regex.Replace(text, "#[0-9A-Fa-f]{6}", delegate(Match match)
            {
                string mapped;
                return darkMap.TryGetValue(match.Value, out mapped) ? mapped : match.Value;
            });
        }

        const string sheet = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='Action' TargetType='Button'>
    <Setter Property='FontFamily' Value='Microsoft YaHei UI'/><Setter Property='FontSize' Value='13'/>
    <Setter Property='Foreground' Value='#343D49'/><Setter Property='Background' Value='#FFFFFF'/>
    <Setter Property='BorderBrush' Value='#DCE2EA'/><Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='14,0'/><Setter Property='Height' Value='34'/><Setter Property='MinWidth' Value='80'/>
    <Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Border x:Name='Surface' CornerRadius='7' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>
        <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#F0F2F5'/></Trigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Background' Value='#E8EDF3'/></Trigger>
        <Trigger Property='Tag' Value='primary'><Setter Property='Background' Value='#2463AE'/><Setter Property='BorderBrush' Value='#2463AE'/><Setter Property='Foreground' Value='White'/><Setter Property='FontWeight' Value='SemiBold'/></Trigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='primary'/><Condition Property='IsEnabled' Value='True'/><Condition Property='IsMouseOver' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Background' Value='#3070C2'/><Setter TargetName='Surface' Property='BorderBrush' Value='#3070C2'/></MultiTrigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='primary'/><Condition Property='IsEnabled' Value='True'/><Condition Property='IsPressed' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Background' Value='#173F6F'/><Setter TargetName='Surface' Property='BorderBrush' Value='#173F6F'/><Setter Property='Foreground' Value='#C9DCF5'/></MultiTrigger>
        <Trigger Property='Tag' Value='danger'><Setter Property='Background' Value='#B5433E'/><Setter Property='BorderBrush' Value='#B5433E'/><Setter Property='Foreground' Value='White'/><Setter Property='FontWeight' Value='SemiBold'/></Trigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='danger'/><Condition Property='IsEnabled' Value='True'/><Condition Property='IsMouseOver' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Background' Value='#C4574F'/><Setter TargetName='Surface' Property='BorderBrush' Value='#C4574F'/></MultiTrigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='danger'/><Condition Property='IsEnabled' Value='True'/><Condition Property='IsPressed' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Background' Value='#8C2E28'/><Setter TargetName='Surface' Property='BorderBrush' Value='#8C2E28'/><Setter Property='Foreground' Value='#F2D3CF'/></MultiTrigger>
        <Trigger Property='Tag' Value='select'><Setter Property='Background' Value='#FFFFFF'/><Setter Property='BorderBrush' Value='#D5DDE6'/><Setter Property='Foreground' Value='#343D49'/></Trigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='select'/><Condition Property='IsMouseOver' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Background' Value='#E4EEFB'/><Setter TargetName='Surface' Property='BorderBrush' Value='#7FA6D6'/><Setter Property='Foreground' Value='#1E549B'/></MultiTrigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='select'/><Condition Property='IsPressed' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Background' Value='#CFE1F5'/><Setter TargetName='Surface' Property='BorderBrush' Value='#5E8EC4'/><Setter Property='Foreground' Value='#17497F'/></MultiTrigger>
        <Trigger Property='Tag' Value='select-on'><Setter Property='Background' Value='#2463AE'/><Setter Property='BorderBrush' Value='#2463AE'/><Setter Property='Foreground' Value='White'/><Setter Property='FontWeight' Value='SemiBold'/></Trigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='select-on'/><Condition Property='IsEnabled' Value='True'/><Condition Property='IsMouseOver' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Background' Value='#3070C2'/><Setter TargetName='Surface' Property='BorderBrush' Value='#3070C2'/></MultiTrigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='select-on'/><Condition Property='IsEnabled' Value='True'/><Condition Property='IsPressed' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Background' Value='#173F6F'/><Setter TargetName='Surface' Property='BorderBrush' Value='#173F6F'/></MultiTrigger>
        <Trigger Property='Tag' Value='quiet'><Setter Property='BorderBrush' Value='Transparent'/><Setter Property='Background' Value='Transparent'/></Trigger>
        <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#6E97CB'/><Setter TargetName='Surface' Property='BorderThickness' Value='2'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Surface' Property='Opacity' Value='0.45'/><Setter Property='Cursor' Value='Arrow'/></Trigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='primary'/><Condition Property='IsEnabled' Value='False'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Opacity' Value='1'/><Setter TargetName='Surface' Property='Background' Value='#E3EDF8'/><Setter TargetName='Surface' Property='BorderBrush' Value='#C9DBEF'/><Setter Property='Foreground' Value='#88A6CB'/></MultiTrigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='danger'/><Condition Property='IsEnabled' Value='False'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Opacity' Value='1'/><Setter TargetName='Surface' Property='Background' Value='#F9E2DF'/><Setter TargetName='Surface' Property='BorderBrush' Value='#ECC5C0'/><Setter Property='Foreground' Value='#D49A93'/></MultiTrigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Hint' TargetType='ToolTip'>
    <Setter Property='FontFamily' Value='Microsoft YaHei UI'/><Setter Property='FontSize' Value='12'/><Setter Property='Foreground' Value='#536172'/>
    <Setter Property='MaxWidth' Value='320'/><Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToolTip'>
      <Border Background='#FFFFFF' BorderBrush='#E0E4EA' BorderThickness='1' CornerRadius='7' Padding='10,7' Margin='8'>
        <Border.Effect><DropShadowEffect BlurRadius='12' ShadowDepth='2' Opacity='0.12'/></Border.Effect>
        <TextBlock Text='{TemplateBinding Content}' TextWrapping='Wrap'/>
      </Border>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>";

        static readonly ResourceDictionary lightStyles = (ResourceDictionary)XamlReader.Parse(sheet);
        static readonly ResourceDictionary darkStyles = (ResourceDictionary)XamlReader.Parse(Recolor(sheet));
        static ResourceDictionary styles { get { return dark ? darkStyles : lightStyles; } }

        public static Style ButtonStyle { get { return (Style)styles["Action"]; } }
        public static Style HintStyle { get { return (Style)styles["Hint"]; } }
        // 中西文统一用同一族字体，避免 Sego UI 缺中文字形时回退成另一套字重、大小不一的字。
        public static readonly FontFamily UiFont = new FontFamily("Microsoft YaHei UI");
        // 透明窗口拿不到 ClearType，但把排版模式切成 Display 能让小字号按整像素落笔，中文明显更实。
        public static void Polish(Window window)
        {
            TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(window, TextRenderingMode.ClearType);
            window.UseLayoutRounding = true;
            window.SnapsToDevicePixels = true;
        }
        public static SolidColorBrush Brush(string value)
        {
            string mapped;
            if (dark && darkMap.TryGetValue(value, out mapped)) value = mapped;
            return (SolidColorBrush)new BrushConverter().ConvertFromString(value);
        }

        // 卡片与面板的底色：浅色是接近纯白，深色是略高于背景的深灰。
        public static Color CardColor { get { return dark ? Color.FromArgb(246, 38, 41, 47) : Color.FromArgb(245, 255, 255, 255); } }
        public static Color CardEdge { get { return dark ? Color.FromRgb(58, 64, 72) : Color.FromRgb(220, 225, 233); } }
        public static Color ShadowColor { get { return dark ? Color.FromRgb(0, 0, 0) : Color.FromRgb(38, 57, 76); } }
        public static void Install(Application app)
        {
            // 小型透明挂件使用软件绘制，避免反复开关面板积累大块图形缓存。
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            app.Resources[typeof(ToolTip)] = styles["Hint"];
        }

        public static UIElement CloseIcon()
        {
            return new System.Windows.Shapes.Path {
                Width = 8, Height = 8, Data = Geometry.Parse("M0,0 L8,8 M8,0 L0,8"),
                Stroke = Brush("#343D49"), StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            };
        }

        public static Button Button(string title, Action action, string tag = null)
        {
            var button = new Button { Content = title, Style = ButtonStyle, Tag = tag };
            button.Click += delegate { action(); };
            return button;
        }

        public static Border Card(UIElement content)
        {
            return new Border {
                Child = content, Margin = new Thickness(16), Padding = new Thickness(24), CornerRadius = new CornerRadius(14),
                Background = Brush("#FFFFFF"), BorderBrush = Brush("#E0E4EA"), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { Color = ShadowColor, BlurRadius = 22, ShadowDepth = 6, Opacity = dark ? 0.34 : 0.16 }
            };
        }
    }
}
