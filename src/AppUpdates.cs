using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ScreenLine
{
    internal sealed class UpdateRelease
    {
        public string Version, Notes, Url, DownloadUrl, Sha256;
        public long Size;
    }

    internal enum UpdateCheckPhase { Idle, Checking, Current, Available, Failed }
    internal sealed class UpdateCheckResult
    {
        internal UpdateCheckPhase Phase;
        internal string Message;
        internal DateTime CheckedAt;
        internal UpdateRelease Release;
    }

    internal sealed class AppUpdates : IDisposable
    {
        // 只读取正式 Release；联网失败与「没有新版本」分别反馈。
        public const string Repository = "https://github.com/lurrrrrr/PickSnap";
        public const string Endpoint = "https://api.github.com/repos/lurrrrrr/PickSnap/releases/latest";
        public static readonly string CurrentVersion = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .Cast<AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
        internal static readonly string Cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PickSnap", "Updates");
        readonly HttpClient client;
        // 本次请求成功确认到的线上版本号（含「已是最新版」的情况）。失败的检查不会更新它，
        // App 用它记住「上次成功确认」，好在网络失败时说明依据而不是笼统报连接不上。
        public string LastSeenVersion { get; private set; }

        public AppUpdates() : this(new HttpClientHandler()) { }
        internal AppUpdates(HttpMessageHandler handler)
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PickSnap/" + CurrentVersion);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        public async Task<UpdateRelease> CheckAsync(CancellationToken cancellation)
        {
            LastSeenVersion = null;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using (var response = await client.GetAsync(Endpoint, timeout.Token).ConfigureAwait(false))
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new InvalidDataException("暂时没有可用更新，请稍后再试。");
                    if ((int)response.StatusCode == 403 || (int)response.StatusCode == 429)
                    {
                        System.Collections.Generic.IEnumerable<string> remaining;
                        bool limited = (int)response.StatusCode == 429 || response.Headers.TryGetValues("X-RateLimit-Remaining", out remaining) && remaining.Contains("0");
                        throw new InvalidDataException(limited ? "检查过于频繁，请稍后重试，或从发布页下载。" : "暂时无法检查更新，请稍后重试，或从发布页下载。");
                    }
                    if (!response.IsSuccessStatusCode) throw new InvalidDataException("GitHub 返回错误 " + (int)response.StatusCode + "，请稍后再试。");
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    string seen;
                    var found = Parse(body, CurrentVersion, out seen);
                    LastSeenVersion = seen;
                    return found;
                }
            }
        }

        internal static UpdateRelease Parse(string json, string current, out string seenVersion)
        {
            seenVersion = null;
            var release = new JavaScriptSerializer().Deserialize<ReleaseData>(json);
            if (release == null) throw new InvalidDataException("更新信息不完整。");
            if (release.draft || release.prerelease) return null;
            string version = (release.tag_name ?? "").TrimStart('v', 'V');
            if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$")) throw new InvalidDataException("更新版本号无效。");
            // 到这里说明请求成功、且线上确实有一个正式版本：把它记下来，
            // 无论它比本地新还是一样（一样就是「已是最新版」）。
            seenVersion = version;
            if (new Version(version) <= new Version(current)) return null;
            string file = "PickSnap-" + version + "-windows-x64.zip";
            var matches = (release.assets ?? new AssetData[0]).Where(item => item != null && item.name == file).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("新版本的 Windows 安装包缺失或重复，请打开发布页确认。");
            var asset = matches[0];
            string expected = Repository + "/releases/download/" + release.tag_name + "/" + file;
            if (asset.browser_download_url != expected || asset.size <= 0 || asset.size > 256L * 1024 * 1024 || !Regex.IsMatch(asset.digest ?? "", @"^sha256:[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("更新包地址或校验信息无效，请稍后重试。");
            return new UpdateRelease { Version = version, Notes = release.body ?? "", Url = Repository + "/releases/tag/" + release.tag_name,
                DownloadUrl = expected, Size = asset.size, Sha256 = asset.digest.Substring(7) };
        }

        public async Task<string> DownloadAsync(UpdateRelease release, IProgress<int> progress, CancellationToken cancellation)
        {
            var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(5)); cancellation = timeout.Token;
            string work = Path.Combine(Cache, Guid.NewGuid().ToString("N"));
            try
            {
                FileRules.AssertPlainTree(Cache, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                Directory.CreateDirectory(work);
                string archive = Path.Combine(work, "update.zip");
                using (var response = await client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.RequestMessage != null && response.RequestMessage.RequestUri.Scheme != "https")
                        throw new InvalidDataException("更新下载未使用安全连接。");
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = File.Create(archive))
                    {
                        byte[] buffer = new byte[65536]; long total = 0; int length;
                        while ((length = await input.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
                        {
                            total += length;
                            if (total > release.Size) throw new InvalidDataException("更新包大小不符合发布信息。");
                            await output.WriteAsync(buffer, 0, length, cancellation).ConfigureAwait(false);
                            if (progress != null) progress.Report((int)(total * 100 / release.Size));
                        }
                        if (total != release.Size) throw new InvalidDataException("更新包未下载完整，请重试。");
                    }
                }
                await Task.Run(delegate
                {
                    cancellation.ThrowIfCancellationRequested();
                    using (var input = File.OpenRead(archive))
                    using (var hash = SHA256.Create())
                        if (!String.Equals(BitConverter.ToString(hash.ComputeHash(input)).Replace("-", ""), release.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("更新包校验失败，请重新下载。");
                    Extract(archive, Path.Combine(work, "contents"), release.Version);
                    cancellation.ThrowIfCancellationRequested();
                    WriteUpdateScript(Path.Combine(work, "apply-update.ps1"));
                }, cancellation).ConfigureAwait(false);
                return work;
            }
            catch { Discard(work); throw; }
            finally { timeout.Dispose(); }
        }

        internal static void Extract(string archive, string destination, string version)
        {
            string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            FileRules.AssertPlainTree(destination, Path.GetDirectoryName(Path.GetFullPath(destination)));
            using (var zip = ZipFile.OpenRead(archive))
            {
                if (zip.Entries.Count > 10000) throw new InvalidDataException("更新包文件过多。");
                long extractedBytes = 0;
                foreach (var entry in zip.Entries)
                {
                    extractedBytes += entry.Length;
                    if (entry.Length > 128L * 1024 * 1024 || extractedBytes > 512L * 1024 * 1024) throw new InvalidDataException("更新包解压大小超出限制。");
                    string name = entry.FullName.Replace('\\', '/');
                    string file = Path.GetFullPath(Path.Combine(root, name));
                    if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase) || name.Contains(':'))
                        throw new InvalidDataException("更新包包含无效路径。");
                    if (!name.StartsWith("app/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (new[] { "data", "test-data", "preview-data" }.Contains(name.Substring(4).Split('/')[0], StringComparer.OrdinalIgnoreCase))
                        throw new InvalidDataException("更新包不能覆盖用户图片和设置。");
                    if (String.IsNullOrEmpty(entry.Name)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    entry.ExtractToFile(file);
                }
            }
            string executable = Path.Combine(root, "app", "PickSnap.exe");
            if (!File.Exists(executable) || FileVersionInfo.GetVersionInfo(executable).ProductVersion != version
                || !File.Exists(Path.Combine(root, "app", "editor", "index.html")) || !File.Exists(Path.Combine(root, "app", "update.ps1"))
                || !File.Exists(Path.Combine(root, "app", "uninstall.ps1")) || !File.Exists(Path.Combine(root, "app", "maintenance-common.ps1")))
                throw new InvalidDataException("更新包不完整或版本不匹配。");
        }

        public static void Install(string work)
        {
            var start = new ProcessStartInfo("powershell.exe") {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(Path.Combine(work, "apply-update.ps1"))
                    + " -WorkPath " + Quote(work) + " -TargetPath " + Quote(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'))
                    + " -ParentProcessId " + Process.GetCurrentProcess().Id
            };
            using (var process = Process.Start(start)) { if (process == null) throw new IOException("无法启动更新安装程序。"); }
        }

        // 更新脚本优先取安装目录里的 update.ps1；从压缩包内直接运行、文件被杀毒软件隔离等
        // 场景下该文件可能缺失，此时改用编译进 exe 的内嵌副本，更新不再因此中断。
        public static void WriteUpdateScript(string destination)
        {
            string onDisk = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.ps1");
            if (File.Exists(onDisk)) { File.Copy(onDisk, destination); return; }
            using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("PickSnap.UpdateScript"))
            {
                if (input == null) throw new IOException("找不到更新脚本，请重新下载完整安装包。");
                using (var output = File.Create(destination)) input.CopyTo(output);
            }
        }

        // 从压缩软件的临时解压目录（%TEMP% 下，例如 360 压缩的 360zip$Temp）直接运行时，
        // 更新装进去关机即失，更新窗口据此直接给出「先解压再安装」的指引，而不是让人下载完再报错。
        public static bool RunsFromTemporaryFolder()
        {
            try { return AppDomain.CurrentDomain.BaseDirectory.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        public static void Discard(string work)
        {
            string path = Path.GetFullPath(work);
            if (!path.StartsWith(Path.GetFullPath(Cache) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            try { if (Directory.Exists(path)) { FileRules.AssertPlainTree(path, Cache); Directory.Delete(path, true); } }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // 下载缓存只在一次更新过程中有用：启动时顺手清掉半天前的残留（下载中断、安装失败留下的半成品），
        // 既不会占着几十兆空间，也不会和正在进行的更新抢文件。
        public static void ClearStaleCache()
        {
            try
            {
                if (!Directory.Exists(Cache)) return;
                FileRules.AssertPlainTree(Cache, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                foreach (var directory in Directory.GetDirectories(Cache))
                {
                    try
                    {
                        if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(directory) < TimeSpan.FromHours(12)) continue;
                        FileRules.AssertPlainTree(directory, Cache); Directory.Delete(directory, true);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static string Quote(string value) { return "\"" + value + "\""; }
        public void Dispose() { client.Dispose(); }

        public sealed class ReleaseData
        {
            public string tag_name { get; set; }
            public string body { get; set; }
            public bool draft { get; set; }
            public bool prerelease { get; set; }
            public AssetData[] assets { get; set; }
        }
        public sealed class AssetData
        {
            public string name { get; set; }
            public string browser_download_url { get; set; }
            public string digest { get; set; }
            public long size { get; set; }
        }
    }
}
