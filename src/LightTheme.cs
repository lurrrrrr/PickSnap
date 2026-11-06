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
            // 托盘快捷格与开关、右键菜单红字、"已复制"徽标的深色补映射（此前漏配导致深色下发白）。
            { "#B8C2D0", "#4A525C" }, { "#D6DFEA", "#3F4750" }, { "#C4D1E0", "#4A525C" },
            { "#C8D4E2", "#434B55" }, { "#E5ECF5", "#3A4048" }, { "#BB514E", "#E0807B" },
            { "#20408E", "#A8CDF5" }, { "#DCE1E9", "#3A4048" },
            { "#185FA5", "#5B9BE0" }, { "#26394C", "#000000" }, { "#5B8A67", "#7CC48F" },
            { "#667487", "#8A94A2" }, { "#9CAABD", "#5A6675" }, { "#B8C0CC", "#4A525C" },
            { "#C3CCD8", "#4A525C" }, { "#CDD6E1", "#4A525C" }, { "#E5EAF1", "#3A4048" },
            { "#E9EEF4", "#3A4048" }, { "#F4F6F9", "#333940" },
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
    <Setter Property='FontFamily' Value='Microsoft YaHei'/><Setter Property='FontSize' Value='13'/>
    <Setter Property='Foreground' Value='#343D49'/><Setter Property='Background' Value='#FFFFFF'/>
    <Setter Property='BorderBrush' Value='#DCE2EA'/><Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='16,0'/><Setter Property='Height' Value='36'/><Setter Property='MinWidth' Value='96'/>
    <Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Border x:Name='Surface' CornerRadius='7' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>
        <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' RecognizesAccessKey='True'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#F0F2F5'/></Trigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Background' Value='#E8EDF3'/></Trigger>
        <Trigger Property='Tag' Value='primary'><Setter Property='Background' Value='#2463AE'/><Setter Property='BorderBrush' Value='#2463AE'/><Setter Property='Foreground' Value='White'/><Setter Property='FontWeight' Value='SemiBold'/></Trigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='primary'/><Condition Property='IsEnabled' Value='True'/><Condition Property='IsMouseOver' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Background' Value='#3070C2'/><Setter TargetName='Surface' Property='BorderBrush' Value='#3070C2'/></MultiTrigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='primary'/><Condition Property='IsEnabled' Value='True'/><Condition Property='IsPressed' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Background' Value='#173F6F'/><Setter TargetName='Surface' Property='BorderBrush' Value='#173F6F'/><Setter Property='Foreground' Value='#C9DCF5'/></MultiTrigger>
        <Trigger Property='Tag' Value='danger'><Setter Property='Background' Value='#FFFFFF'/><Setter Property='BorderBrush' Value='#D5DDE6'/><Setter Property='Foreground' Value='#B74E4A'/></Trigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='danger'/><Condition Property='IsEnabled' Value='True'/><Condition Property='IsMouseOver' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Background' Value='#FFF0ED'/><Setter TargetName='Surface' Property='BorderBrush' Value='#B74E4A'/></MultiTrigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='danger'/><Condition Property='IsEnabled' Value='True'/><Condition Property='IsPressed' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Background' Value='#F9E2DF'/><Setter Property='Foreground' Value='#B74E4A'/></MultiTrigger>
        <Trigger Property='Tag' Value='danger-primary'><Setter Property='Background' Value='#B5433E'/><Setter Property='BorderBrush' Value='#B5433E'/><Setter Property='Foreground' Value='White'/><Setter Property='FontWeight' Value='SemiBold'/></Trigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='danger-primary'/><Condition Property='IsEnabled' Value='True'/><Condition Property='IsMouseOver' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Background' Value='#C4574F'/><Setter TargetName='Surface' Property='BorderBrush' Value='#C4574F'/></MultiTrigger>
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
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='danger-primary'/><Condition Property='IsEnabled' Value='False'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Opacity' Value='1'/><Setter TargetName='Surface' Property='Background' Value='#F9E2DF'/><Setter TargetName='Surface' Property='BorderBrush' Value='#ECC5C0'/><Setter Property='Foreground' Value='#B74E4A'/></MultiTrigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='danger'/><Condition Property='IsEnabled' Value='False'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Opacity' Value='1'/><Setter TargetName='Surface' Property='Background' Value='#F1F4F8'/><Setter TargetName='Surface' Property='BorderBrush' Value='#DCE2EA'/><Setter Property='Foreground' Value='#8390A1'/></MultiTrigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='LinkAction' TargetType='Button'>
    <Setter Property='FontFamily' Value='Microsoft YaHei'/><Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='Foreground' Value='#2463AE'/><Setter Property='Background' Value='Transparent'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Height' Value='36'/><Setter Property='MinWidth' Value='0'/><Setter Property='Padding' Value='10,0'/><Setter Property='FontSize' Value='13'/><Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Border x:Name='Surface' Background='{TemplateBinding Background}' CornerRadius='6'><ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
      <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#EDF4FF'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Background' Value='#D6E4F7'/></Trigger><Trigger Property='Tag' Value='danger'><Setter Property='Foreground' Value='#B74E4A'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.5'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Selection' TargetType='CheckBox'>
    <Setter Property='Foreground' Value='#343D49'/><Setter Property='FontSize' Value='13'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Padding' Value='8,0'/><Setter Property='MinHeight' Value='36'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
      <Grid Background='Transparent'><Grid.ColumnDefinitions><ColumnDefinition Width='20'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
        <Border x:Name='Box' Width='17' Height='17' CornerRadius='4' Background='#FFFFFF' BorderBrush='#B8C2D0' BorderThickness='1' VerticalAlignment='Center'/>
        <Path x:Name='Check' Data='M4,8 L7,11 L13,5' Width='17' Height='17' VerticalAlignment='Center' Stroke='White' StrokeThickness='1.7' Visibility='Collapsed'/>
        <ContentPresenter Grid.Column='1' Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/>
      </Grid><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='#2463AE'/><Setter TargetName='Box' Property='BorderBrush' Value='#2463AE'/><Setter TargetName='Check' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='#2463AE'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.45'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='NavigationItem' TargetType='ListBoxItem'>
    <Setter Property='FocusVisualStyle'><Setter.Value><Style TargetType='Control'><Setter Property='Template'><Setter.Value><ControlTemplate><Border BorderBrush='#6E97CB' BorderThickness='1' CornerRadius='5'/></ControlTemplate></Setter.Value></Setter></Style></Setter.Value></Setter>
    <Setter Property='Foreground' Value='#343D49'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='Height' Value='48'/><Setter Property='Margin' Value='0,3'/><Setter Property='Padding' Value='12,0'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'>
      <Border x:Name='Surface' Background='Transparent' CornerRadius='5'>
        <Grid><ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Stretch' VerticalAlignment='Center'/></Grid>
      </Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#E8EDF3'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='Surface' Property='Background' Value='#E7EEF8'/><Setter Property='FontWeight' Value='SemiBold'/><Setter Property='Foreground' Value='#2463AE'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Switch' TargetType='CheckBox'>
    <Setter Property='Width' Value='44'/><Setter Property='Height' Value='28'/><Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle'><Setter.Value><Style TargetType='Control'><Setter Property='Template'><Setter.Value><ControlTemplate><Border BorderBrush='#6E97CB' BorderThickness='1' CornerRadius='5' Margin='-1'/></ControlTemplate></Setter.Value></Setter></Style></Setter.Value></Setter>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
      <Border x:Name='Focus' Background='Transparent' CornerRadius='14' BorderBrush='Transparent' BorderThickness='2'>
        <Border x:Name='Track' Width='36' Height='20' Background='#B8C2D0' CornerRadius='10'>
          <Ellipse x:Name='Thumb' Fill='White' Width='14' Height='14' HorizontalAlignment='Left' Margin='3,0,3,0'><Ellipse.RenderTransform><TranslateTransform x:Name='Slide'/></Ellipse.RenderTransform></Ellipse>
        </Border>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsChecked' Value='True'><Setter TargetName='Track' Property='Background' Value='#2463AE'/>
        </Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Field' TargetType='TextBox'><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='FontFamily' Value='Segoe UI, Microsoft YaHei'/><Setter Property='FontSize' Value='13'/><Setter Property='Foreground' Value='#283343'/><Setter Property='Background' Value='#FFFFFF'/><Setter Property='BorderBrush' Value='#DCE2EA'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='12,7'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'><Border x:Name='Edge' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='7'><ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'/></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Edge' Property='BorderBrush' Value='#6E97CB'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Choice' TargetType='ComboBox'>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Foreground' Value='#283343'/><Setter Property='Background' Value='#FFFFFF'/><Setter Property='BorderBrush' Value='#DCE2EA'/><Setter Property='FontFamily' Value='Segoe UI, Microsoft YaHei'/><Setter Property='FontSize' Value='13'/><Setter Property='Padding' Value='12,0'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/>
    <Setter Property='ItemContainerStyle'><Setter.Value><Style TargetType='ComboBoxItem'><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Padding' Value='12,8'/><Setter Property='Foreground' Value='#283343'/></Style></Setter.Value></Setter>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'>
      <Grid><Border x:Name='Edge' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='7'/>
        <ContentPresenter Margin='12,0,30,0' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' IsHitTestVisible='False'/>
        <ToggleButton IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' Focusable='False' Background='Transparent'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='Transparent'><Path Data='M0,0 L4,4 L8,0' Width='8' Height='4' Stroke='#65717F' StrokeThickness='1.3' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,12,0'/></Border></ControlTemplate></ToggleButton.Template></ToggleButton>
        <Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'><Border Background='#FFFFFF' BorderBrush='#DCE2EA' BorderThickness='1' CornerRadius='7' MinWidth='{TemplateBinding ActualWidth}' Padding='4' Margin='0,4,0,0'><ScrollViewer MaxHeight='260' CanContentScroll='True'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer></Border></Popup>
      </Grid><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Edge' Property='BorderBrush' Value='#6E97CB'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Scroll' TargetType='ScrollBar'>
    <Setter Property='Width' Value='12'/><Setter Property='Background' Value='Transparent'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
      <Grid Background='{TemplateBinding Background}'><Track x:Name='PART_Track' Orientation='{TemplateBinding Orientation}' IsDirectionReversed='True'>
        <Track.DecreaseRepeatButton><RepeatButton x:Name='PageBefore' Command='ScrollBar.PageUpCommand' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
        <Track.Thumb><Thumb x:Name='ScrollThumb' MinHeight='24'><Thumb.Template><ControlTemplate TargetType='Thumb'><Border x:Name='Grip' Background='#B8C2D0' CornerRadius='4' Margin='3,2'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Grip' Property='Background' Value='#8390A1'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter TargetName='Grip' Property='Background' Value='#65717F'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
        <Track.IncreaseRepeatButton><RepeatButton x:Name='PageAfter' Command='ScrollBar.PageDownCommand' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
      </Track></Grid>
      <ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0'/></Trigger><Trigger Property='Orientation' Value='Horizontal'><Setter TargetName='PART_Track' Property='IsDirectionReversed' Value='False'/><Setter TargetName='PageBefore' Property='Command' Value='ScrollBar.PageLeftCommand'/><Setter TargetName='PageAfter' Property='Command' Value='ScrollBar.PageRightCommand'/><Setter TargetName='ScrollThumb' Property='MinHeight' Value='0'/><Setter TargetName='ScrollThumb' Property='MinWidth' Value='24'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
    <Style.Triggers><Trigger Property='Orientation' Value='Horizontal'><Setter Property='Width' Value='Auto'/><Setter Property='Height' Value='12'/></Trigger></Style.Triggers>
  </Style>
  <Style x:Key='Hint' TargetType='ToolTip'>
    <Setter Property='FontFamily' Value='Segoe UI, Microsoft YaHei'/><Setter Property='FontSize' Value='12'/><Setter Property='Foreground' Value='#536172'/>
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
        static readonly ResourceDictionary darkStyles = (ResourceDictionary)XamlReader.Parse(Recolor(sheet)
            .Replace("Property='Background' Value='#5B9BE0'", "Property='Background' Value='#225995'")
            .Replace("Property='Background' Value='#4A87C9'", "Property='Background' Value='#2E639E'")
            .Replace("Property='Background' Value='#D97068'", "Property='Background' Value='#943F3A'")
            .Replace("Property='Background' Value='#E0807B'", "Property='Background' Value='#A44742'"));
        static ResourceDictionary styles { get { return dark ? darkStyles : lightStyles; } }

        public static Style ButtonStyle { get { return (Style)styles["Action"]; } }
        public static Style HintStyle { get { return (Style)styles["Hint"]; } }
        public static Style LinkStyle { get { return (Style)styles["LinkAction"]; } }
        public static Style SelectionStyle { get { return (Style)styles["Selection"]; } }
        internal static Viewbox Icon(string name, double size = 18)
        {
            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(new System.Windows.Shapes.Path { Data = LightMenu.IconGeometry(name), Stroke = Brush("#647386"), StrokeThickness = 1.65, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
            return new Viewbox { Width = size, Height = size, Child = canvas, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        }
        public static Style NavigationItemStyle { get { return (Style)styles["NavigationItem"]; } }
        public static Style SwitchStyle { get { return (Style)styles["Switch"]; } }
        public static Style FieldStyle { get { return (Style)styles["Field"]; } }
        public static Style ChoiceStyle { get { return (Style)styles["Choice"]; } }
        // Windows 常用界面字体：西文 Segoe UI，中文明确回退到微软雅黑。
        public static readonly FontFamily UiFont = new FontFamily("Segoe UI, Microsoft YaHei");
        // 透明窗口拿不到 ClearType，但把排版模式切成 Display 能让小字号按整像素落笔，中文明显更实。
        public static void Polish(Window window)
        {
            TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(window, TextRenderingMode.ClearType);
            window.UseLayoutRounding = true;
            window.SnapsToDevicePixels = true;
            window.SourceInitialized += delegate { WindowsNative.SetTitleTheme(new System.Windows.Interop.WindowInteropHelper(window).Handle, dark); };
        }
        public static void FitDialog(Window window, UIElement content)
        {
            var screen = WindowsNative.ScreenAtPointer(); double scale = WindowsNative.MonitorScale(screen);
            window.Width = Math.Min(window.Width, screen.WorkingArea.Width / scale - 24);
            window.MaxHeight = Math.Max(120, screen.WorkingArea.Height / scale - 24);
            window.Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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
            // 渲染模式不再进程级强制软件：进程级 SoftwareOnly 的优先级高于窗口级设置，
            // 会把图片篮子、贴图这些大窗口也压进 CPU 渲染，滚动和缩放每帧整窗重画，明显卡顿。
            // 现在只对反复开关的小型透明挂件逐窗保持软件绘制（见 SoftwareRender/SoftwareRenderMenu），
            // 避免「反复开关面板积累大块图形缓存」的老问题；其余窗口走默认的硬件渲染。
            foreach (var type in new[] { typeof(RadioButton), typeof(CheckBox), typeof(Expander) })
            {
                var controlStyle = new Style(type);
                controlStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brush("#283343")));
                controlStyle.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
                app.Resources[type] = controlStyle;
            }
            app.Resources[typeof(ToolTip)] = styles["Hint"];
            app.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = styles["Scroll"];
        }

        // 让单个窗口保持软件渲染（构造函数里调用；真正的设置挂在 HWND 建好之后）。
        public static void SoftwareRender(Window window)
        {
            window.SourceInitialized += delegate
            {
                var source = PresentationSource.FromVisual(window) as System.Windows.Interop.HwndSource;
                if (source != null && source.CompositionTarget != null)
                    source.CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            };
        }

        // 右键菜单、托盘面板这类 ContextMenu 弹在独立 Popup 窗口里，只有打开后才能拿到它的 HwndSource。
        public static void SoftwareRenderMenu(ContextMenu menu)
        {
            menu.Opened += delegate
            {
                var source = PresentationSource.FromVisual(menu) as System.Windows.Interop.HwndSource;
                if (source != null && source.CompositionTarget != null)
                    source.CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            };
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
            var button = new ActionButton { Content = title, Style = ButtonStyle, Tag = tag };
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
