using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ScreenLine
{
    // Documentation fixtures: real WPF views and application handlers, synthetic content only.
    // Never starts clipboard collection, hotkeys, automatic cleanup or installation.
    internal static class DemoRecorder
    {
        static string output;
        static int frame;
        static void Pump(int ms)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until) { Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background); Thread.Sleep(4); }
        }
        static void Label(DrawingContext dc, string text, double x, double y, double size, string color)
        {
            dc.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei"), size, (Brush)new BrushConverter().ConvertFromString(color), 1.0), new Point(x, y));
        }
        static void Save(BitmapSource bitmap, string path)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
        static BitmapSource Render(FrameworkElement view)
        {
            view.UpdateLayout();
            var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(view.ActualWidth)), Math.Max(1, (int)Math.Ceiling(view.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view); return bitmap;
        }
        static void Start(string name) { output = Path.GetFullPath(Path.Combine("build", "demo-frames", name)); Directory.CreateDirectory(output); frame = 0; File.WriteAllText(Path.Combine(output, "timing.txt"), ""); }
        static void Shot(string title, string caption, int duration, Action<DrawingContext> draw)
        {
            int height = output.EndsWith("free-placement") || output.EndsWith("horizontal-scroll") ? 350 : 680;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(243, 246, 250)), null, new Rect(0, 0, 1100, height));
                Label(dc, "PickSnap", 36, 23, 23, "#24384B"); Label(dc, "2.0 / " + title, 185, 29, 15, "#687C90");
                draw(dc);
                dc.DrawRoundedRectangle(Brushes.White, null, new Rect(28, height - 64, 1044, 44), 12, 12);
                Label(dc, caption, 48, height - 55, 17, "#245B96");
            }
            var bitmap = new RenderTargetBitmap(1100, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            Save(bitmap, Path.Combine(output, (frame++).ToString("D4") + ".png"));
            File.AppendAllText(Path.Combine(output, "timing.txt"), duration + Environment.NewLine);
        }
        static void Panel(FrameworkElement panel, string title, string caption, int duration)
        {
            Pump(160); var image = Render(panel);
            Shot(title, caption, duration, dc => { double scale = Math.Min(1000.0 / image.Width, 525.0 / image.Height); dc.DrawImage(image, new Rect((1100 - image.Width * scale) / 2, 76, image.Width * scale, image.Height * scale)); });
        }
        static void Scene(ShelfWindow shelf, string caption, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (shelf.Width != 1000) shelf.Width = 1000;
                Pump(65);
                Shot(output.EndsWith("free-placement") ? "自由摆放图片" : output.EndsWith("horizontal-scroll") ? "横向滚动浏览" : "桌面贴图", caption, 80, dc => {
                    foreach (Window w in new Window[] { shelf }.Concat(shelf.MotionWindows.Cast<Window>()).Concat(shelf.PinWindows.Cast<Window>()).ToArray())
                        if (w.IsVisible) { var image = Render(w); dc.DrawImage(image, new Rect(w.Left - shelf.Left + 50, w.Top - shelf.Top + 82, image.Width, image.Height)); }
                });
            }
        }
        static string Sample(string root, int index)
        {
            string[] names = { "周末计划", "灵感配色", "项目进度" };
            string[] colors = { "#367EAA", "#B06F4D", "#577E69" };
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 500, 310));
                dc.DrawRectangle((Brush)new BrushConverter().ConvertFromString(colors[index]), null, new Rect(0, 0, 500, 70));
                Label(dc, names[index], 24, 18, 24, "#FFFFFF");
                string[][] lines = { new[] { "09:30  出发 · 城市漫步", "11:00  看一场展览", "14:00  咖啡与阅读" }, new[] { "海盐蓝   /   陶土橙", "留白，让内容呼吸", "收集今天的好想法" }, new[] { "界面整理     已完成", "交互验证     进行中", "发布记录     准备好了" } };
                for (int i = 0; i < 3; i++) { Label(dc, lines[index][i], 28, 98 + i * 57, 20, "#31455A"); dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(235, 240, 246)), 1), new Point(28, 142 + i * 57), new Point(470, 142 + i * 57)); }
            }
            var bitmap = new RenderTargetBitmap(500, 310, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            string path = Path.Combine(root, names[index] + ".png"); Save(bitmap, path); return path;
        }
        internal static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "PickSnap-demos-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            ShelfWindow shelf = null;
            try
            {
                var store = new StateStore(root); store.State.AutoPurge = false; store.State.CollectTextClipboard = true;
                var gallery = new Gallery(store); var texts = new TextCollection(store);
                texts.Add("灵感备忘：留白，让内容呼吸。"); texts.Add("待办清单\n整理截图与参考资料\n核对交互细节\n准备发布说明");
                texts.Add("一段值得保留的文字\n\n把零散的想法收进信封，需要时再展开。单击条目可以选择；长文字被截断时，双击正文即可阅读全文。\n\n复制时保留换行与缩进。支持搜索、单条复制，以及多选删除后的撤销和重做。");
                var paths = Enumerable.Range(0, 3).Select(i => Sample(root, i)).ToArray();
                gallery.Add(paths[0], ImageContent.Read(paths[0])); gallery.Add(paths[1], ImageContent.Read(paths[1]));
                shelf = new ShelfWindow(gallery, (a, item) => { }, () => { }); shelf.Show(); shelf.ChoosingAppearance = true;
                shelf.Width = 1000; shelf.Update(); shelf.Reveal(true, false, 300, true); Pump(600);
                Start("rope-pin"); Scene(shelf, "01  截图和复制的图片，收集后自动挂上绳子", 15);
                gallery.Add(paths[2], ImageContent.Read(paths[2])); shelf.Update();
                shelf.ShowNewCapture(gallery.Items.Last(), new Point(shelf.Display.Bounds.Left + 850, shelf.Display.Bounds.Top + 450));
                shelf.Width = 1000; Scene(shelf, "02  新图片上绳，保留在屏幕顶边", 25);
                var card = shelf.CardFor(paths[0]);
                typeof(ShelfWindow).GetMethod("ReleasePhoto", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(shelf, new object[] { card });
                Scene(shelf, "03  单击小夹子：变成置顶贴图，方便对照", 30);
                var pin = shelf.PinWindows.Single(); pin.PinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Scene(shelf, "04  单击贴图顶部图钉：挂回绳上原处", 30);
                gallery.Remove(gallery.Items.First(i => i.Path == paths[1])); gallery.Remove(gallery.Items.First(i => i.Path == paths[2])); shelf.Update();
                var basket = new BasketWindow(gallery, item => { gallery.Add(item.Path, item.Image); shelf.Update(); }, (a, item) => { }, null, () => shelf.RoomForHanging(), () => shelf.RopeCapacity) { Width = 940, Height = 490 };
                basket.Show(); Start("basket");
                Panel(basket, "图片篮子 / 取下、选择、挂回", "01  暂时不用的图片放进篮子，文件仍然保留", 1800);
                basket.ChoiceFor(paths[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Panel(basket, "图片篮子 / 取下、选择、挂回", "02  单击选择并预览，底部显示绳上剩余位置", 1800);
                basket.SelectAllBox.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Panel(basket, "图片篮子 / 取下、选择、挂回", "03  全选待挂图片，一次挂回绳子", 1600);
                basket.HangButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Panel(basket, "图片篮子 / 取下、选择、挂回", "04  挂回完成；彩色计数同步更新", 2000); basket.Close();
                var text = new TextWindow(texts, shelf); text.Show(); Start("text-envelope");
                Panel(text, "文字信封 / 收集、搜索、阅读全文", "01  复制过的文字集中保存，保留换行与缩进", 1800);
                text.HandleRowClick(texts.Items[0], 1, true, 1000);
                Panel(text, "文字信封 / 收集、搜索、阅读全文", "02  单击条目即可选中；复制、删除按钮独立操作", 1600);
                text.HandleRowClick(texts.Items[0], 2, true, 1160); Pump(200);
                var reader = Application.Current.Windows.OfType<TextReaderWindow>().Single();
                Panel(reader, "文字信封 / 收集、搜索、阅读全文", "03  双击被截断的长文正文，展开阅读全文", 2400); reader.Close();
                text = new TextWindow(texts, shelf); text.Show(); text.Search.Text = "清单";
                Panel(text, "文字信封 / 收集、搜索、阅读全文", "04  输入关键词，快速找回需要的文字", 2000); text.Close();
                shelf.Width = 1000; shelf.Update(); shelf.Reveal(true, false, 300, true); Pump(400);
                Start("free-placement"); Scene(shelf, "按住图片上方的夹子，沿绳子自由移动", 15);
                var moving = shelf.CardFor(paths[0]); double initial = moving.MountX;
                var startRendering = typeof(ShelfWindow).GetMethod("StartRendering", BindingFlags.NonPublic | BindingFlags.Instance);
                for (int n = 0; n < 35; n++) {
                    moving.Place(initial + 320 * Math.Sin(n / 34.0 * Math.PI / 2), true);
                    startRendering.Invoke(shelf, null); Scene(shelf, "拖到哪里，就放在哪里；其他图片保持原位", 1);
                }
                Scene(shelf, "自由留出空隙，把需要的图片放在顺手的位置", 20);
                for (int n = 0; n < 30; n++) {
                    moving.Place(initial + 320 * (1 - n / 29.0), true); startRendering.Invoke(shelf, null);
                    Scene(shelf, "还可以随时重新摆放", 1);
                }
                for (int n = 0; n < 7; n++) { string extra = Path.Combine(root, "参考图-" + n + ".png"); File.Copy(paths[n % 3], extra); gallery.Add(extra, ImageContent.Read(extra)); }
                shelf.Update(); shelf.Width = 1000; Pump(400); Start("horizontal-scroll");
                Scene(shelf, "图片超过一屏时，在绳子区域滚动鼠标滚轮", 15);
                var wheel = typeof(ShelfWindow).GetMethod("ScrollByWheel", BindingFlags.NonPublic | BindingFlags.Instance);
                for (int n = 0; n < 7; n++) { wheel.Invoke(shelf, new object[] { -120.0 }); Scene(shelf, "横向浏览整条绳子，不必把图片一张张取下", 6); }
                Scene(shelf, "停下后自动对齐，继续查看右侧图片", 15);
                for (int n = 0; n < 7; n++) { wheel.Invoke(shelf, new object[] { 120.0 }); Scene(shelf, "反向滚动，回到前面的图片", 6); }
                Console.WriteLine("Documentation frames saved under build/demo-frames; synthetic content only.");
            }
            finally
            {
                foreach (Window w in Application.Current.Windows.Cast<Window>().ToArray()) w.Close();
                if (!FileRules.IsInside(root, Path.GetTempPath())) throw new InvalidOperationException("Unsafe fixture path");
                Directory.Delete(root, true);
            }
        }
    }
}
