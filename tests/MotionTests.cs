using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ScreenLine
{
    internal static class MotionTests
    {
        static int failures;
        [STAThread]
        static int Main()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            LightTheme.UseDark(false); LightTheme.Install(app);
            Run("Travel curves start and finish without a velocity jump", Curves);
            Run("Travel duration stays bounded across distances", Durations);
            Run("Springs follow the same path at 60 and 144 Hz", FrameRates);
            Run("Cards peak at twelve degrees, basket at eight, and flight slots stay reserved", SwingAmplitude);
            Run("Return target stays fixed while the shelf reveals", LandingTarget);
            Run("Flight hands off once without a transparent card frame", Handoff);
            Run("Clip detach and pin return complete without losing the card", PinCycle);
            Run("Basket arrival completes once and cancellation does not collect", Collection);
            app.Shutdown();
            Console.WriteLine(failures == 0 ? "All 8 motion checks passed." : failures + " motion check(s) failed.");
            return failures == 0 ? 0 : 1;
        }

        static void Run(string name, Action test)
        {
            try { test(); Console.WriteLine("PASS: " + name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL: " + name + " - " + error.Message); }
        }
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        static void Curves()
        {
            Check(MotionTiming.Progress(0) == 0 && MotionTiming.Progress(1) == 1, "invalid travel endpoints");
            Check(MotionTiming.Arc(0) == 0 && MotionTiming.Arc(1) == 0, "arc does not return to the landing plane");
            double previous = 0;
            for (int i = 0; i <= 1000; i++)
            {
                double t = i / 1000.0, next = MotionTiming.Progress(t), arc = MotionTiming.Arc(t);
                Check(next >= previous && next <= 1 && arc >= 0 && arc <= 1, "overshoot or reverse travel"); previous = next;
            }
            const double step = 0.00001;
            Check(MotionTiming.Progress(step) / step < 0.001 && MotionTiming.Arc(step) / step < 0.001, "launch velocity is discontinuous");
            Check((1 - MotionTiming.Progress(1 - step)) / step < 0.001 && MotionTiming.Arc(1 - step) / step < 0.001, "landing velocity is discontinuous");
        }
        static void Durations()
        {
            Rect start = new Rect(0, 0, 100, 100);
            double nearby = MotionTiming.TravelSeconds(start, new Rect(20, 20, 100, 100), false);
            double distant = MotionTiming.TravelSeconds(start, new Rect(12000, 8000, 100, 100), false);
            Check(nearby >= 0.28 && distant <= 0.420001 && distant > nearby, "distance timing is unbounded");
        }
        static void FrameRates()
        {
            var sixty = new SpringMotion(0.72, 0.6, 3) { Target = 0 };
            var fast = new SpringMotion(0.72, 0.6, 3) { Target = 0 };
            for (int i = 0; i < 30; i++) sixty.Step(1.0 / 60);
            for (int i = 0; i < 72; i++) fast.Step(1.0 / 144);
            Check(Math.Abs(sixty.Position - fast.Position) < 0.000001 && Math.Abs(sixty.Velocity - fast.Velocity) < 0.000001, "frame rate changes spring trajectory");
        }

        static void SwingAmplitude()
        {
            Check(MotionTiming.CardSwing == 12 && MotionTiming.BasketSwing == 8, "configured card or basket swing differs");
            foreach (double response in new[] { .62, .72 })
            {
                double amplitude = response == .72 ? MotionTiming.CardSwing : MotionTiming.BasketSwing;
                var spring = new SpringMotion(response, .6, 0); spring.Kick(amplitude); double peak = 0;
                for (int i = 0; i < 2000; i++) { spring.Step(.001); peak = Math.Max(peak, spring.Position); }
                Check(peak > amplitude - .01 && peak <= amplitude + .001 && spring.Settled, "swing overshot or failed to settle");
            }
            WithShelf((shelf, card) => { int room = shelf.RoomForHanging(); card.BeginFlight(); Check(shelf.RoomForHanging() == room, "flying card did not reserve its slot"); card.Land(); });
        }
        static void WithShelf(Action<ShelfWindow, ShotCard> test)
        {
            string root = Path.Combine(Path.GetTempPath(), "PickSnap-motion-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            ShelfWindow shelf = null;
            try
            {
                var gallery = new Gallery(new StateStore(root));
                string file = Path.Combine(root, "test.png");
                byte[] pixels = new byte[96 * 64 * 4];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = 255;
                File.WriteAllBytes(file, ImageContent.EncodeClipboard(BitmapSource.Create(96, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 96 * 4)));
                gallery.Add(file, ImageContent.Read(file));
                shelf = new ShelfWindow(gallery, (action, item) => { }, () => { });
                shelf.Show(); shelf.UpdateLayout();
                test(shelf, shelf.CardFor(file));
            }
            finally
            {
                if (shelf != null) shelf.Close();
                if (!FileRules.IsInside(root, Path.GetTempPath())) throw new InvalidOperationException("Unsafe test cleanup path");
                Directory.Delete(root, true);
            }
        }

        static void LandingTarget()
        {
            WithShelf((shelf, card) =>
            {
                var slide = (TranslateTransform)typeof(ShelfWindow).GetField("slide", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(shelf);
                slide.Y = -180;
                Rect moving = card.PhotoRect(), target = card.PhotoRect(true);
                slide.Y = -40;
                Rect next = card.PhotoRect(true);
                Check(Math.Abs(target.Y - next.Y) < 0.01 && Math.Abs(target.X - next.X) < 0.01, "flight target follows the reveal animation");
                Check(Math.Abs(moving.Y - target.Y) > 100, "test did not exercise a hidden shelf");
            });
        }

        static void Handoff()
        {
            WithShelf((shelf, card) =>
            {
                var slide = (TranslateTransform)typeof(ShelfWindow).GetField("slide", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(shelf);
                slide.Y = 0;
                card.Settle(); card.BeginFlight();
                int calls = 0; bool closed = false;
                Rect target = card.PhotoRect(true);
                var flight = new ShotFlight(shelf.Display, card, new Rect(target.X, target.Y + 120, target.Width * 2, target.Height * 2), false, () => { calls++; card.Land(); });
                flight.Closed += (sender, e) => closed = true;
                flight.Show();
                var pump = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                DateTime deadline = DateTime.UtcNow.AddSeconds(2);
                timer.Tick += (sender, e) => { if (closed || DateTime.UtcNow >= deadline) pump.Continue = false; };
                timer.Start();
                try { Dispatcher.PushFrame(pump); }
                finally { timer.Stop(); if (!closed) flight.Close(); }
                Check(closed && calls == 1, "flight did not complete exactly once");
                Check(!card.Flying && card.Opacity == 1, "landing leaves a transparent card frame");
            });
        }

        static void PumpUntil(Func<bool> ready)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            timer.Tick += (sender, e) => { if (ready() || DateTime.UtcNow >= deadline) frame.Continue = false; };
            timer.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
            Check(ready(), "animation did not complete within 3 seconds");
        }

        static void PinCycle()
        {
            WithShelf((shelf, card) =>
            {
                shelf.ChoosingAppearance = true; shelf.Reveal(true, false, 60, true);
                var release = typeof(ShelfWindow).GetMethod("ReleasePhoto", BindingFlags.Instance | BindingFlags.NonPublic);
                var task = (Task)release.Invoke(shelf, new object[] { card });
                PumpUntil(() => task.IsCompleted && shelf.PinWindows.Any(w => w.IsHitTestVisible));
                task.GetAwaiter().GetResult();
                Check(card.Detached && card.Surface.Opacity == 0, "detached card is still duplicated on the rope");
                var pin = shelf.PinWindows.Single();
                pin.PinButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                PumpUntil(() => !shelf.PinWindows.Any() && !card.Flying);
                Check(!card.Detached && card.Opacity == 1 && card.Grip.IsEnabled, "returned card is invisible or cannot be used");
            });
        }

        static void Collection()
        {
            WithShelf((shelf, card) =>
            {
                Rect origin = card.PhotoRect(true);
                Rect basket = new Rect(origin.Right + 160, origin.Top + 36, 48, 16);
                int arrivals = 0; bool closed = false;
                var cancelled = new ShotFlight(shelf.Display, card, origin, false, () => arrivals++, basketTarget: () => basket);
                cancelled.Show(); cancelled.Close();
                Check(arrivals == 0, "cancelled animation reported a basket arrival");
                var flight = new ShotFlight(shelf.Display, card, origin, false, () => arrivals++, basketTarget: () => basket);
                flight.Closed += (sender, e) => closed = true;
                flight.Show();
                PumpUntil(() => closed);
                Check(arrivals == 1, "basket arrival did not complete exactly once");
            });
        }
    }
}
