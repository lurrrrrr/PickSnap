using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ScreenLine
{
    internal static class FeatureTests
    {
        static int failures, count;
        static bool render;
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--verify-package")
            {
                try { Temporary(root => AppUpdates.Extract(args[1], Path.Combine(root, "package"), AppUpdates.CurrentVersion)); Console.WriteLine("PASS: Actual release archive passes the application's updater validation"); return 0; }
                catch (Exception error) { Console.WriteLine(error); return 1; }
            }
            if (args.Length == 4 && args[0] == "--verify-screenshot-pair") {
                try {
                    byte[] original = File.ReadAllBytes(args[1]), edited = File.ReadAllBytes(args[2]), corrupt = File.ReadAllBytes(args[3]);
                    ClipboardTransfer.ValidateEncoded(original, ".png"); ClipboardTransfer.ValidateEncoded(edited, ".png");
                    bool rejected = false; try { ClipboardTransfer.ValidateEncoded(corrupt, ".png"); } catch (InvalidDataException) { rejected = true; }
                    Check(rejected, "damaged sample accepted");
                    var left = ImageContent.Decode(original); var right = ImageContent.Decode(edited);
                    double difference = ImageContent.DifferenceAcrossSizes(args[1], left.PixelWidth, edited, right.PixelWidth);
                    Check(difference <= 0.35, "valid edit does not match original: " + difference);
                    Console.WriteLine("PASS: Damaged sample rejected; valid edit difference=" + difference); return 0;
                } catch (Exception error) { Console.WriteLine(error); return 1; }
            }
            render = args.Contains("--render");
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            LightTheme.UseDark(args.Contains("--dark")); LightTheme.Install(app);
            if (args.Contains("--clipboard-only")) {
                Run("Unsupported encoded medium falls back to bitmap", ClipboardMediumFallback);
                Run("Ordinary clipboard formats still work", ClipboardFormats);
                Run("Screenshot edits replace the correct card", ScreenshotEditTarget);
                app.Shutdown(); Console.WriteLine(count + " clipboard checks: " + (count - failures) + " passed, " + failures + " failed."); return failures == 0 ? 0 : 1;
            }
            Run("Unsupported encoded medium falls back to bitmap", ClipboardMediumFallback);
            if (args.Contains("--demos")) { DemoRecorder.Run(); app.Shutdown(); return 0; }
            if (args.Contains("--ui-only"))
            {
                Run("Settings button alignment", SettingsPages);
                Run("Menu dismissal timing", MenuDismissalTiming);
                Run("Tray and basket layout", MenuAndBasketLayout);
                Run("Text selection layout", TextHistory);
                Run("Button bounds and centering", ButtonBounds);
                Run("Button contrast", ButtonContrast);
                Run("Uninstall layout", UninstallChoice);
                app.Shutdown(); Console.WriteLine(count + " UI checks: " + (count - failures) + " passed, " + failures + " failed."); return failures == 0 ? 0 : 1;
            }
            Run("Menu dismissal timing", MenuDismissalTiming);
            Run("Menu submenu actions close cleanly and only execute once", MenuLifecycle);
            Run("Corrupt state restores the previous backup", StateRecovery);
            Run("Moving managed data retains rope positions", Relocation);
            Run("Gallery removal rolls back when saving fails", GalleryRollback);
            Run("QQ image files are accepted without collecting Office text", ClipboardFormats);
            Run("Top activation zones respect negative monitor coordinates", RevealZones);
            Run("Hotkey conflict retains the previous binding", Hotkeys);
            Run("Failed shortcut persistence keeps the previous system registration", HotkeyRollback);
            Run("404 release response is not reported as up-to-date", MissingRelease);
            Run("Update archives reject traversal and user data", Archives);
            Run("Uninstall defaults to retaining data and requires deletion acknowledgement", UninstallChoice);
            Run("Settings navigation, button sizing and saved shortcuts agree", SettingsPages);
            Run("Switches initialize without animation and animate only user toggles", SwitchInitialization);
            Run("Lightweight basket selection survives incremental updates", BasketUpdates);
            Run("Primary and destructive button labels retain readable contrast", ButtonContrast);
            Run("Replacement preserves rope position metadata", ReplacementMetadata);
            Run("A complete update package is accepted and missing maintenance files rejected", CompleteArchive);
            Run("A thousand-image basket reuses unchanged image controls", LargeBasket);
            Run("Long text opens at the beginning and can scroll", ReaderPosition);
            Run("Tray rows and basket selection remain readable at scaled layout", MenuAndBasketLayout);
            Run("Buttons retain complete text after labels and DPI change", ButtonBounds);
            Run("Manual update checks share pending work and retain inline results", SharedUpdateCheck);
            Run("Update failures and available releases have distinct inline states", UpdateStates);
            Run("Confirmed updates install automatically after download", DownloadAutoInstall);
            Run("Manual imports hang while automatic collection is disabled", ManualImport);
            Run("Image collection covers both sources and old pause migrates", CollectionSwitches);
            Run("System screenshot imports use managed copies and automatically hang", ScreenshotCopies);
            Run("Basket capacity follows pending selections and prevents overfilling", BasketCapacity);
            Run("Feedback: text deletion supports one, batch and redo without reordering", TextHistory);
            Run("Text rows toggle marks, isolate action buttons and open on double click", TextRowInteraction);
            Run("Feedback: screenshot folder arrival updates the annotation target", ScreenshotEditTarget);
            Run("Feedback: minimized settings restores the existing window", RestoreSettings);
            Run("Feedback: collection switches hide accessories without losing content", AccessorySwitches);
            Run("Feedback: defaults persist and basket cleanup uses the same setting", DefaultsAndCleanup);
            Run("Feedback: common shortcut warnings and focus styles are consistent", ShortcutAndFocus);
            Run("Feedback: capture overlays cover every connected display and cancel together", CaptureDisplays);
            app.Shutdown();
            Console.WriteLine(count + " feature checks: " + (count - failures) + " passed, " + failures + " failed.");
            return failures == 0 ? 0 : 1;
        }
        static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        static void Run(string name, Action action)
        {
            count++;
            try { action(); Console.WriteLine("PASS: " + name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL: " + name + " - " + error); }
            finally { foreach (Window window in Application.Current.Windows.Cast<Window>().ToArray()) window.Close(); }
        }
        static void ReaderPosition()
        {
            Temporary(root => {
                var window = new TextReaderWindow(new TextItem { Content = string.Join("\n", Enumerable.Range(1, 100).Select(i => "Line " + i)) }, new StateStore(root));
                window.Show(); window.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);
                Check(window.FullText.VerticalOffset == 0 && window.FullText.CaretIndex == 0, "reader did not open at the beginning");
                window.FullText.ScrollToEnd(); Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);
                Check(window.FullText.VerticalOffset > 0, "reader could not scroll to the end"); window.Close();
            });
        }
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Pump(int milliseconds)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds) { Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background); Thread.Sleep(5); }
        }
        static void TextHistory()
        {
            Temporary(root => {
                var texts = new TextCollection(new StateStore(root));
                foreach (string value in new[] { "E", "D", "C", "B", "A" }) texts.Add(value);
                var original = texts.Items.ToArray(); var history = new TextEditHistory(texts);
                history.Delete(new[] { original[1], original[3] });
                Check(history.Undo(false) == 1 && string.Join("", texts.Items.Select(t => t.Content)) == "ABCE", "single undo restored the batch or changed order");
                Check(history.Redo() == 1 && string.Join("", texts.Items.Select(t => t.Content)) == "ACE", "redo did not remove the restored item");
                texts.Add("N"); Check(history.Undo(true) == 2 && string.Join("", texts.Items.Select(t => t.Content)) == "NABCDE", "batch recovery lost new content or original ordering");
                Check(history.Redo() == 2 && string.Join("", texts.Items.Select(t => t.Content)) == "NACE", "batch redo failed");
                Directory.CreateDirectory(Path.Combine(root, "state.json.tmp"));
                Reject(() => history.Undo(true), "save failure accepted"); Check(history.PendingCount == 2 && string.Join("", texts.Items.Select(t => t.Content)) == "NACE", "failed undo consumed history");
                Directory.Delete(Path.Combine(root, "state.json.tmp")); history.Undo(true);
                var shelf = new ShelfWindow(new Gallery(texts.Store), delegate { }, delegate { }); shelf.Show();
                var panel = new TextWindow(texts, shelf); panel.Show(); Pump(200); panel.UpdateLayout();
                int beforeDelete = texts.Items.Count; texts.Items[1].IsMarked = true; texts.Items[3].IsMarked = true;
                panel.DeleteSelectedButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                panel.DeleteSelectedButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(texts.Items.Count == beforeDelete - 2, "panel batch delete failed");
                var undo = Descendants(panel).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "撤销一条删除");
                undo.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(texts.Items.Count == beforeDelete - 1, "panel undo restored the whole batch");
                Snapshot(panel, "text-history"); panel.Close(); shelf.Close();
            });
        }
        static void TextRowInteraction()
        {
            Temporary(root => {
                var texts = new TextCollection(new StateStore(root)); var item = texts.Add("全文阅读测试");
                var shelf = new ShelfWindow(new Gallery(texts.Store), delegate { }, delegate { }); shelf.Show();
                var panel = new TextWindow(texts, shelf); panel.Show(); panel.UpdateLayout(); Pump(60);
                var row = (ListBoxItem)panel.List.ItemContainerGenerator.ContainerFromIndex(0);
                var preview = Descendants(row).OfType<TextBlock>().First(t => t.Text == item.Content);
                Check(panel.RowFromClick(preview) == item && panel.RowFromClick(row) == item, "row text or blank area is not clickable");
                foreach (var button in Descendants(row).OfType<System.Windows.Controls.Primitives.ButtonBase>())
                    Check(panel.RowFromClick(button) == null, "row intercepts action or checkbox clicks");
                panel.HandleRowClick(item, 1); Check(item.IsMarked && panel.DeleteSelectedButton.IsEnabled, "single click failed to select");
                panel.HandleRowClick(item, 1); Check(!item.IsMarked && !panel.DeleteSelectedButton.IsEnabled, "second single click failed to deselect");
                Check(!panel.IsTruncatedPreview(preview), "short text was treated as truncated");
                Check(!Descendants(row).OfType<Button>().Any(b => (string)b.Tag == "read"), "read button remains");
                panel.HandleRowClick(item, 1, false, 1000); panel.HandleRowClick(item, 2, false, 1100);
                Check(!item.IsMarked && panel.IsVisible, "fast selection on short text opened reader");
                panel.HandleRowClick(item, 1, true, 2000); panel.HandleRowClick(item, 2, true, 2350);
                Check(!item.IsMarked && panel.IsVisible, "slow pair opened reader");
                item.Content = new string('长', 300); panel.Search.Text = "长"; panel.UpdateLayout(); Pump(50);
                row = (ListBoxItem)panel.List.ItemContainerGenerator.ContainerFromIndex(0);
                preview = Descendants(row).OfType<TextBlock>().First(t => t.Name == "Preview");
                Check(panel.IsTruncatedPreview(preview), "long text was not detected as truncated");
                panel.HandleRowClick(item, 1, true, 3000); panel.HandleRowClick(item, 2, true, 3100);
                Check(item.IsMarked && !panel.IsVisible && Application.Current.Windows.OfType<TextReaderWindow>().Count() == 1, "double click toggled twice or failed to open full text");
                foreach (var reader in Application.Current.Windows.OfType<TextReaderWindow>().ToArray()) reader.Close(); shelf.Close();
            });
        }
        sealed class BrokenPngData : IDataObject
        {
            internal bool Alternative;
            internal bool Corrupt;
            public object GetData(string format, bool convert) {
                if (format == "PNG" && Corrupt) { byte[] bytes = Png(); bytes[29] ^= 1; return bytes; }
                if (format == "PNG") throw new System.Runtime.InteropServices.COMException("invalid tymed", unchecked((int)0x800401D3));
                if (format == "image/png" && Alternative) return new MemoryStream(Png());
                if (format == DataFormats.Bitmap) return ImageContent.Decode(Png()).Thumbnail;
                return null;
            }
            public object GetData(string format) { return GetData(format, true); }
            public object GetData(Type type) { return null; }
            public bool GetDataPresent(string format, bool convert) { return GetFormats().Contains(format); }
            public bool GetDataPresent(string format) { return GetDataPresent(format, true); }
            public bool GetDataPresent(Type type) { return false; }
            public string[] GetFormats() { return Alternative ? new[] { "PNG", "image/png", DataFormats.Bitmap } : new[] { "PNG", DataFormats.Bitmap }; }
            public string[] GetFormats(bool convert) { return GetFormats(); }
            public void SetData(string format, object data, bool convert) { throw new NotSupportedException(); }
            public void SetData(string format, object data) { throw new NotSupportedException(); }
            public void SetData(Type type, object data) { throw new NotSupportedException(); }
            public void SetData(object data) { throw new NotSupportedException(); }
        }
        [System.Runtime.InteropServices.DllImport("ole32.dll")]
        static extern int CreateStreamOnHGlobal(IntPtr memory, bool delete, out System.Runtime.InteropServices.ComTypes.IStream stream);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr GlobalLock(IntPtr memory);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool GlobalUnlock(IntPtr memory);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr GlobalFree(IntPtr memory);
        static void NativeClipboardMedia()
        {
            byte[] expected = Png();
            IntPtr memory = GlobalAlloc(2, new UIntPtr((uint)expected.Length));
            Check(memory != IntPtr.Zero, "allocation failed");
            try {
                IntPtr address = GlobalLock(memory);
                System.Runtime.InteropServices.Marshal.Copy(expected, 0, address, expected.Length); GlobalUnlock(memory);
                var medium = new System.Runtime.InteropServices.ComTypes.STGMEDIUM {
                    tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL, unionmember = memory };
                Check(ClipboardTransfer.ReadMedium(medium).Take(expected.Length).SequenceEqual(expected), "HGLOBAL image corrupted");
            } finally { GlobalFree(memory); }
            System.Runtime.InteropServices.ComTypes.IStream stream;
            System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(CreateStreamOnHGlobal(IntPtr.Zero, true, out stream));
            IntPtr pointer = IntPtr.Zero;
            try {
                stream.Write(expected, expected.Length, IntPtr.Zero);
                pointer = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(stream);
                var medium = new System.Runtime.InteropServices.ComTypes.STGMEDIUM {
                    tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_ISTREAM, unionmember = pointer };
                Check(ClipboardTransfer.ReadMedium(medium).SequenceEqual(expected), "IStream image corrupted or not rewound");
            } finally {
                if (pointer != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(pointer);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(stream);
            }
        }
        static void ClipboardMediumFallback()
        {
            NativeClipboardMedia();
            byte[] valid = Png(); ClipboardTransfer.ValidateEncoded(valid, ".png");
            byte[] invalid = (byte[])valid.Clone(); invalid[29] ^= 1;
            bool rejected = false; try { ClipboardTransfer.ValidateEncoded(invalid, ".png"); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "invalid PNG checksum accepted");
            rejected = false; try { ClipboardTransfer.ValidateEncoded(valid.Take(valid.Length - 8).ToArray(), ".png"); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "truncated PNG accepted");
            Check(CapturePipeline.ResolveCaptureSource("svchost", "ScreenSketch") == "ScreenSketch", "local editor evidence was lost");
            Check(CapturePipeline.ResolveCaptureSource("svchost", null) == "svchost", "remote or unknown broker must not be trusted");
            Check(CapturePipeline.ResolveCaptureSource("Paint", "ScreenSketch") == "Paint", "unrelated owner was relabeled");
            foreach (bool corrupt in new[] { false, true })
            foreach (bool alternative in new[] { false, true }) {
                int errors = 0;
                var task = CapturePipeline.ReadImage(new BrokenPngData { Alternative = alternative, Corrupt = corrupt }, "ScreenSketch", error => errors++); Await(task);
                Check(task.Result != null && task.Result.Extension == ".png" && errors == 1, "bad medium aborted instead of falling back or was not diagnosed");
                Check(ImageContent.Decode(task.Result.Bytes).PixelWidth > 0, "fallback produced invalid image");
            }
        }

        static byte[] EditedPng()
        {
            byte[] pixels = Enumerable.Repeat((byte)255, 64 * 40 * 4).ToArray();
            for (int x = 0; x < 12; x++) { pixels[x * 4] = 0; pixels[x * 4 + 1] = 0; }
            return ImageContent.EncodeClipboard(BitmapSource.Create(64, 40, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4));
        }
        static void ScreenshotEditTarget()
        {
            Temporary(root => {
                var store = new StateStore(Path.Combine(root, "data")); var gallery = new Gallery(store);
                var older = Image(gallery, "older", true);
                using (var captures = new CapturePipeline(gallery, Dispatcher.CurrentDispatcher, root))
                {
                    captures.LinkClipboardCandidate(older.Image.Fingerprint);
                    string file = Path.Combine(root, "PrtSc.png"); File.WriteAllBytes(file, Png()); Await(captures.Import(file, CaptureSource.Folder, false));
                    var recent = gallery.Items.Last(); byte[] after = EditedPng();
                    Check(captures.StageEditedClipboard(new ClipboardPicture { Bytes = after, Extension = ".png" }, ImageContent.Decode(after), "SnippingTool"), "PrtSc edit did not attach");
                    Await(captures.CommitStagedClipboard());
                    Check(gallery.Items.Count == 2 && gallery.Items[0] == older && gallery.Items[1] != recent, "editing replaced the previous screenshot or added a third card");
                    Check(File.ReadAllBytes(gallery.Items[1].Path).SequenceEqual(after) && File.Exists(file), "edited image or original source incorrect");
                    Check(!captures.StageEditedClipboard(new ClipboardPicture { Bytes = after, Extension = ".png" }, ImageContent.Decode(after), "Paint"), "unrelated image application replaced a screenshot");
                    Check(captures.StageEditedClipboard(new ClipboardPicture { Bytes = Png(), Extension = ".png" }, ImageContent.Decode(Png()), "ScreenClippingHost"), "Win+Shift+S edits use a different route");
                    Await(captures.CommitStagedClipboard()); Check(gallery.Items.Count == 2, "second edit duplicated the card");
                }
            });
        }
        static void RestoreSettings()
        {
            Temporary(root => { using (var controller = new DesktopController(new StateStore(root), root, false)) {
                controller.ShowSettings("更新与卸载"); var window = controller.Settings; window.WindowState = WindowState.Minimized; Pump(50);
                controller.ShowSettings(); Check(controller.Settings == window && window.WindowState == WindowState.Normal && window.CurrentPage == "更新与卸载", "settings did not restore in place");
                window.SelectPage("常规与外观"); window.UpdateLayout(); Pump(100);
                var combo = Descendants(window).OfType<ComboBox>().First(); combo.IsDropDownOpen = true; Pump(50);
                Check(combo.IsDropDownOpen, "test dropdown did not open");
                window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
                Check(window.IsVisible && !combo.IsDropDownOpen, "Escape state: visible=" + window.IsVisible + ", dropdown=" + combo.IsDropDownOpen);
            }});
        }
        static void AccessorySwitches()
        {
            Temporary(root => { using (var controller = new DesktopController(new StateStore(root), root, false)) {
                controller.Shelf.Show(); Image(controller.Gallery, "pending"); controller.Texts.Add("retained text");
                controller.SetClipboardImages(false); controller.SetClipboardText(false); controller.Shelf.Reveal(true, true, 0); controller.Shelf.UpdateLayout();
                Check(!controller.Shelf.BasketButton.IsVisible && !controller.Shelf.TextButton.IsVisible, "disabled accessory is still hanging");
                Check(controller.Gallery.Collected.Count == 1 && controller.Texts.Items.Count == 1, "disabling collection removed saved content");
                controller.ShowTexts(); Check(controller.TextWindow != null, "saved text cannot be opened from tray"); controller.TextWindow.Close();
                controller.Texts.RemoveUndoable(controller.Texts.Items.ToArray()); controller.SetClipboardText(true); controller.ShowTexts(); Check(controller.TextWindow == null, "empty tray envelope opened a blank panel");
            }});
        }
        static void DefaultsAndCleanup()
        {
            Temporary(root => { var store = new StateStore(root);
                Check(store.State.CollectClipboard && !store.State.CollectTextClipboard && store.State.AutoPurge && store.State.RevealZone == "full" && !store.State.NoHoverReveal && store.State.HoverPreview && store.State.RevealDelayMs == 450, "documented defaults differ");
                using (var controller = new DesktopController(store, root, false)) {
                    controller.Shelf.Show(); controller.ShowBasket(); var basket = controller.Basket; basket.AutoPurgeSwitch.IsChecked = true; basket.AutoPurgeSwitch.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Check(store.State.AutoPurge && new StateStore(root).State.AutoPurge, "basket toggle did not save shared cleanup setting");
                    controller.SetAutoPurge(false); controller.Gallery.Notify(); Pump(100); Check(basket.AutoPurgeSwitch.IsChecked == false, "basket setting did not refresh");
                    store.State.RevealZone = "left"; store.Save(); var saved = new StateStore(root); Check(!saved.State.AutoPurge && saved.State.RevealZone == "left", "new defaults overwrote existing choices");
                    File.WriteAllText(Path.Combine(root, "state.json"), "{}"); var legacy = new StateStore(root); Check(!legacy.State.AutoPurge && legacy.State.RevealZone == "center", "legacy settings unexpectedly enabled cleanup");
                }
            });
        }
        static void ShortcutAndFocus()
        {
            Check(ShortcutWindow.CommonShortcutWarning(2, (uint)'C').Length > 0 && ShortcutWindow.CommonShortcutWarning(3, (uint)'S') == "", "shortcut warning missing or too broad");
            var choice = new ComboBox { Style = LightTheme.ChoiceStyle }; Check(choice.FocusVisualStyle == null, "default dotted combo focus remains");
            var tray = new TrayPanel(); Check(tray.CaptureButton.FocusVisualStyle == null && tray.Width < 310, "tray focus or compact size regressed");
            Check(SettingsWindow.Pages.All(p => p.Length == 5), "navigation names have inconsistent lengths");
        }
        static void CaptureDisplays()
        {
            CaptureWindow active = null; var task = CaptureWindow.PickAcrossScreens(window => active = window); Pump(250);
            var overlays = Application.Current.Windows.OfType<CaptureWindow>().ToArray();
            Check(overlays.Length == WindowsNative.Screens.Length, "not every display has an overlay");
            foreach (var overlay in overlays) Check(overlay.Surface.Picture != null && overlay.IsVisible, "display overlay has no captured image");
            var last = overlays.Last(); last.Surface.Selection = new Rect(3, 4, 25, 18);
            last.Tools["完成"].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Await(task);
            Check(task.Result.PixelWidth == 25 && task.Result.PixelHeight == 18 && active == null && !Application.Current.Windows.OfType<CaptureWindow>().Any(), "selected display export or peer cleanup failed");
            task = CaptureWindow.PickAcrossScreens(window => active = window); active.Close(); Await(task);
            Check(task.Result == null && !Application.Current.Windows.OfType<CaptureWindow>().Any(), "cancel left an overlay running");
        }
        static void Reject(Action action, string message) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, message); }
        static void Temporary(Action<string> test)
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PickSnap-features-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            try { test(root); }
            finally { if (!FileRules.IsInside(root, Path.GetTempPath())) throw new InvalidOperationException("Unsafe cleanup path"); Directory.Delete(root, true); }
        }
        static byte[] Png()
        {
            byte[] pixels = Enumerable.Repeat((byte)255, 64 * 40 * 4).ToArray();
            return ImageContent.EncodeClipboard(BitmapSource.Create(64, 40, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4));
        }
        static CaptureItem Image(Gallery gallery, string name, bool hang = false)
        {
            Directory.CreateDirectory(gallery.Store.Inbox);
            string file = Path.Combine(gallery.Store.Inbox, name); File.WriteAllBytes(file, Png());
            var content = ImageContent.Read(file);
            if (hang) { gallery.Add(file, content); return gallery.Items.Last(); }
            return gallery.Collect(file, content);
        }
        static void StateRecovery()
        {
            Temporary(root => {
                var store = new StateStore(root); store.State.RevealDelayMs = 750; store.Save();
                store.State.RevealDelayMs = 1000; store.Save(); File.WriteAllText(Path.Combine(root, "state.json"), "{broken");
                var recovered = new StateStore(root);
                Check(recovered.State.RevealDelayMs == 750 && !string.IsNullOrEmpty(recovered.RecoveryNotice), "backup was not restored");
                Check(Directory.GetFiles(root, "*.damaged-*").Length == 1, "damaged original was not retained");
                recovered.Save(); Check(new StateStore(root).State.RevealDelayMs == 750, "recovered state could not be saved");
                File.WriteAllText(Path.Combine(root, "state.json"), "{\"Texts\":[{\"Content\":\"retained\",\"CreatedUtc\":\"invalid-date\"}]}");
                recovered = new StateStore(root);
                Check(recovered.State.RevealDelayMs == 750 && !string.IsNullOrEmpty(recovered.RecoveryNotice), "invalid date escaped configuration recovery");
                recovered.State.Texts.Add(new TextItem { Id = "test", Content = "retained", CreatedUtc = DateTime.UtcNow }); recovered.Save();
                Check(new StateStore(root).State.Texts.Single().Content == "retained", "text date could not roundtrip");
            });
        }
        static void Relocation()
        {
            Temporary(root => {
                string old = Path.Combine(root, "old-data"), moved = Path.Combine(root, "new-data");
                var store = new StateStore(old); var gallery = new Gallery(store); var item = Image(gallery, "retained.png", true);
                store.State.CardOffsets[item.Path] = 245; store.Save();
                Check(FileRules.IsInside(old, root) && FileRules.IsInside(moved, root), "unsafe move fixture"); Directory.Move(old, moved);
                var restored = new StateStore(moved); string file = Path.Combine(moved, "Captures", "retained.png");
                Check(restored.State.Images.Single() == file && restored.State.CardOffsets[file] == 245, "managed paths did not migrate");
            });
        }
        static void GalleryRollback()
        {
            Temporary(root => {
                var g = new Gallery(new StateStore(root)); var item = Image(g, "keep.png", true);
                Directory.CreateDirectory(Path.Combine(root, "state.json.tmp"));
                Reject(() => g.Clear(), "fixture did not block saving"); Check(g.Items.Single() == item, "clear did not roll back");
                Reject(() => g.Remove(item), "fixture did not block removal"); Check(g.Items.Single() == item, "remove did not roll back");
            });
        }
        static void ClipboardFormats()
        {
            var image = new DataObject(); image.SetData(DataFormats.FileDrop, new[] { "C:\\sample.png" }); image.SetData("PNG", new byte[] { 1, 2, 3 });
            string path; Check(ClipboardRules.IsImage(image, "QQ", out path) && path == "C:\\sample.png", "QQ file image was rejected");
            Check(!ClipboardRules.IsImage(image, "WINWORD"), "unknown mixed file source accepted");
            image.SetText(""); Check(ClipboardRules.IsImage(image, "QQ"), "empty text metadata rejected");
            image.SetText("C:\\sample.png"); Check(ClipboardRules.IsImage(image, "QQ"), "image-path text metadata rejected");
            image.SetText("selected text"); Check(!ClipboardRules.IsImage(image, "QQ"), "mixed text collected as a screenshot");
        }
        static void RevealZones()
        {
            var screen = new System.Drawing.Rectangle(-1920, -1080, 1920, 1080);
            Check(RevealPolicy.Contains(screen, -960, "center") && !RevealPolicy.Contains(screen, -20, "center"), "center activation overlaps caption buttons");
            Check(RevealPolicy.Contains(screen, -1800, "full") && !RevealPolicy.Contains(screen, 0, "full"), "monitor boundary is incorrect");
            Check(RevealPolicy.Contains(screen, -1600, "left") && !RevealPolicy.Contains(screen, -400, "left"), "left activation range is incorrect");
            Check(RevealPolicy.NearTop(screen, screen, -960, -1074, 1.5) && !RevealPolicy.NearTop(screen, screen, -960, -1060, 1.5), "DPI-scaled top strip is inaccessible or too broad");
            Check(!RevealPolicy.CoversScreen(screen, screen, true, true) && RevealPolicy.CoversScreen(screen, screen, false, false), "ordinary maximized window was treated as fullscreen");
            var dwell = new RevealDwell(); var now = DateTime.UtcNow;
            Check(!dwell.Ready(true, "screen:center", now, 450) && !dwell.Ready(true, "screen:center", now.AddMilliseconds(449), 450) && dwell.Ready(true, "screen:center", now.AddMilliseconds(450), 450), "hover delay never completed");
            Check(!dwell.Ready(true, "other:left", now.AddSeconds(1), 450), "switching display reused old hover time");
            Check(!dwell.Ready(false, "other:left", now.AddSeconds(2), 450) && !dwell.Ready(true, "other:left", now.AddSeconds(3), 450), "leaving the trigger region did not reset dwell");

        }
        static void Hotkeys()
        {
            using (var events = new WindowsEvents(false))
            {
                uint key = 0; for (uint next = 0x7C; next <= 0x87; next++) if (events.RegisterToggle(7, next)) { key = next; break; }
                Check(key != 0, "no test hotkey available");
                Check(!events.RegisterCapture(7, key), "conflicting shortcut replaced the previous binding");
                Check(events.RegisterToggle(7, key), "old shortcut is no longer registered");
                Check(!events.RegisterToggle(0, key) && events.RegisterToggle(7, key), "invalid shortcut discarded the previous binding");
            }
        }
        static void HotkeyRollback()
        {
            using (var events = new WindowsEvents(false))
            {
                uint old = 0, next = 0;
                for (uint key = 0x7C; key <= 0x87; key++) if (events.RegisterToggle(7, key)) { old = key; break; }
                using (var probe = new WindowsEvents(false)) { for (uint key = 0x7C; key <= 0x87; key++) if (key != old && probe.RegisterCapture(7, key)) { next = key; break; } }
                Check(old != 0 && next != 0, "no test shortcuts available");
                Reject(() => events.RegisterToggle(7, next, () => { throw new IOException("fixture save failure"); }), "save failure was swallowed");
                using (var verifier = new WindowsEvents(false))
                {
                    Check(!verifier.RegisterCapture(7, old), "old registration was released before saving succeeded");
                    Check(verifier.RegisterCapture(7, next), "failed new registration was not released");
                }
            }
        }
        sealed class MissingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); }
        }
        static void MissingRelease()
        {
            using (var updates = new AppUpdates(new MissingHandler())) Reject(() => updates.CheckAsync(CancellationToken.None).GetAwaiter().GetResult(), "404 was treated as current version");
        }
        static void Archives()
        {
            Temporary(root => {
                foreach (string bad in new[] { "../escape.txt", "app/data/state.json", "app/../../escape.txt" })
                {
                    string archive = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
                    using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) using (var writer = new StreamWriter(zip.CreateEntry(bad).Open())) writer.Write("test");
                    Reject(() => AppUpdates.Extract(archive, Path.Combine(root, "out"), "1.2.0"), "unsafe archive accepted");
                }
                Check(!File.Exists(Path.Combine(root, "escape.txt")), "archive escaped the destination");
            });
        }
        static void UninstallChoice()
        {
            bool invoked = false;
            var window = new UninstallWindow(remove => { invoked = true; Check(remove, "wrong data mode"); return Task.FromResult(true); }, "test-data");
            Check(window.KeepData.IsChecked == true && window.UninstallButton.IsEnabled, "uninstall does not default to retaining data");
            window.Show(); window.UpdateLayout(); Snapshot(window, "uninstall-keep");
            if (LightTheme.Dark) foreach (var label in new Control[] { window.KeepData, window.RemoveData }.Concat(Descendants(window).OfType<Expander>()))
                Check(((SolidColorBrush)label.Foreground).Color.R > 150, "native option text remained dark");
            window.RemoveData.IsChecked = true; Check(!window.UninstallButton.IsEnabled, "deletion does not require confirmation");
            window.UpdateLayout(); Snapshot(window, "uninstall-delete-disabled");
            window.ConfirmDeletion.IsChecked = true; Check(window.UninstallButton.IsEnabled, "acknowledged deletion cannot continue");
            window.UninstallButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(invoked, "uninstall action was not dispatched");
        }
        static void SwitchInitialization()
        {
            Temporary(root => { using (var controller = new DesktopController(new StateStore(root), root, false)) {
                controller.ShowSettings();
                for (int round = 0; round < 3; round++) foreach (string page in SettingsWindow.Pages)
                {
                    controller.Settings.SelectPage(page); controller.Settings.UpdateLayout();
                    foreach (var box in Descendants(controller.Settings).OfType<SwitchBox>())
                    {
                        var thumb = (FrameworkElement)box.Template.FindName("Thumb", box);
                        var slide = (TranslateTransform)thumb.RenderTransform;
                        Check(!slide.HasAnimatedProperties && slide.X == (box.IsChecked == true ? 16 : 0), "page initialization played switch animation");
                    }
                }
            }});
            var toggle = new SwitchBox { Style = LightTheme.SwitchStyle, IsChecked = true };
            var host = new Window { Content = toggle, Width = 200, Height = 100 }; host.Show(); host.UpdateLayout(); Pump(50);
            var transform = (TranslateTransform)((FrameworkElement)toggle.Template.FindName("Thumb", toggle)).RenderTransform;
            typeof(SwitchBox).GetMethod("OnToggle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(toggle, null);
            Check(toggle.IsChecked == false && transform.HasAnimatedProperties, "user toggle no longer animates");
            toggle.IsChecked = true; Check(transform.X == 16 && !transform.HasAnimatedProperties, "programmatic sync retained stale motion"); host.Close();
        }
        static void SettingsPages()
        {
            Temporary(root => {
                using (var controller = new DesktopController(new StateStore(root), Path.Combine(root, "Screenshots"), false))
                {
                    var events = (WindowsEvents)typeof(DesktopController).GetField("events", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
                    Check(typeof(WindowsEvents).GetField("ClipboardChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(events) == null, "maintenance UI started collecting clipboard content before normal startup");
                    controller.Shelf.Show(); controller.Shelf.Reveal(true, true, 0); controller.ShowSettings();
                    foreach (string page in SettingsWindow.Pages) { controller.Settings.SelectPage(page); controller.Settings.UpdateLayout(); Check(controller.Settings.CurrentPage == page, "settings page missing"); Snapshot(controller.Settings, "settings-" + Array.IndexOf(SettingsWindow.Pages, page)); }
                    Check(controller.Settings.Navigation.Items.Count == 4, "settings navigation is not a category list");
                    var buttons = Descendants(controller.Settings).OfType<Button>().ToArray();
                    Check(buttons.Any(b => (b.Content as string ?? "").StartsWith("创建快捷方式")), "shortcut action missing");
                    Check(buttons.Where(b => b.IsVisible && b.Style == LightTheme.LinkStyle).All(b => b.ActualHeight == 36), "settings action slots differ");
                    var nav = (ListBoxItem)controller.Settings.Navigation.ItemContainerGenerator.ContainerFromIndex(0);
                    Check(nav.ActualHeight == 48 && nav.HorizontalContentAlignment == HorizontalAlignment.Stretch, "navigation rows or distributed labels differ");
                    var letter = Descendants(nav).OfType<TextBlock>().First();
                    Check(((SolidColorBrush)letter.Foreground).Color == ((SolidColorBrush)nav.Foreground).Color, "navigation label ignored theme or selection color");
                    Check(System.Windows.Automation.AutomationProperties.GetName(nav) == SettingsWindow.Pages[0], "navigation category has no accessible name");
                    Check(!Descendants(nav).OfType<System.Windows.Shapes.Path>().Any() && letter.FontSize == 16, "sidebar retained icons or small labels");
                    var actions = buttons.Where(b => b.IsVisible && b.Style == LightTheme.ButtonStyle).ToArray();
                    Check(actions.Length >= 4 && actions.All(b => Math.Abs(b.ActualWidth - 200) < .01 && Math.Abs(b.ActualHeight - 36) < .01), "settings button sizes differ");
                    foreach (var action in actions) { var presenter = Descendants(action).OfType<ContentPresenter>().First(); Check(presenter.HorizontalAlignment == HorizontalAlignment.Center && presenter.VerticalAlignment == VerticalAlignment.Center, "button label is not centered"); }

                    uint key = 0; for (uint next = 0x7C; next <= 0x87; next++) if (controller.SetToggleShortcut(7, next)) { key = next; break; }
                    Check(key != 0 && controller.ToggleShortcutAvailable, "shortcut was saved without registering it");
                    Check(!controller.SetCaptureShortcut(7, key), "two actions share one shortcut");
                    controller.ToggleShelf(); Check(controller.Settings == null && controller.Shelf.Revealed, "shortcut did not leave settings and reveal the rope");
                    Check((bool)typeof(ShelfWindow).GetField("pinned", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller.Shelf), "settings restoration changed a manual reveal into a timed peek");
                    controller.ToggleShelf(); Check(!controller.Shelf.Revealed, "shortcut could not hide the rope");
                }
            });
        }
        static void BasketUpdates()
        {
            Temporary(root => {
                var g = new Gallery(new StateStore(root)); var one = Image(g, "one.png", true); var two = Image(g, "two.png", true);
                var basket = new BasketWindow(g, i => { }, (a, i) => { });
                Check(basket.VisibleCount == 2 && (string)basket.AllTab.Tag == "primary", "empty pending tab is the default");
                var originalButton = basket.ChoiceFor(one.Path);
                g.Remove(one); Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);
                Check(ReferenceEquals(originalButton, basket.ChoiceFor(one.Path)), "unchanged controls were rebuilt");
                basket.PendingTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(basket.VisibleCount == 1, "pending scope includes mounted pictures");
                basket.ChoiceFor(one.Path).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(basket.SelectedCount == 1, "picture selection failed");
                basket.AllTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(basket.VisibleCount == 2 && basket.SelectedCount == 1, "scope change lost a visible selection");
                basket.SelectAllBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(basket.SelectedCount == 2 && basket.SelectAllBox.IsChecked == true && (string)basket.SelectAllBox.Content == "全选", "select-all checkbox state diverged");
                basket.SelectAllBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(basket.SelectedCount == 0 && basket.SelectAllBox.IsChecked == false, "select-all checkbox did not clear");
                basket.ChoiceFor(one.Path).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                g.Forget(one); Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);
                Check(basket.VisibleCount == 1 && basket.SelectedCount == 0, "removed image remains selected");
                basket.Close();
            });
        }
        static void ButtonContrast()
        {
            foreach (string kind in new[] { "primary", "danger", "danger-primary" })
            {
                var button = LightTheme.Button("test", () => { }, kind); button.ApplyTemplate();
                var foreground = ((SolidColorBrush)button.Foreground).Color; var background = ((SolidColorBrush)button.Background).Color;
                Func<Color, double> luminance = c => {
                    Func<byte, double> linear = channel => { double value = channel / 255.0; return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4); };
                    return 0.2126 * linear(c.R) + 0.7152 * linear(c.G) + 0.0722 * linear(c.B);
                };
                double first = luminance(foreground), second = luminance(background);
                Check((Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05) >= 4.5, kind + " button label has low contrast");
            }
        }
        static void ReplacementMetadata()
        {
            Temporary(root => {
                var g = new Gallery(new StateStore(root)); var before = Image(g, "before.png", true); var after = Image(g, "after.png");
                g.Forget(after); g.Store.State.CardOffsets[before.Path] = 167;
                var shelf = new ShelfWindow(g, delegate { }, delegate { }); shelf.Show(); shelf.Update();
                try {
                    var cards = (Dictionary<string, ShotCard>)typeof(ShelfWindow).GetField("cards", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(shelf);
                    var originalCard = cards[before.Path]; double position = originalCard.MountX;
                    var replacement = g.ReplaceImage(before, after.Path, after.Image);
                    Check(g.Store.State.CardOffsets[replacement.Path] == 167, "metadata was lost when the path changed");
                    Check(cards[replacement.Path] == originalCard && originalCard.MountX == position && originalCard.Item == replacement, "replacement rebuilt or moved the clip");
                    for (int i = 0; i < 12; i++) {
                        double last = cards.Values.Max(card => card.MountX);
                        var next = Image(g, "append-" + i, true);
                        Check(cards[next.Path].MountX > last, "new capture wrapped to the left");
                        Check(originalCard.MountX == position, "append moved an existing clip");
                    }
                } finally { shelf.Close(); }
            });
        }
        static void CompleteArchive()
        {
            Temporary(root => {
                string archive = Path.Combine(root, "valid.zip");
                using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(System.Reflection.Assembly.GetExecutingAssembly().Location, "app/PickSnap.exe");
                    foreach (string file in new[] { "editor/index.html", "update.ps1", "uninstall.ps1", "maintenance-common.ps1" }) using (var writer = new StreamWriter(zip.CreateEntry("app/" + file).Open())) writer.Write("fixture");
                }
                AppUpdates.Extract(archive, Path.Combine(root, "valid"), AppUpdates.CurrentVersion);
                Check(File.Exists(Path.Combine(root, "valid", "app", "PickSnap.exe")), "valid update package was rejected");
                using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update)) zip.GetEntry("app/maintenance-common.ps1").Delete();
                Reject(() => AppUpdates.Extract(archive, Path.Combine(root, "missing"), AppUpdates.CurrentVersion), "incomplete uninstall/update support was accepted");
            });
        }
        static void Await(Task task)
        {
            var clock = Stopwatch.StartNew();
            while (!task.IsCompleted && clock.ElapsedMilliseconds < 10000) { Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background); Thread.Sleep(5); }
            Check(task.IsCompleted, "async fixture did not finish"); task.GetAwaiter().GetResult();
        }
        static void Snapshot(FrameworkElement element, string name)
        {
            if (!render) return;
            Pump(180);
            string directory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "ui-checks")); Directory.CreateDirectory(directory);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * 1.5), (int)Math.Ceiling(element.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32); bitmap.Render(element);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(Path.Combine(directory, name + (LightTheme.Dark ? "-dark" : "-light") + ".png"))) encoder.Save(stream);
        }
        static void MenuLifecycle()
        {
            var host = new Window { Width = 320, Height = 200, Content = new Grid(), ShowInTaskbar = false }; host.Show();
            int actions = 0;
            var menu = new ImageMenu(a => actions++, true) { PlacementTarget = (FrameworkElement)host.Content, StaysOpen = true };
            for (int i = 0; i < 3; i++)
            {
                menu.IsOpen = true; menu.UpdateLayout(); menu.BulkTrash.IsSubmenuOpen = true; Pump(80);
                menu.BulkActions[ShotAction.TrashAll].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(80);
                Check(!menu.IsOpen && !menu.BulkTrash.IsSubmenuOpen && actions == i + 1, "submenu action did not close exactly once");
            }
            menu.IsOpen = true; menu.UpdateLayout(); menu.BulkMenu.IsSubmenuOpen = true; Pump(60);
            menu.CloseMenu(); Pump(60); Check(!menu.IsOpen && !menu.BulkMenu.IsSubmenuOpen, "owner disposal leaves submenu open");
            host.Close(); Pump(40);
        }

        static void MenuDismissalTiming()
        {
            var p = new MenuDismissalPolicy(); p.Reset();
            Check(!p.ShouldClose(0, false, false, false, false), "opening must allow travel from tray icon");
            Check(!p.ShouldClose(600, false, false, false, false), "opening grace too short");
            Check(p.ShouldClose(1280, false, false, false, false), "abandoned menu stays open");
            p.Reset(); p.ShouldClose(80, true, false, false, false);
            Check(!p.ShouldClose(160, false, false, false, false), "leave closes immediately");
            Check(!p.ShouldClose(400, true, false, false, false), "submenu/reentry must cancel closing");
            p.ShouldClose(480, false, false, false, false);
            Check(p.ShouldClose(960, false, false, false, false), "leaving for another image must close");
            p.Reset(); Check(!p.ShouldClose(2000, false, true, false, false), "keyboard menu must stay open");
            Check(p.ShouldClose(2100, false, true, false, true), "app switch must close keyboard menu");
            p.Reset(); Check(p.ShouldClose(300, false, false, true, false), "outside click must close");
        }

        static void MenuAndBasketLayout()
        {
            var host = new Window { Width = 400, Height = 300, Content = new Grid(), ShowInTaskbar = false }; host.Show();
            var menu = new TrayPanel { PlacementTarget = (FrameworkElement)host.Content, StaysOpen = true };
            menu.SetShortcuts("Ctrl+Alt+Shift+Win+F24", "Ctrl+Alt+S"); menu.SyncCollection(true, false); menu.SetVisibility(true); menu.IsOpen = true; menu.UpdateLayout();
            menu.BeginAnimation(UIElement.OpacityProperty, null); menu.Opacity = 1;
            Check(System.Windows.Automation.AutomationProperties.GetName(menu.ExitButton) == "退出", "exit label is verbose");
            foreach (var text in Descendants(menu).OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text)))
            {
                var measured = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentUICulture, text.FlowDirection, new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, text.Foreground, 1.5);
                Check(text.TextTrimming != TextTrimming.None || text.ActualWidth + 2 >= measured.WidthIncludingTrailingWhitespace, "menu text clipped: " + text.Text);
            }
            Snapshot(menu, "tray"); menu.Close();
            var imageMenu = new ImageMenu(a => { }, true) { PlacementTarget = (FrameworkElement)host.Content, StaysOpen = true };
            imageMenu.SetDetails("657 × 347"); imageMenu.IsOpen = true; imageMenu.UpdateLayout(); Snapshot(imageMenu, "image-menu");
            imageMenu.BulkTrash.IsSubmenuOpen = true; imageMenu.UpdateLayout(); Pump(180);
            var popup = imageMenu.BulkTrash.Template.FindName("PART_Popup", imageMenu.BulkTrash) as System.Windows.Controls.Primitives.Popup;
            if (popup != null && popup.Child != null) Snapshot((FrameworkElement)popup.Child, "image-submenu");
            imageMenu.BulkTrash.IsSubmenuOpen = false; Pump(60); imageMenu.IsOpen = false; Pump(60); host.Close();
            Temporary(root => {
                var g = new Gallery(new StateStore(root)); var one = Image(g, "图像 A.png", true); Image(g, "图像 B.png"); Image(g, "图像 C.png");
                var basket = new BasketWindow(g, i => { }, (a, i) => { }, null, () => 23, () => 24) { Width = 980, Height = 580 }; basket.Show(); basket.UpdateLayout();
                basket.ChoiceFor(g.Collected[1].Path).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); basket.UpdateLayout();
                Check((string)basket.SelectAllBox.Content == "全选" && basket.SelectAllBox.ActualWidth >= 50, "selection control is too narrow");
                Check(Descendants(basket).OfType<Border>().Count(b => b.CornerRadius.TopLeft == 11 && b.Child is TextBlock && new[] { "绳上 1/24", "贴图 0", "待挂 2" }.Contains(((TextBlock)b.Child).Text)) == 3, "three count capsules are missing");
                Snapshot(basket, "basket"); basket.Close();
            });
        }
        static void ButtonBounds()
        {
            foreach (double scale in new[] { 1.0, 1.5, 2.0 })
            {
                var button = LightTheme.Button("全选", () => { }); button.Width = 96; button.Height = 36; button.FontSize = 14; button.LayoutTransform = new ScaleTransform(scale, scale);
                foreach (string label in new[] { "全选", "✓ 取消全选", "创建快捷方式", "卸载并删除当前数据", "复制失败，重试", "重启并安装" })
                {
                    button.Content = label; button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); button.Arrange(new Rect(new Point(), button.DesiredSize));
                    var text = new FormattedText(label, System.Globalization.CultureInfo.CurrentUICulture, button.FlowDirection, new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch), button.FontSize, button.Foreground, scale);
                    Check(button.ActualWidth >= text.WidthIncludingTrailingWhitespace + button.Padding.Left + button.Padding.Right + 2, "button clipped: " + label + " at " + scale);
                    var presenter = Descendants(button).OfType<ContentPresenter>().First();
                    var center = presenter.TranslatePoint(new Point(presenter.ActualWidth / 2, presenter.ActualHeight / 2), button);
                    Check(Math.Abs(center.X - button.ActualWidth / 2) <= 1 && Math.Abs(center.Y - button.ActualHeight / 2) <= 1, "button text is not centered: " + label);
                }
            }
        }
        sealed class DelayedHandler : HttpMessageHandler
        {
            internal int Calls;
            internal readonly TaskCompletionSource<HttpResponseMessage> Response = new TaskCompletionSource<HttpResponseMessage>();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return Response.Task; }
        }
        static string ReleaseJson(string version)
        {
            string name = "PickSnap-" + version + "-windows-x64.zip";
            return "{\"tag_name\":\"v" + version + "\",\"assets\":[{\"name\":\"" + name + "\",\"browser_download_url\":\"" + AppUpdates.Repository + "/releases/download/v" + version + "/" + name + "\",\"size\":100,\"digest\":\"sha256:" + new string('a',64) + "\"}]}";
        }
        static void SharedUpdateCheck()
        {
            Temporary(root => {
                var handler = new DelayedHandler();
                using (var controller = new DesktopController(new StateStore(root), Path.Combine(root, "Screenshots"), false, new AppUpdates(handler)))
                {
                    controller.Shelf.Show(); controller.ShowSettings("更新与卸载");
                    var background = controller.CheckForUpdates(false); var manual = controller.CheckForUpdates(true);
                    Check(ReferenceEquals(background, manual) && controller.LastUpdateCheck.Phase == UpdateCheckPhase.Checking, "manual click did not join background work");
                    Check(controller.Settings.UpdateStatusText.Text.Contains("正在") && !controller.Settings.CheckUpdateButton.IsEnabled, "in-flight check has no visible feedback");
                    handler.Response.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ReleaseJson(AppUpdates.CurrentVersion)) }); Await(manual);
                    Check(handler.Calls == 1 && controller.LastUpdateCheck.Phase == UpdateCheckPhase.Current, "shared check result is incorrect");
                    controller.Settings.SelectPage("快捷键"); controller.Settings.SelectPage("更新与卸载");
                    Check(controller.Settings.UpdateStatusText.Text.Contains("最新版本") && controller.Settings.CheckUpdateButton.IsEnabled, "check result disappeared across page navigation");
                }
            });
        }
        static void UpdateStates()
        {
            foreach (bool found in new[] { false, true }) Temporary(root => {
                var handler = new DelayedHandler();
                using (var controller = new DesktopController(new StateStore(root), Path.Combine(root, "Screenshots"), false, new AppUpdates(handler)))
                {
                    controller.Shelf.Show(); controller.ShowSettings("更新与卸载"); var check = controller.CheckForUpdates(true);
                    handler.Response.SetResult(found ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ReleaseJson("9.9.9")) } : new HttpResponseMessage(HttpStatusCode.NotFound)); Await(check);
                    Check(controller.LastUpdateCheck.Phase == (found ? UpdateCheckPhase.Available : UpdateCheckPhase.Failed), "result phase was collapsed into up-to-date");
                    Check(found ? controller.Settings.ViewUpdateButton.Visibility == Visibility.Visible : controller.Settings.UpdateStatusText.Text.Contains("检查失败"), "result has no clear action or failure explanation");
                }
            });
        }
        sealed class BytesHandler : HttpMessageHandler
        {
            readonly byte[] bytes;
            internal BytesHandler(byte[] bytes) { this.bytes = bytes; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }); }
        }
        static void DownloadAutoInstall()
        {
            Temporary(root => {
                string archive = Path.Combine(root, "download.zip");
                using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(System.Reflection.Assembly.GetExecutingAssembly().Location, "app/PickSnap.exe");
                    foreach (string file in new[] { "editor/index.html", "update.ps1", "uninstall.ps1", "maintenance-common.ps1" }) using (var writer = new StreamWriter(zip.CreateEntry("app/" + file).Open())) writer.Write("fixture");
                }
                byte[] bytes = File.ReadAllBytes(archive); string digest;
                using (var hash = System.Security.Cryptography.SHA256.Create()) digest = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
                using (var updates = new AppUpdates(new BytesHandler(bytes)))
                {
                    var release = new UpdateRelease { Version = AppUpdates.CurrentVersion, DownloadUrl = "https://example.invalid/fixture.zip", Size = bytes.Length, Sha256 = digest, Url = AppUpdates.Repository + "/releases/latest", Notes = "test" };
                    int applications = 0;
                    var canceled = new UpdateWindow(release, updates, work => { applications++; return Task.FromResult(false); }, null, () => Task.FromResult(false)); canceled.Show(); canceled.UpdateLayout();
                    Descendants(canceled).OfType<Button>().Single(b => (b.Content as string) == "更新并重启").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(applications == 0 && typeof(UpdateWindow).GetField("downloaded", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(canceled) == null, "canceled preparation started an update"); canceled.Close();
                    var window = new UpdateWindow(release, updates, work => { applications++; return Task.FromResult(false); }); window.Show(); window.UpdateLayout();
                    var button = Descendants(window).OfType<Button>().Single(b => (b.Content as string) == "更新并重启"); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var clock = Stopwatch.StartNew(); while (!button.IsEnabled && clock.ElapsedMilliseconds < 10000) { Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background); Thread.Sleep(5); }
                    Check((button.Content as string) == "继续更新" && applications == 1, "confirmed download did not dispatch installation automatically"); window.Close();
                }
            });
        }
        static void ManualImport()
        {
            Temporary(root => {
                var store = new StateStore(Path.Combine(root, "data")); store.State.CollectClipboard = store.State.CollectScreenshotFiles = false;
                using (var controller = new DesktopController(store, Path.Combine(root, "Screenshots"), false))
                {
                    string original = Path.Combine(root, "source.png"); File.WriteAllBytes(original, Png());
                    var task = controller.Captures.Import(original, null, false); Await(task);
                    Check(task.Result && controller.Gallery.Items.Count == 1 && File.Exists(original), "manual import did not hang or modified the source");
                    Check(!store.State.CollectClipboard && !store.State.CollectScreenshotFiles, "manual import changed automatic preferences");
                }
            });
        }
        static void CollectionSwitches()
        {
            Temporary(root => {
                var store = new StateStore(root); store.State.PauseCollection = true; store.State.CollectClipboard = true; store.State.CollectTextClipboard = true; store.Save();
                store = new StateStore(root);
                Check(!store.State.PauseCollection && !store.State.CollectScreenshotFiles && !store.State.CollectClipboard && !store.State.CollectTextClipboard, "legacy pause unexpectedly resumed collection");
                using (var controller = new DesktopController(store, Path.Combine(root, "Screenshots"), false))
                {
                    controller.SetClipboardImages(true); Check(store.State.CollectClipboard && !store.State.CollectTextClipboard && store.State.CollectScreenshotFiles, "image switch did not enable both image sources");
                    Check(controller.TrayMenu.PicturesSwitch.IsChecked == true && controller.TrayMenu.TextSwitch.IsChecked == false, "tray switches are stale");
                    controller.SetClipboardText(true); controller.SetClipboardImages(false);
                    Check(!store.State.CollectClipboard && store.State.CollectTextClipboard && !store.State.CollectScreenshotFiles, "text source depends on the image source");
                    controller.ShowSettings("收集与清理"); controller.Settings.UpdateLayout();
                    Check(!Descendants(controller.Settings).OfType<TextBlock>().Any(t => t.Text.Contains("自动收集新内容")), "ambiguous master switch remains");
                }
            });
        }
        static void ScreenshotCopies()
        {
            Temporary(root => {
                var store = new StateStore(Path.Combine(root, "data")); var gallery = new Gallery(store);
                using (var captures = new CapturePipeline(gallery, Dispatcher.CurrentDispatcher, Path.Combine(root, "Screenshots")))
                {
                    string source = Path.Combine(root, "original.png"); File.WriteAllBytes(source, Png());
                    var task = captures.Import(source, CaptureSource.Folder, false); Await(task);
                    Check(task.Result && gallery.Items.Count == 1 && FileRules.IsInside(gallery.Items[0].Path, store.Inbox) && File.Exists(source), "folder import did not store its own hanging copy");
                    captures.CollectClipboard = false;
                    string other = Path.Combine(root, "other.png"); File.WriteAllBytes(other, Png()); task = captures.Import(other, CaptureSource.Folder, false); Await(task);
                    Check(!task.Result && gallery.Items.Count == 1, "image switch did not stop folder collection");
                    task = captures.Import(other, null, false); Await(task); Check(task.Result && gallery.Items.Count == 2, "manual import depended on auto collection");
                }
            });
        }
        static void BasketCapacity()
        {
            Temporary(root => {
                var g = new Gallery(new StateStore(root)); var first = Image(g, "first.png"); var second = Image(g, "second.png"); int room = 1;
                var basket = new BasketWindow(g, i => { room--; g.Add(i.Path, i.Image); }, (a, i) => { }, null, () => room, () => 24); basket.Show(); basket.UpdateLayout();
                basket.ChoiceFor(first.Path).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); basket.ChoiceFor(second.Path).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); basket.UpdateLayout();
                Check(basket.HangButton.IsEnabled && Descendants(basket).OfType<TextBlock>().Any(t => t.Text == "已选 2 张，绳子上还能挂 1 张"), "selection does not show the remaining capacity");
                basket.HangButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); basket.UpdateLayout();
                Check(g.Items.Count == 1 && basket.SelectedCount == 1 && !basket.HangButton.IsEnabled, "overflow selection displaced a hanging image"); basket.Close();
            });
        }
        static void LargeBasket()
        {
            Temporary(root => {
                var g = new Gallery(new StateStore(root)); Directory.CreateDirectory(g.Store.Inbox); byte[] bytes = Png(); var content = ImageContent.Decode(bytes);
                for (int index = 0; index < 1000; index++) { string file = Path.Combine(g.Store.Inbox, "frame-" + index.ToString("D4") + ".png"); File.WriteAllBytes(file, bytes); g.Collected.Add(new CaptureItem(file, content)); }
                var clock = Stopwatch.StartNew();
                var basket = new BasketWindow(g, i => { }, (a, i) => { }); long initial = clock.ElapsedMilliseconds;
                var item = g.Collected[0]; var button = basket.ChoiceFor(item.Path); clock.Restart();
                basket.AllTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); g.Add(item.Path, item.Image); Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background); long incremental = clock.ElapsedMilliseconds;
                Check(basket.VisibleCount == 1000 && ReferenceEquals(button, basket.ChoiceFor(item.Path)), "incremental refresh recreated a thousand controls");
                basket.Close();
                Console.WriteLine("  1000-image fixture: initial " + initial + " ms, incremental " + incremental + " ms (local machine only)");
            });
        }
    }
}
