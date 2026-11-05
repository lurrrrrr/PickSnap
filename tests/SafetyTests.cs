using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ScreenLine
{
    internal static class SafetyTests
    {
        static int failures;

        [STAThread]
        static int Main()
        {
            Run("Clipboard annotation preserves watched source", () => ReplaceClipboard(false));
            Run("Clipboard annotation retires owned copy", () => ReplaceClipboard(true));
            Run("Failed text save restores all 100 records", TextSaveFailure);
            Run("Successful text save retains latest 100 records", TextSaveSuccess);
            Run("Seven-day cleanup recycles old managed images and preserves external files", PurgeImages);
            Console.WriteLine(failures == 0 ? "All 5 safety checks passed." : failures + " safety check(s) failed.");
            return failures == 0 ? 0 : 1;
        }

        static void Run(string name, Action test)
        {
            try { test(); Console.WriteLine("PASS: " + name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL: " + name + " - " + error.Message); }
        }

        static void InTemporaryRoot(Action<string> test)
        {
            string root = Path.Combine(Path.GetTempPath(), "PickSnap-safety-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { test(root); }
            finally
            {
                if (!FileRules.IsInside(root, Path.GetTempPath())) throw new InvalidOperationException("Unsafe test cleanup path");
                Directory.Delete(root, true);
            }
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static byte[] Picture(bool edited)
        {
            byte[] pixels = Enumerable.Repeat((byte)255, 48 * 48 * 4).ToArray();
            if (edited) for (int x = 0; x < 8; x++) { pixels[x * 4] = 0; pixels[x * 4 + 1] = 0; }
            var bitmap = BitmapSource.Create(48, 48, 96, 96, PixelFormats.Bgra32, null, pixels, 48 * 4);
            return ImageContent.EncodeClipboard(bitmap);
        }

        static void PurgeImages()
        {
            InTemporaryRoot(root => {
                var store = new StateStore(Path.Combine(root, "data")); Directory.CreateDirectory(store.Inbox); var gallery = new Gallery(store);
                string old = Path.Combine(store.Inbox, "old.png"), fresh = Path.Combine(store.Inbox, "fresh.png"), external = Path.Combine(root, "legacy-original.png");
                foreach (string file in new[] { old, fresh, external }) { File.WriteAllBytes(file, Picture(false)); gallery.Add(file, ImageContent.Read(file)); }
                File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-8)); File.SetLastWriteTimeUtc(external, DateTime.UtcNow.AddDays(-8));
                Check(gallery.PurgeOlderThan(7) == 2, "expired records were not cleaned");
                Check(!File.Exists(old) && File.Exists(fresh) && File.Exists(external) && gallery.Collected.Count == 1, "cleanup deleted a fresh or external image");
            });
        }

        static void ReplaceClipboard(bool owned)
        {
            InTemporaryRoot(root =>
            {
                var store = new StateStore(Path.Combine(root, "data"));
                store.State.CollectClipboard = true;
                var gallery = new Gallery(store);
                string folder = Path.Combine(root, "Screenshots");
                Directory.CreateDirectory(folder); Directory.CreateDirectory(store.Inbox);
                string original = Path.Combine(owned ? store.Inbox : folder, "original.png");
                byte[] before = Picture(false), after = Picture(true);
                File.WriteAllBytes(original, before);
                gallery.Add(original, ImageContent.Read(original));
                using (var pipeline = new CapturePipeline(gallery, Dispatcher.CurrentDispatcher, folder))
                {
                    var picture = new ClipboardPicture { Bytes = after, Extension = ".png" };
                    var stage = typeof(CapturePipeline).GetMethod("StageEditedClipboard", BindingFlags.Instance | BindingFlags.NonPublic);
                    Check((bool)stage.Invoke(pipeline, new object[] { picture, ImageContent.Decode(after), "SnippingTool" }), "annotation did not stage");
                    var commit = typeof(CapturePipeline).GetMethod("CommitStagedClipboard", BindingFlags.Instance | BindingFlags.NonPublic);
                    ((Task)commit.Invoke(pipeline, null)).GetAwaiter().GetResult();
                    Check(gallery.Collected.Count == 1, "replacement changed record count");
                    string replacement = gallery.Collected.Single().Path;
                    Check(FileRules.IsInside(replacement, store.Inbox), "replacement is outside owned inbox");
                    Check(File.ReadAllBytes(replacement).SequenceEqual(after), "edited bytes were not saved");
                    Check(new StateStore(store.Root).State.Collection.Single() == replacement, "replacement was not persisted");
                    if (owned) Check(!File.Exists(original), "obsolete owned copy was not retired");
                    else Check(File.Exists(original) && File.ReadAllBytes(original).SequenceEqual(before), "external source was deleted or altered");
                }
            });
        }

        static TextCollection SeedTexts(string root)
        {
            var store = new StateStore(root);
            for (int i = 0; i < 100; i++) store.State.Texts.Add(new TextItem { Id = i.ToString(), Content = "record " + i, CreatedUtc = DateTime.UtcNow.AddMinutes(-i) });
            store.Save();
            return new TextCollection(store);
        }

        static void TextSaveFailure()
        {
            InTemporaryRoot(root =>
            {
                var texts = SeedTexts(root);
                var before = texts.Items.ToArray(); int notifications = 0;
                texts.Changed += () => notifications++;
                Directory.CreateDirectory(Path.Combine(root, "state.json.tmp"));
                bool failed = false;
                try { texts.Add("new record"); } catch (UnauthorizedAccessException) { failed = true; }
                Check(failed, "test did not induce a write failure");
                Check(texts.Items.SequenceEqual(before), "failed save lost or reordered previous records");
                Check(notifications == 0, "failed save emitted a success notification");
                Check(new StateStore(root).State.Texts.Select(i => i.Id).SequenceEqual(before.Select(i => i.Id)), "failed save changed persisted records");
            });
        }

        static void TextSaveSuccess()
        {
            InTemporaryRoot(root =>
            {
                var texts = SeedTexts(root);
                texts.Add("new record");
                Check(texts.Items.Count == 100 && texts.Items[0].Content == "new record", "latest record/cap was not retained");
                Check(!texts.Items.Any(i => i.Id == "99"), "oldest record was not trimmed");
                Check(new StateStore(root).State.Texts.Count == 100, "text cap was not persisted");
            });
        }
    }
}
