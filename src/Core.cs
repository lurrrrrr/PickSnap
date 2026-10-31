using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using Microsoft.VisualBasic.FileIO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenLine
{
    internal sealed class SavedState
    {
        public bool CollectClipboard { get; set; }
        public bool CollectTextClipboard { get; set; }
        public bool AutoHang { get; set; }
        public bool HoverPreview { get; set; }
        public string Theme { get; set; }
        // 界面配色：auto（跟随系统）/ light / dark
        public string AppTheme { get; set; }
        public string DisplayDevice { get; set; }
        // 被拖动过的夹子在绳子上的位置（图片路径 → 内容坐标 X）。没记录的沿用默认均分排列。
        public Dictionary<string, double> CardOffsets { get; set; }
        public string LastUpdateNotification { get; set; }
        // 用户点「跳过此版本」记录的版本号：自动检查不再提醒它，直到出现更新的版本；手动检查仍然可见。
        public string SkippedUpdate { get; set; }
        public uint CaptureModifiers { get; set; }
        public uint CaptureKey { get; set; }
        // 旧图自动清理：默认关闭。开启后按天数把过期的图片移出（收集文件夹内的副本进回收站）。
        public bool AutoPurge { get; set; }
        // 保留最近多少天的截图：配合上面的开关使用，默认 7 天。
        public int AutoPurgeDays { get; set; }
        public List<string> Images { get; set; }
        public List<string> Collection { get; set; }
        public List<TextItem> Texts { get; set; }

        public SavedState() { Images = new List<string>(); Collection = new List<string>(); Texts = new List<TextItem>(); CardOffsets = new Dictionary<string, double>(); Theme = "classic"; AppTheme = "auto"; AutoHang = true; HoverPreview = true; AutoPurgeDays = 7; CaptureModifiers = 3; CaptureKey = (uint)'S'; }
    }

    internal sealed class StateStore
    {
        public static string DefaultRoot { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"); } }
        public readonly string Root;
        public readonly string Inbox;
        public readonly SavedState State;
        // 首次启动（尚无 state.json）：用于触发一次性动作（如在桌面创建快捷方式）。
        public bool FreshInstall { get; private set; }
        readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        readonly string file;

        public StateStore(string root)
        {
            Root = Path.GetFullPath(root);
            Inbox = Path.Combine(Root, "Captures");
            file = Path.Combine(Root, "state.json");
            FreshInstall = !File.Exists(file);
            State = new SavedState();
            if (!File.Exists(file)) return;
            try
            {
                State = serializer.Deserialize<SavedState>(File.ReadAllText(file, Encoding.UTF8)) ?? new SavedState();
                if (State.Images == null) State.Images = new List<string>();
                if (State.Collection == null) State.Collection = new List<string>();
                if (State.Texts == null) State.Texts = new List<TextItem>();
                if (State.CardOffsets == null) State.CardOffsets = new Dictionary<string, double>();
                if (string.IsNullOrEmpty(State.AppTheme)) State.AppTheme = "auto";
                if (State.AutoPurgeDays <= 0) State.AutoPurgeDays = 7;
            }
            catch (ArgumentException ex) { Log(ex); }
            catch (InvalidOperationException ex) { Log(ex); }
            catch (IOException ex) { Log(ex); }
        }

        public void Save()
        {
            Directory.CreateDirectory(Root);
            string temporary = file + ".tmp";
            File.WriteAllText(temporary, serializer.Serialize(State), new UTF8Encoding(false));
            FileRules.Commit(temporary, file);
        }

        string lastError;
        DateTime lastErrorUtc;

        public void Log(Exception error)
        {
            // 同一条错误 5 秒内只记一次：剪贴板被外部程序高频污染时会瞬间产生几十万条
            // 相同异常，曾把 error.log 刷到 758MB。
            string body = error.ToString();
            if (body == lastError && (DateTime.UtcNow - lastErrorUtc).TotalSeconds < 5) return;
            lastError = body; lastErrorUtc = DateTime.UtcNow;
            try
            {
                Directory.CreateDirectory(Root);
                string file = Path.Combine(Root, "error.log");
                // 日志轮转：超过 1MB 只保留末尾 128KB，日志永远不会占用可观空间。
                var info = new FileInfo(file);
                if (info.Exists && info.Length > 1024 * 1024)
                {
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (stream.Length > 128 * 1024) stream.Seek(-128 * 1024, SeekOrigin.End);
                        string tail;
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            reader.ReadLine(); // 丢掉可能被截断的半行
                            tail = reader.ReadToEnd();
                        }
                        File.WriteAllText(file, tail, new UTF8Encoding(false));
                    }
                }
                File.AppendAllText(file, DateTime.Now.ToString("s") + " " + error + Environment.NewLine);
            }
            catch { } // 记日志失败绝不能拖垮主流程
        }
    }

    internal sealed class TextItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Content { get; set; }
        DateTime createdUtc;
        public DateTime CreatedUtc
        {
            get { return createdUtc; }
            set
            {
                if (createdUtc == value) return;
                createdUtc = value;
                if (PropertyChanged != null) PropertyChanged(this, new System.ComponentModel.PropertyChangedEventArgs("CreatedUtc"));
            }
        }
        bool marked;
        [System.Web.Script.Serialization.ScriptIgnore]
        public bool IsMarked
        {
            get { return marked; }
            set { if (marked == value) return; marked = value; if (PropertyChanged != null) PropertyChanged(this, new System.ComponentModel.PropertyChangedEventArgs("IsMarked")); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    internal sealed class TextCollection
    {
        public readonly StateStore Store;
        public List<TextItem> Items { get { return Store.State.Texts; } }
        public event Action Changed;

        public TextCollection(StateStore store) { Store = store; }

        public bool CollectClipboard
        {
            get { return Store.State.CollectTextClipboard; }
            set
            {
                bool previous = CollectClipboard;
                if (previous == value) return;
                Store.State.CollectTextClipboard = value;
                try { Store.Save(); }
                catch { Store.State.CollectTextClipboard = previous; throw; }
                if (Changed != null) Changed();
            }
        }

        public TextItem Add(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;
            var existing = Items.Find(item => item.Content == content);
            if (existing != null)
            {
                // 再次复制已收藏的同一条文字：把它提到最前面，时间也更新为这一次，
                // 这样列表始终按「最近复制」排序，而不是按「最早收藏」。
                int index = Items.IndexOf(existing);
                if (index == 0) return existing;
                DateTime previousTime = existing.CreatedUtc;
                Items.RemoveAt(index);
                existing.CreatedUtc = DateTime.UtcNow;
                Items.Insert(0, existing);
                try { Store.Save(); }
                catch { Items.RemoveAt(0); existing.CreatedUtc = previousTime; Items.Insert(index, existing); throw; }
                if (Changed != null) Changed();
                return existing;
            }
            var added = new TextItem { Id = Guid.NewGuid().ToString("N"), Content = content, CreatedUtc = DateTime.UtcNow };
            Items.Insert(0, added);
            // 文字信封自动修剪：剪贴板文字是零碎内容，只保留最近 100 条，
            // 避免 state.json 随长期使用无限膨胀；篮子里的图片是用户文件，不做任何自动清理。
            while (Items.Count > 100) Items.RemoveAt(Items.Count - 1);
            try { Store.Save(); }
            catch { Items.Remove(added); throw; }
            if (Changed != null) Changed();
            return added;
        }

        public void Remove(TextItem item)
        {
            int index = Items.IndexOf(item);
            if (index < 0) return;
            Items.RemoveAt(index);
            try { Store.Save(); }
            catch { Items.Insert(index, item); throw; }
            if (Changed != null) Changed();
        }

        public void RemoveMany(IEnumerable<TextItem> selected)
        {
            var ids = new HashSet<string>(selected.Select(item => item.Id));
            var previous = Items.ToArray();
            if (Items.RemoveAll(item => ids.Contains(item.Id)) == 0) return;
            try { Store.Save(); }
            catch { Items.Clear(); Items.AddRange(previous); throw; }
            if (Changed != null) Changed();
        }

        // 删除但保留退路：返回被删的条目和它们原来的位置，撤销时原样插回去。
        // 文字只存在 state.json 这一份记录里，删错了没有回收站可以找，所以必须留这个口子。
        public List<KeyValuePair<int, TextItem>> RemoveUndoable(IEnumerable<TextItem> selected)
        {
            var ids = new HashSet<string>(selected.Select(item => item.Id));
            var removed = new List<KeyValuePair<int, TextItem>>();
            var previous = Items.ToArray();
            for (int index = Items.Count - 1; index >= 0; index--)
            {
                if (!ids.Contains(Items[index].Id)) continue;
                removed.Insert(0, new KeyValuePair<int, TextItem>(index, Items[index]));
                Items.RemoveAt(index);
            }
            if (removed.Count == 0) return removed;
            try { Store.Save(); }
            catch { Items.Clear(); Items.AddRange(previous); throw; }
            if (Changed != null) Changed();
            return removed;
        }

        public void Restore(List<KeyValuePair<int, TextItem>> removed)
        {
            if (removed == null || removed.Count == 0) return;
            var previous = Items.ToArray();
            foreach (var entry in removed.OrderBy(pair => pair.Key))
            {
                int index = Math.Max(0, Math.Min(Items.Count, entry.Key));
                Items.Insert(index, entry.Value);
            }
            try { Store.Save(); }
            catch { Items.Clear(); Items.AddRange(previous); throw; }
            if (Changed != null) Changed();
        }
    }

    internal static class FileRules
    {
        static readonly string[] extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

        public static void Commit(string temporary, string destination)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(destination)) File.Replace(temporary, destination, null);
                    else File.Move(temporary, destination);
                    return;
                }
                catch (IOException error)
                {
                    int code = error.HResult & 65535;
                    if (attempt == 4 || (code != 32 && code != 33 && code != 1175)) throw;
                    // 索引或杀毒扫描短暂占用时重试，仍以原子替换保护已有文件。
                    System.Threading.Thread.Sleep(30);
                }
            }
        }

        public static bool IsImage(string path)
        {
            return extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsInside(string path, string directory)
        {
            string prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool SameFile(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class ClipboardRules
    {
        public const string OwnFormat = "PickSnap.OwnClipboard";
        internal static readonly KeyValuePair<string, string>[] EncodedFormats = {
            new KeyValuePair<string, string>("PNG", ".png"), new KeyValuePair<string, string>("image/png", ".png"),
            new KeyValuePair<string, string>("image/jpeg", ".jpg"), new KeyValuePair<string, string>("image/jpg", ".jpg"), new KeyValuePair<string, string>("image/jpe", ".jpg"),
            new KeyValuePair<string, string>("image/bmp", ".bmp"), new KeyValuePair<string, string>("image/gif", ".gif"),
            new KeyValuePair<string, string>("image/tiff", ".tiff"), new KeyValuePair<string, string>("image/tif", ".tiff")
        };
        static readonly string[] excluded = { "HTML Format", "UnicodeText", "Text", "System.String", "FileDrop", "Rich Text Format" };
        static readonly TimeSpan htmlTimeout = TimeSpan.FromMilliseconds(200);

        public static bool IsPlainImage(IEnumerable<string> formats)
        {
            var values = new HashSet<string>(formats, StringComparer.OrdinalIgnoreCase);
            return !values.Overlaps(excluded) && HasImage(values);
        }

        static bool HasImage(HashSet<string> formats)
        {
            return formats.Contains("Bitmap") || formats.Contains("DeviceIndependentBitmap") || formats.Contains("Format17")
                || EncodedFormats.Any(format => formats.Contains(format.Key));
        }

        public static bool IsImage(IDataObject data) { return IsImage(data, null); }

        public static bool IsImage(IDataObject data, string source)
        {
            string imageFile;
            return IsImage(data, source, out imageFile);
        }

        public static bool IsImage(IDataObject data, string source, out string imageFile)
        {
            imageFile = null;
            var formats = new HashSet<string>(data.GetFormats(), StringComparer.OrdinalIgnoreCase);
            if (formats.Contains(OwnFormat)) return false;
            if (IsPlainImage(formats)) return true;
            if (formats.Contains("FileDrop"))
            {
                bool wechat = string.Equals(source, "Weixin", StringComparison.OrdinalIgnoreCase) || string.Equals(source, "WeChat", StringComparison.OrdinalIgnoreCase);
                if (!wechat || !formats.Contains("image/png") || !formats.Contains("DeviceIndependentBitmap")
                    || formats.Contains("HTML Format") || formats.Contains("Rich Text Format") || formats.Contains("UnicodeText") || formats.Contains("Text") || formats.Contains("System.String")) return false;
                var files = data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length != 1 || !FileRules.IsImage(files[0])) return false;
                imageFile = files[0]; return true;
            }
            if (formats.Contains("Rich Text Format") || !formats.Contains("HTML Format") || !HasImage(formats)) return false;
            string html = data.GetData(DataFormats.Html) as string;
            if (string.IsNullOrEmpty(html) || html.Length > 1024 * 1024) return false;
            const string start = "<!--StartFragment-->", end = "<!--EndFragment-->";
            int first = html.IndexOf(start, StringComparison.OrdinalIgnoreCase), last = html.IndexOf(end, StringComparison.OrdinalIgnoreCase);
            if (first >= 0 || last >= 0)
            {
                if (first < 0 || last < first + start.Length) return false;
                html = html.Substring(first + start.Length, last - first - start.Length);
            }
            else
            {
                int document = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
                if (document >= 0) html = html.Substring(document);
            }
            try
            {
                var options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
                html = Regex.Replace(html, @"<!--[\s\S]*?-->", "", options, htmlTimeout);
                const string tail = @"(?:[^'"">]|""[^""]*""|'[^']*')*";
                var images = Regex.Matches(html, @"<\s*img\b" + tail + @">", options, htmlTimeout);
                if (images.Count != 1) return false;
                string text = data.GetData(DataFormats.UnicodeText) as string ?? data.GetData(DataFormats.Text) as string ?? data.GetData("System.String") as string;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var imageSource = Regex.Match(images[0].Value, @"\ssrc\s*=\s*(?:""(?<url>[^""]*)""|'(?<url>[^']*)'|(?<url>[^\s>]+))", options, htmlTimeout);
                    if (!imageSource.Success || !string.Equals(text.Trim(), WebUtility.HtmlDecode(imageSource.Groups["url"].Value), StringComparison.Ordinal)) return false;
                }
                html = html.Remove(images[0].Index, images[0].Length);
                html = Regex.Replace(html, @"<\s*/?\s*(?:html|body|div|span|p|a|figure|br)\b" + tail + @">", "", options, htmlTimeout);
                return string.IsNullOrWhiteSpace(WebUtility.HtmlDecode(html));
            }
            catch (RegexMatchTimeoutException) { return false; }
        }

    }

    internal sealed class ImageContent
    {
        static readonly LinkedList<ImageContent> cache = new LinkedList<ImageContent>();
        BitmapSource thumbnail;
        string backingPath;
        LinkedListNode<ImageContent> cacheNode;
        internal BitmapSource CachedThumbnail
        {
            get
            {
                if (!System.Threading.Monitor.TryEnter(cache)) return null;
                try { if (thumbnail != null) Touch(); return thumbnail; }
                finally { System.Threading.Monitor.Exit(cache); }
            }
        }
        public BitmapSource Thumbnail
        {
            get
            {
                string path;
                lock (cache)
                {
                    if (thumbnail != null) { Touch(); return thumbnail; }
                    path = backingPath;
                }
                BitmapSource loaded;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    loaded = MakeThumbnail(stream, PixelWidth, PixelHeight);
                lock (cache)
                {
                    if (thumbnail == null) thumbnail = loaded;
                    Touch(); return thumbnail;
                }
            }
        }
        public readonly string Fingerprint;
        public readonly int PixelWidth, PixelHeight;

        ImageContent(BitmapSource thumbnail, string fingerprint, int width, int height)
        {
            this.thumbnail = thumbnail;
            Fingerprint = fingerprint;
            PixelWidth = width;
            PixelHeight = height;
        }

        internal void BindFile(string path)
        {
            lock (cache) { backingPath = path; if (thumbnail != null) Touch(); }
        }

        internal ImageContent ForFile(string path)
        {
            lock (cache)
            {
                var image = backingPath != null && !FileRules.SameFile(backingPath, path)
                    ? new ImageContent(thumbnail, Fingerprint, PixelWidth, PixelHeight) : this;
                image.BindFile(path); return image;
            }
        }

        void Touch()
        {
            if (backingPath == null) return;
            if (cacheNode != null) cache.Remove(cacheNode);
            cacheNode = cache.AddLast(this);
            while (cache.Count > 64)
            {
                var evicted = cache.First.Value; cache.RemoveFirst();
                evicted.cacheNode = null; evicted.thumbnail = null;
            }
        }

        public static ImageContent Read(string path)
        {
            return Read(path, true);
        }

        public static ImageContent Read(string path, bool fingerprint)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length == 0 || stream.Length > 64L * 1024 * 1024) throw new InvalidDataException("Image file size is invalid");
                if (!fingerprint)
                {
                    var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                    if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || (long)frame.PixelWidth * frame.PixelHeight > 80000000) throw new InvalidDataException("Image dimensions are invalid");
                    var metadata = new ImageContent(null, null, frame.PixelWidth, frame.PixelHeight); metadata.BindFile(path); return metadata;
                }
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    var image = Decode(memory.ToArray(), fingerprint); image.BindFile(path); return image;
                }
            }
        }

        public static ImageContent Decode(byte[] bytes)
        {
            return Decode(bytes, true);
        }

        static ImageContent Decode(byte[] bytes, bool fingerprint)
        {
            using (var memory = new MemoryStream(bytes))
            {
                var full = BitmapDecoder.Create(memory, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                if (full.PixelWidth <= 0 || full.PixelHeight <= 0 || (long)full.PixelWidth * full.PixelHeight > 80000000)
                    throw new InvalidDataException("Image dimensions are invalid");
                string hash = fingerprint ? HashPixels(full) : null;
                memory.Position = 0;
                return new ImageContent(MakeThumbnail(memory, full.PixelWidth, full.PixelHeight), hash, full.PixelWidth, full.PixelHeight);
            }
        }

        static BitmapSource MakeThumbnail(Stream stream, int width, int height)
        {
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream;
            if (width >= height) image.DecodePixelWidth = Math.Min(240, width); else image.DecodePixelHeight = Math.Min(240, height);
            image.EndInit();
            var color = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = color.PixelWidth * 4; byte[] pixels = new byte[stride * color.PixelHeight];
            color.CopyPixels(pixels, stride, 0);
            var detached = BitmapSource.Create(color.PixelWidth, color.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            detached.Freeze(); return detached;
        }

        public static byte[] EncodeClipboard(BitmapSource image)
        {
            // 部分 Windows 位图的透明通道全为零，截图应按不透明图像保存。
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            byte[] rows = new byte[stride * Math.Min(32, converted.PixelHeight)];
            bool emptyAlpha = true;
            for (int y = 0; y < converted.PixelHeight && emptyAlpha; y += 32)
            {
                int height = Math.Min(32, converted.PixelHeight - y);
                converted.CopyPixels(new Int32Rect(0, y, converted.PixelWidth, height), rows, stride, 0);
                for (int i = 3; i < stride * height; i += 4)
                    if (rows[i] != 0) { emptyAlpha = false; break; }
            }
            if (emptyAlpha)
            {
                byte[] pixels = new byte[stride * converted.PixelHeight];
                converted.CopyPixels(pixels, stride, 0);
                for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                image = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var memory = new MemoryStream()) { encoder.Save(memory); return memory.ToArray(); }
        }

        public static string HashPixels(BitmapSource image)
        {
            var color = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = color.PixelWidth * 4;
            byte[] header = new byte[8];
            BitConverter.GetBytes(color.PixelWidth).CopyTo(header, 0);
            BitConverter.GetBytes(color.PixelHeight).CopyTo(header, 4);
            byte[] pixels = new byte[stride * Math.Min(32, color.PixelHeight)];
            using (var sha = SHA256.Create())
            {
                sha.TransformBlock(header, 0, header.Length, header, 0);
                for (int y = 0; y < color.PixelHeight; y += 32)
                {
                    int height = Math.Min(32, color.PixelHeight - y);
                    color.CopyPixels(new Int32Rect(0, y, color.PixelWidth, height), pixels, stride, 0);
                    sha.TransformBlock(pixels, 0, stride * height, pixels, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return Convert.ToBase64String(sha.Hash);
            }
        }

        // 两张图的缩略图像素差异比例（0 表示完全相同，1 表示全不相同）。
        // 截图工具的编辑器每改动一笔都会把整张图重新写进剪贴板，靠这个比例判断是不是同一张截图的连续版本。
        public static double Difference(ImageContent left, ImageContent right)
        {
            if (left == null || right == null) return 1;
            if (left.PixelWidth != right.PixelWidth || left.PixelHeight != right.PixelHeight) return 1;
            BitmapSource a, b;
            try { a = left.Thumbnail; b = right.Thumbnail; }
            catch (IOException) { return 1; }
            catch (NotSupportedException) { return 1; }
            if (a == null || b == null || a.PixelWidth != b.PixelWidth || a.PixelHeight != b.PixelHeight) return 1;
            int stride = a.PixelWidth * 4;
            byte[] first = new byte[stride * a.PixelHeight], second = new byte[stride * b.PixelHeight];
            a.CopyPixels(first, stride, 0);
            b.CopyPixels(second, stride, 0);
            long changed = 0;
            for (int i = 0; i < first.Length; i += 4)
                if (Math.Abs(first[i] - second[i]) + Math.Abs(first[i + 1] - second[i + 1]) + Math.Abs(first[i + 2] - second[i + 2]) > 32) changed++;
            return (double)changed / (a.PixelWidth * a.PixelHeight);
        }

        // 截图工具的编辑器打开、切换工具栏、撤销时会往画布补一圈空白，尺寸会小幅变化（例如 1597x1126 → 1707x1126，高度不变）。
        // 这种情况不能按"尺寸不同就是新图"处理：按同一缩放比例解码（不是同一宽度，那会把内容压扁），
        // 再拿小的那张在大图里滑动找最吻合的位置，取最小差异。
        public static double DifferenceAcrossSizes(string path, int pathWidth, byte[] bytes, int bytesWidth)
        {
            int widest = Math.Max(pathWidth, bytesWidth);
            if (widest <= 0) return 1;
            double scale = Math.Min(1.0, 260.0 / widest);
            BitmapSource a = DecodeWidth(path, null, Math.Max(1, (int)Math.Round(pathWidth * scale)));
            BitmapSource b = DecodeWidth(null, bytes, Math.Max(1, (int)Math.Round(bytesWidth * scale)));
            if (a == null || b == null) return 1;
            BitmapSource small = a.PixelWidth * a.PixelHeight <= b.PixelWidth * b.PixelHeight ? a : b;
            BitmapSource large = ReferenceEquals(small, a) ? b : a;
            int smallStride = small.PixelWidth * 4, largeStride = large.PixelWidth * 4;
            byte[] inner = new byte[smallStride * small.PixelHeight], outer = new byte[largeStride * large.PixelHeight];
            small.CopyPixels(inner, smallStride, 0);
            large.CopyPixels(outer, largeStride, 0);
            int roomX = large.PixelWidth - small.PixelWidth, roomY = large.PixelHeight - small.PixelHeight;
            // 差得太多（超过一半）就不是"补了点白边"，直接当新图。
            if (roomX < 0 || roomY < 0 || roomX > small.PixelWidth / 2 || roomY > small.PixelHeight / 2) return 1;
            double best = 1;
            for (int offsetY = 0; offsetY <= roomY; offsetY++)
            {
                for (int offsetX = 0; offsetX <= roomX; offsetX++)
                {
                    long changed = 0;
                    for (int y = 0; y < small.PixelHeight; y++)
                    {
                        int rowInner = y * smallStride, rowOuter = (offsetY + y) * largeStride + offsetX * 4;
                        for (int x = 0; x < small.PixelWidth; x++)
                        {
                            int i = rowInner + x * 4, j = rowOuter + x * 4;
                            if (Math.Abs(inner[i] - outer[j]) + Math.Abs(inner[i + 1] - outer[j + 1]) + Math.Abs(inner[i + 2] - outer[j + 2]) > 32) changed++;
                        }
                    }
                    double ratio = (double)changed / (small.PixelWidth * small.PixelHeight);
                    if (ratio < best) best = ratio;
                    if (best == 0) return 0;
                }
            }
            return best;
        }

        static BitmapSource DecodeWidth(string path, byte[] bytes, int width)
        {
            try
            {
                using (Stream stream = path != null
                    ? (Stream)new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)
                    : new MemoryStream(bytes, false))
                {
                    BitmapFrame frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    int natural = frame.PixelWidth;
                    stream.Position = 0;
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.DecodePixelWidth = Math.Max(1, Math.Min(width, natural));
                    image.EndInit();
                    image.Freeze();
                    return new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                }
            }
            catch (IOException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (ArgumentException) { return null; }
        }
    }

    internal enum CaptureSource { Folder, Clipboard }

    internal sealed class CaptureReceipts
    {
        sealed class Receipt
        {
            public string Hash;
            public CaptureSource Source;
            public DateTime Time;
            public bool Paired;
        }

        readonly List<Receipt> recent = new List<Receipt>();

        public bool Accept(string hash, CaptureSource source, DateTime time)
        {
            return Accept(hash, source, time, TimeSpan.Zero);
        }

        // repeat：同一来源在窗口内再次到达相同内容时视为重复。
        // 截图工具在一次截图或标注过程中会反复重写剪贴板，用它吸收这些重写，避免同一次操作留下多张副本。
        public bool Accept(string hash, CaptureSource source, DateTime time, TimeSpan repeat)
        {
            recent.RemoveAll(r => time - r.Time > TimeSpan.FromSeconds(5));
            if (repeat > TimeSpan.Zero && recent.Any(r => r.Hash == hash && r.Source == source && time - r.Time <= repeat)) return false;
            // 只合并文件夹与剪贴板对同一次截图的通知；连续截取相同内容仍可保留。
            var pair = recent.FirstOrDefault(r => !r.Paired && r.Hash == hash && r.Source != source);
            if (pair != null) { pair.Paired = true; return false; }
            recent.Add(new Receipt { Hash = hash, Source = source, Time = time });
            return true;
        }
    }

    internal sealed class CaptureItem
    {
        public readonly string Path;
        public ImageContent Image;
        public DateTime Written;

        public CaptureItem(string path, ImageContent image)
        {
            Path = System.IO.Path.GetFullPath(path);
            Image = image.ForFile(Path);
            Written = File.GetLastWriteTimeUtc(Path);
        }
    }

    internal sealed class Gallery
    {
        public readonly List<CaptureItem> Items = new List<CaptureItem>();
        public readonly List<CaptureItem> Collected = new List<CaptureItem>();
        public readonly StateStore Store;
        public event Action Changed;

        public Gallery(StateStore store) { Store = store; }

        public bool Contains(string path) { return Items.Any(i => FileRules.SameFile(i.Path, path)); }

        public CaptureItem Collect(string path, ImageContent image)
        {
            var item = Collected.FirstOrDefault(i => FileRules.SameFile(i.Path, path));
            if (item != null) return item;
            item = new CaptureItem(path, image);
            Collected.Add(item);
            Save();
            Notify();
            return item;
        }

        public void Add(string path, ImageContent image)
        {
            if (Contains(path)) return;
            var item = Collected.FirstOrDefault(i => FileRules.SameFile(i.Path, path));
            if (item == null) { item = new CaptureItem(path, image); Collected.Add(item); }
            else if (item.Image != image) { item.Image = image.ForFile(path); item.Written = File.GetLastWriteTimeUtc(path); }
            Items.Add(item);
            if (Items.Count > 50) Items.RemoveAt(0);
            Save();
            Notify();
        }

        public void Remove(CaptureItem item)
        {
            Items.Remove(item);
            Save();
            Notify();
        }

        public void RemoveMany(IEnumerable<CaptureItem> items)
        {
            var removed = new HashSet<CaptureItem>(items);
            var previous = Items.ToArray();
            if (Items.RemoveAll(item => removed.Contains(item)) == 0) return;
            try { Save(); }
            catch { Items.Clear(); Items.AddRange(previous); Store.State.Images = Items.Select(i => i.Path).ToList(); throw; }
            Notify();
        }

        public void Clear()
        {
            Items.Clear();
            Save();
            Notify();
        }

        public void Forget(CaptureItem item)
        {
            int hanging = Items.IndexOf(item), collected = Collected.IndexOf(item);
            Items.Remove(item);
            Collected.Remove(item);
            try { Save(); }
            catch
            {
                if (hanging >= 0) Items.Insert(hanging, item);
                if (collected >= 0) Collected.Insert(collected, item);
                Store.State.Images = Items.Select(i => i.Path).ToList();
                Store.State.Collection = Collected.Select(i => i.Path).ToList();
                throw;
            }
            Notify();
        }

        public CaptureItem Replace(CaptureItem item, string path)
        {
            int hanging = Items.IndexOf(item), collected = Collected.IndexOf(item);
            if (collected < 0) return null;
            var copy = new CaptureItem(path, item.Image);
            if (hanging >= 0) Items[hanging] = copy;
            Collected[collected] = copy;
            try { Save(); }
            catch
            {
                if (hanging >= 0) Items[hanging] = item;
                Collected[collected] = item;
                Store.State.Images = Items.Select(i => i.Path).ToList();
                Store.State.Collection = Collected.Select(i => i.Path).ToList();
                throw;
            }
            Notify();
            return copy;
        }

        // 用新图片顶替同一条记录，位置保持不变：截图工具标注过程中的中间版本不应该在绳子上留下一串副本。
        public CaptureItem ReplaceImage(CaptureItem item, string path, ImageContent image)
        {
            int hanging = Items.IndexOf(item), collected = Collected.IndexOf(item);
            if (collected < 0) return null;
            var copy = new CaptureItem(path, image);
            if (hanging >= 0) Items[hanging] = copy;
            Collected[collected] = copy;
            try { Save(); }
            catch
            {
                if (hanging >= 0) Items[hanging] = item;
                Collected[collected] = item;
                Store.State.Images = Items.Select(i => i.Path).ToList();
                Store.State.Collection = Collected.Select(i => i.Path).ToList();
                throw;
            }
            Notify();
            return copy;
        }

        public void Prune()
        {
            int removed = Items.RemoveAll(i => !File.Exists(i.Path));
            removed += Collected.RemoveAll(i => !File.Exists(i.Path));
            if (removed == 0) return;
            Save();
            Notify();
        }

        // 清理「几天前」的旧图：超过这个时间的记录全部移出清单，
        // 收集文件夹内的副本同时送进回收站（还能找回），手动添加的图片只移除记录、不改动原文件。
        // 返回实际清理的张数。
        public int PurgeOlderThan(int days)
        {
            if (days <= 0) return 0;
            DateTime deadline = DateTime.UtcNow.AddDays(-days);
            var stale = Collected.Where(item => Stamp(item) < deadline).ToArray();
            if (stale.Length == 0) return 0;
            var removed = new HashSet<CaptureItem>(stale);
            foreach (var item in stale)
            {
                if (!FileRules.IsInside(item.Path, Store.Inbox)) continue;
                try
                {
                    if (File.Exists(item.Path)) FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.DoNothing);
                }
                catch (Exception error) { Store.Log(error); }
            }
            Items.RemoveAll(item => removed.Contains(item));
            Collected.RemoveAll(item => removed.Contains(item));
            Save();
            Notify();
            return stale.Length;
        }

        // 判断「新旧」优先看文件当前的修改时间：截图被标注、被替换过之后记录里的旧时间不再准确。
        static DateTime Stamp(CaptureItem item)
        {
            try { return File.Exists(item.Path) ? File.GetLastWriteTimeUtc(item.Path) : item.Written; }
            catch { return item.Written; }
        }

        public void Notify() { if (Changed != null) Changed(); }

        void Save()
        {
            Store.State.Images = Items.Select(i => i.Path).ToList();
            Store.State.Collection = Collected.Select(i => i.Path).ToList();
            Store.Save();
        }
    }
}
