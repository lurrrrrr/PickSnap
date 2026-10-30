using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ScreenLine
{
    internal sealed class ClipboardPicture
    {
        public byte[] Bytes;
        public string Extension;
    }

    internal sealed class CapturePipeline : IDisposable
    {
        readonly Gallery gallery;
        readonly Dispatcher dispatcher;
        readonly string folder;
        readonly CaptureReceipts receipts = new CaptureReceipts();
        readonly Dictionary<string, int> pending = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> processing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        readonly DateTime launched = DateTime.UtcNow;
        // 截图工具在一次截图或标注过程中会反复重写剪贴板：等它安静下来再读，并用内容指纹吸收残余重写。
        static readonly TimeSpan ClipboardRepeat = TimeSpan.FromSeconds(3);
        // 剪贴板所有者是截图工具时，与绳上那张同尺寸、画面又相近的新版本只暂存不入库；
        // 剪贴板安静这么久（用户改完、点了复制，截图工具不再自动写入）才落盘，覆盖绳上那张。
        static readonly TimeSpan ClipboardCommit = TimeSpan.FromSeconds(3);
        const double ClipboardEditTolerance = 0.35;
        const int ClipboardSettle = 600;
        CaptureItem clipboardPending;
        // 最近一次真正落盘的剪贴板图片。截图工具退出时还会再写一次剪贴板，而那时它进程已销毁，
        // 剪贴板所有者查不到名字，没法靠进程名识别；用内容指纹兜底：和刚保存的一模一样就不重复收。
        string clipboardLastFingerprint;
        DateTime clipboardLastCommitAt = DateTime.MinValue;
        ImageContent clipboardStaged;
        byte[] clipboardStagedBytes;
        string clipboardStagedExtension;
        DateTime clipboardStagedAt = DateTime.MinValue;
        FileSystemWatcher watcher;
        uint clipboardSequence;
        DateTime clipboardDue = DateTime.MaxValue;
        bool clipboardBusy, disposed;
        Point? clipboardOrigin;
        int ticks;
        public event Action<CaptureItem, Point?> Arrived;
        public event Action<string> Failed;

        public CapturePipeline(Gallery gallery, Dispatcher dispatcher, string folder)
        {
            this.gallery = gallery;
            this.dispatcher = dispatcher;
            this.folder = folder;
            clipboardSequence = WindowsNative.GetClipboardSequenceNumber();
            timer.Tick += Tick;
        }

        public bool CollectClipboard
        {
            get { return gallery.Store.State.CollectClipboard; }
            set
            {
                gallery.Store.State.CollectClipboard = value;
                gallery.Store.Save();
                clipboardSequence = WindowsNative.GetClipboardSequenceNumber();
                clipboardDue = DateTime.MaxValue;
                gallery.Notify();
            }
        }

        public bool AutoHang
        {
            get { return gallery.Store.State.AutoHang; }
            set
            {
                gallery.Store.State.AutoHang = value;
                gallery.Store.Save();
                gallery.Notify();
            }
        }

        public void IgnoreCurrentClipboard()
        {
            clipboardSequence = WindowsNative.GetClipboardSequenceNumber();
            clipboardDue = DateTime.MaxValue;
        }

        public async Task Start()
        {
            AttachWatcher();
            timer.Start();
            var saved = gallery.Store.State.Images.ToArray();
            var collected = saved.Concat(gallery.Store.State.Collection);
            if (Directory.Exists(gallery.Store.Inbox))
                collected = collected.Concat(Directory.EnumerateFiles(gallery.Store.Inbox).Where(FileRules.IsImage));
            // 恢复图片篮子时不触发自动挂图，也不重新计入截图去重记录。
            foreach (string path in collected.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            {
                if (disposed) return;
                if (!File.Exists(path)) continue;
                try
                {
                    ImageContent image = await Task.Run(() => ImageContent.Read(path, false));
                    if (disposed) return;
                    if (saved.Contains(path, StringComparer.OrdinalIgnoreCase)) gallery.Add(path, image);
                    else gallery.Collect(path, image);
                }
                catch (IOException) { }
                catch (NotSupportedException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        void AttachWatcher()
        {
            if (watcher != null || !Directory.Exists(folder)) return;
            Trace("监听截图文件夹 " + folder);
            watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite };
            watcher.Created += delegate(object sender, FileSystemEventArgs args) { Queue(args.FullPath); };
            watcher.Changed += delegate(object sender, FileSystemEventArgs args) { Queue(args.FullPath); };
            watcher.Renamed += delegate(object sender, RenamedEventArgs args) { Queue(args.FullPath); };
            watcher.EnableRaisingEvents = true;
            foreach (string path in Directory.EnumerateFiles(folder))
            {
                if (File.GetCreationTimeUtc(path) < launched) seen.Add(path);
                else Queue(path);
            }
        }

        void Queue(string path)
        {
            if (!FileRules.IsImage(path) || disposed) { Trace("队列跳过 " + Path.GetFileName(path)); return; }
            dispatcher.BeginInvoke(new Action(delegate { if (!disposed && !seen.Contains(path)) { pending[path] = 0; Trace("入队 " + Path.GetFileName(path)); } }));
        }

        public void ClipboardChanged()
        {
            if (CollectClipboard && WindowsNative.GetClipboardSequenceNumber() != clipboardSequence)
            {
                var cursor = WindowsNative.Cursor();
                clipboardOrigin = new Point(cursor.X, cursor.Y);
                clipboardDue = DateTime.UtcNow.AddMilliseconds(ClipboardSettle);
            }
        }

        async void Tick(object sender, EventArgs args)
        {
            AttachWatcher();
            foreach (var candidate in pending.ToArray())
                if (!processing.Contains(candidate.Key)) ProcessFile(candidate.Key);
            if (!clipboardBusy && DateTime.UtcNow >= clipboardDue)
            {
                clipboardDue = DateTime.MaxValue;
                ReadClipboard();
            }
            if (!clipboardBusy && clipboardStaged != null && ClipboardReady())
                await CommitStagedClipboard();
            if (++ticks % 10 == 0)
            {
                gallery.Prune();
                foreach (var item in gallery.Collected.ToArray())
                    if (!processing.Contains(item.Path) && File.GetLastWriteTimeUtc(item.Path) != item.Written) Refresh(item);
            }
        }

        async void ProcessFile(string path)
        {
            processing.Add(path);
            bool complete = await Import(path, CaptureSource.Folder, true);
            processing.Remove(path);
            if (disposed) return;
            if (complete) { seen.Add(path); pending.Remove(path); }
            else if (pending.ContainsKey(path) && ++pending[path] >= 20) pending.Remove(path);
        }

        public async Task<bool> Import(string path, CaptureSource? source, bool announce)
        {
            if (source.HasValue ? gallery.Collected.Any(i => FileRules.SameFile(i.Path, path)) : gallery.Contains(path)) return true;
            string copy = null;
            try
            {
                ImageContent image = await Task.Run(() => ImageContent.Read(path));
                if (disposed) return false;
                if (source.HasValue && !receipts.Accept(image.Fingerprint, source.Value, DateTime.UtcNow)) { Trace("导入被去重拦截 " + Path.GetFileName(path)); return true; }
                if (!source.HasValue && !FileRules.IsInside(path, gallery.Store.Inbox))
                {
                    copy = await Task.Run(() => CopyToInbox(path));
                    if (disposed) return false;
                    path = copy;
                }
                if (source.HasValue && !AutoHang) gallery.Collect(path, image);
                else
                {
                    gallery.Add(path, image);
                    if (announce && Arrived != null)
                    {
                        var cursor = WindowsNative.Cursor();
                        Arrived(gallery.Items[gallery.Items.Count - 1], source.HasValue ? new Point?(new Point(cursor.X, cursor.Y)) : null);
                    }
                }
                return true;
            }
            catch (IOException error) { Trace("导入失败(IO) " + Path.GetFileName(path) + " " + error.Message); return false; }
            catch (NotSupportedException error) { Trace("导入失败(格式) " + Path.GetFileName(path) + " " + error.Message); return false; }
            catch (UnauthorizedAccessException error) { Trace("导入失败(权限) " + Path.GetFileName(path) + " " + error.Message); return false; }
            finally
            {
                if (copy != null && !gallery.Collected.Any(i => FileRules.SameFile(i.Path, copy)) && File.Exists(copy)) File.Delete(copy);
            }
        }

        public string CopyToInbox(string path)
        {
            Directory.CreateDirectory(gallery.Store.Inbox);
            string name = Path.GetFileNameWithoutExtension(path) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + Path.GetExtension(path);
            string copy = Path.Combine(gallery.Store.Inbox, name);
            try { File.Copy(path, copy); }
            catch { if (File.Exists(copy)) File.Delete(copy); throw; }
            return copy;
        }

        public void Hang(CaptureItem item)
        {
            if (disposed || gallery.Contains(item.Path)) return;
            if (!File.Exists(item.Path)) { gallery.Prune(); return; }
            gallery.Add(item.Path, item.Image);
            if (Arrived != null) Arrived(item, null);
        }

        public async Task<string> AcceptCapture(BitmapSource picture)
        {
            byte[] png = await Task.Run(() => ImageContent.EncodeClipboard(picture));
            ImageContent image = await Task.Run(() => ImageContent.Decode(png));
            string path = Path.Combine(gallery.Store.Inbox, "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png");
            Directory.CreateDirectory(gallery.Store.Inbox);
            await Task.Run(() => File.WriteAllBytes(path, png));
            if (disposed) return path;
            receipts.Accept(image.Fingerprint, CaptureSource.Clipboard, DateTime.UtcNow);
            if (AutoHang) { gallery.Add(path, image); if (Arrived != null) Arrived(gallery.Items.Last(), null); }
            else gallery.Collect(path, image);
            var data = new DataObject(); data.SetImage(picture); data.SetData("PNG", new MemoryStream(png)); data.SetData(ClipboardRules.OwnFormat, "1");
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { path });
            await WindowsClipboard.Write(data); IgnoreCurrentClipboard();
            return path;
        }

        async void Refresh(CaptureItem item)
        {
            processing.Add(item.Path);
            try
            {
                ImageContent image = await Task.Run(() => ImageContent.Read(item.Path));
                if (!disposed && gallery.Collected.Contains(item))
                {
                    item.Image = image;
                    item.Written = File.GetLastWriteTimeUtc(item.Path);
                    gallery.Notify();
                }
            }
            catch (IOException) { }
            catch (NotSupportedException) { }
            finally { processing.Remove(item.Path); }
        }

        async void ReadClipboard()
        {
            clipboardBusy = true;
            uint sequence = WindowsNative.GetClipboardSequenceNumber();
            Point? origin = clipboardOrigin;
            try
            {
                if (!CollectClipboard || sequence == clipboardSequence) return;
                clipboardSequence = sequence;
                ClipboardPicture picture = await ReadClipboardImage(sequence);
                if (picture == null || disposed || !CollectClipboard || WindowsNative.GetClipboardSequenceNumber() != sequence) return;
                // 所有者可能查不到（截图工具刚退出时进程已销毁），这里统一成空串，避免后续按名字判断时空引用。
                string owner = WindowsNative.ClipboardOwnerName() ?? string.Empty;
                ImageContent content = await Task.Run(() => ImageContent.Decode(picture.Bytes));
                if (disposed || !CollectClipboard || WindowsNative.GetClipboardSequenceNumber() != sequence) return;
                Trace("收到图 owner=" + owner + " " + content.PixelWidth + "x" + content.PixelHeight + " hash=" + (content.Fingerprint == null ? "null" : content.Fingerprint.Substring(0, 8)) + " 可覆盖=" + (clipboardPending != null));
                if (!receipts.Accept(content.Fingerprint, CaptureSource.Clipboard, DateTime.UtcNow, ClipboardRepeat)) { Trace("→ 被3秒去重拦截"); return; }
                if (content.Fingerprint != null && content.Fingerprint == clipboardLastFingerprint && DateTime.UtcNow - clipboardLastCommitAt < TimeSpan.FromSeconds(60))
                {
                    Trace("→ 与刚保存的内容完全相同（截图工具退出时重写剪贴板），跳过");
                    return;
                }
                bool staged = false;
                try { staged = StageEditedClipboard(picture, content, owner); }
                catch (Exception error) { gallery.Store.Log(error); Trace("→ 暂存判定异常，按新图处理"); }
                if (staged) return;
                if (clipboardStaged != null) await CommitStagedClipboard();
                if (disposed) return;
                Directory.CreateDirectory(gallery.Store.Inbox);
                string path = Path.Combine(gallery.Store.Inbox, "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + picture.Extension);
                await Task.Run(() => File.WriteAllBytes(path, picture.Bytes));
                if (disposed || !CollectClipboard || WindowsNative.GetClipboardSequenceNumber() != sequence) { File.Delete(path); return; }
                CaptureItem added;
                if (!AutoHang) added = gallery.Collect(path, content);
                else
                {
                    gallery.Add(path, content);
                    added = gallery.Items[gallery.Items.Count - 1];
                    if (Arrived != null) Arrived(added, origin);
                }
                clipboardPending = added;
                clipboardLastFingerprint = content.Fingerprint;
                clipboardLastCommitAt = DateTime.UtcNow;
            }
            catch (Exception error)
            {
                gallery.Store.Log(error);
                if (Failed != null) Failed("这张图片暂时无法读取，请重试。");
            }
            finally { clipboardBusy = false; }
        }

        internal static Task<ClipboardPicture> ReadClipboardImage(uint sequence)
        {
            return WindowsClipboard.ReadOnSta(delegate
            {
                IDataObject data = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    if (WindowsNative.GetClipboardSequenceNumber() != sequence) return null;
                    try { data = Clipboard.GetDataObject(); break; }
                    catch (ExternalException) { System.Threading.Thread.Sleep(50); }
                }
                if (data == null || WindowsNative.GetClipboardSequenceNumber() != sequence) return null;
                string source = WindowsNative.ClipboardOwnerName();
                if (WindowsNative.GetClipboardSequenceNumber() != sequence) return null;
                return ReadImage(data, source).GetAwaiter().GetResult();
            });
        }

        internal static async Task<ClipboardPicture> ReadImage(IDataObject data, string source)
        {
            string imageFile;
            if (!ClipboardRules.IsImage(data, source, out imageFile)) return null;
            if (imageFile != null)
            {
                byte[] original = await Task.Run(delegate
                {
                    using (var stream = new FileStream(imageFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        if (stream.Length == 0 || stream.Length > 64L * 1024 * 1024) throw new InvalidDataException("图片文件大小无效");
                        return WindowsClipboard.ReadImageBytes(stream);
                    }
                });
                return new ClipboardPicture { Bytes = original, Extension = Path.GetExtension(imageFile) };
            }
            string extension;
            byte[] bytes = WindowsClipboard.ReadEncodedImage(data, out extension);
            if (bytes == null)
            {
                BitmapSource image = data.GetData(DataFormats.Bitmap) as BitmapSource;
                if (image == null) return null;
                image.Freeze(); bytes = await Task.Run(() => ImageContent.EncodeClipboard(image)); extension = ".png";
            }
            return bytes.Length > 64L * 1024 * 1024 ? null : new ClipboardPicture { Bytes = bytes, Extension = extension };
        }

        // 只有截图工具会这么干：截完图、打开编辑器、每改一笔，都把整张图重新塞进剪贴板。
        // 它的遮罩进程与编辑器进程不是同一个，所以按名字前缀认，不要求前后两次完全一致。
        static bool IsCaptureTool(string name)
        {
            return !string.IsNullOrEmpty(name)
                && (name.IndexOf("Snip", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("ScreenClip", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("ScreenSketch", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // 剪贴板安静够久就落盘。落盘永远是覆盖绳上那张，不新增，所以标注途中停顿触发的提前落盘也无害——
        // 之后的每一笔都会重新暂存、再次覆盖，最终定格的还是最后一版。
        bool ClipboardReady()
        {
            return DateTime.UtcNow - clipboardStagedAt >= ClipboardCommit;
        }

        // 截图工具的编辑器里每改一笔都会重写剪贴板，但从剪贴板分不清是自动写入还是用户点了「复制」。
        // 因此与绳上那张同尺寸、画面又相近的新版本只暂存在内存里，等剪贴板安静下来（用户改完、复制完毕）再落盘覆盖。
        bool StageEditedClipboard(ClipboardPicture picture, ImageContent content, string owner)
        {
            CaptureItem pending = clipboardPending;
            // 你把绳上那张删了（或它被替换掉）时，指针会失效；退回绳上/篮子里最新的那张，别让后续修改无处可落。
            if (pending == null || !File.Exists(pending.Path))
            {
                pending = gallery.Items.LastOrDefault(item => File.Exists(item.Path)) ?? gallery.Collected.LastOrDefault(item => File.Exists(item.Path));
                clipboardPending = pending;
            }
            if (pending == null) { Trace("→ 不暂存：没有可覆盖的上一张"); return false; }
            // 截图工具活着时进程名能查到；退出时最后写一次剪贴板、进程随即销毁，所有者就是空的，同样算它。
            if (!IsCaptureTool(owner) && owner.Length > 0) { Trace("→ 不暂存：所有者不是截图工具"); return false; }
            if (DateTime.UtcNow - clipboardLastCommitAt > TimeSpan.FromMinutes(10)) { Trace("→ 不暂存：距上次保存超过10分钟"); return false; }
            bool sameSize = content.PixelWidth == pending.Image.PixelWidth && content.PixelHeight == pending.Image.PixelHeight;
            double difference;
            if (sameSize) difference = ImageContent.Difference(pending.Image, content);
            else
            {
                // 截图工具编辑器会给画布补空白，尺寸小幅变化仍算同一张图的下一版。
                if (!File.Exists(pending.Path)) { Trace("→ 不暂存：上一张文件已不在"); return false; }
                difference = ImageContent.DifferenceAcrossSizes(pending.Path, pending.Image.PixelWidth, picture.Bytes, content.PixelWidth);
                Trace("→ 尺寸 " + pending.Image.PixelWidth + "x" + pending.Image.PixelHeight + " → " + content.PixelWidth + "x" + content.PixelHeight + "，对齐比较差异 " + difference.ToString("F2"));
            }
            if (difference > ClipboardEditTolerance) { Trace("→ 不暂存：画面差异 " + difference.ToString("F2")); return false; }
            Trace("→ 暂存，等剪贴板安静后覆盖");
            clipboardStaged = content;
            clipboardStagedBytes = picture.Bytes;
            clipboardStagedExtension = picture.Extension;
            clipboardStagedAt = DateTime.UtcNow;
            return true;
        }

        void Trace(string message)
        {
            try
            {
                string file = Path.Combine(Path.GetDirectoryName(gallery.Store.Inbox), "clipboard-trace.log");
                // 诊断日志超过 1MB 直接重写：它只是排查用的时间线，历史价值有限。
                var info = new FileInfo(file);
                if (info.Exists && info.Length > 1024 * 1024) info.Delete();
                File.AppendAllText(file, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch { /* 诊断日志失败不影响收集 */ }
        }

        // 把暂存的修改版本写成文件，顶替绳上（或篮子里）那张原图，位置保持不变。
        async Task CommitStagedClipboard()
        {
            CaptureItem pending = clipboardPending;
            ImageContent staged = clipboardStaged;
            byte[] bytes = clipboardStagedBytes;
            string extension = clipboardStagedExtension;
            clipboardStaged = null; clipboardStagedBytes = null; clipboardStagedExtension = null;
            if (pending == null || staged == null || bytes == null || disposed) return;
            if (!File.Exists(pending.Path)) { clipboardPending = null; return; }
            try
            {
                Directory.CreateDirectory(gallery.Store.Inbox);
                string path = Path.Combine(gallery.Store.Inbox, "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + extension);
                await Task.Run(() => File.WriteAllBytes(path, bytes));
                if (disposed) return;
                string replaced = pending.Path;
                CaptureItem current = gallery.ReplaceImage(pending, path, staged);
                if (current == null) { if (File.Exists(path)) File.Delete(path); return; }
                clipboardPending = current;
                clipboardLastFingerprint = staged.Fingerprint;
                clipboardLastCommitAt = DateTime.UtcNow;
                Trace("→ 已覆盖：新文件 " + Path.GetFileName(path) + "，旧文件已删除");
                if (!FileRules.SameFile(replaced, path))
                {
                    try { File.Delete(replaced); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (Exception error)
            {
                gallery.Store.Log(error);
            }
        }

        public void Dispose()
        {
            disposed = true;
            timer.Stop();
            if (watcher != null) watcher.Dispose();
        }
    }
}
