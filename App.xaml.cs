using Microsoft.Maui;
using Microsoft.Maui.Controls;

#if WINDOWS
using Microsoft.Maui.Storage;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;
#endif

namespace WeightCalorieMAUI
{
    public partial class App : Application
    {
#if WINDOWS
        // Preference keys (persisted per PC)
        private const string WindowWidthKey = "WindowWidth";
        private const string WindowHeightKey = "WindowHeight";
        private const string WindowXKey = "WindowX";
        private const string WindowYKey = "WindowY";
        private const string WindowStateKey = "WindowState"; // "Maximized" or "Normal"
#endif

        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new AppShell());

#if WINDOWS
            // Run once when the native WinUI window handle becomes available.
            window.HandlerChanged += (s, e) =>
            {
                var nativeWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                if (nativeWindow == null)
                    return;

                var hwnd = WindowNative.GetWindowHandle(nativeWindow);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);

                var presenter = appWindow.Presenter as OverlappedPresenter;

                // Read saved settings
                int savedW = Preferences.Default.Get(WindowWidthKey, 0);
                int savedH = Preferences.Default.Get(WindowHeightKey, 0);
                int savedX = Preferences.Default.Get(WindowXKey, int.MinValue);
                int savedY = Preferences.Default.Get(WindowYKey, int.MinValue);
                string savedState = Preferences.Default.Get(WindowStateKey, "");

                bool hasSavedBounds =
                    savedW > 0 && savedH > 0 &&
                    savedX != int.MinValue && savedY != int.MinValue;

                if (!hasSavedBounds)
                {
                    // FIRST RUN: open full screen (maximized)
                    presenter?.Maximize();
                }
                else
                {
                    // Restore size + position (and keep visible)
                    var rect = new RectInt32(savedX, savedY, savedW, savedH);
                    rect = ClampRectToVisibleDisplay(rect);

                    appWindow.MoveAndResize(rect);

                    if (savedState == "Maximized")
                        presenter?.Maximize();
                    else
                        presenter?.Restore();
                }

                // Persist whenever the user moves/resizes/maximizes/restores.
                // Save bounds only when "Normal" so maximize doesn't overwrite preferred size/location.
                appWindow.Changed += (sender, args) =>
                {
                    var p = appWindow.Presenter as OverlappedPresenter;
                    var state = (p != null && p.State == OverlappedPresenterState.Maximized)
                        ? "Maximized"
                        : "Normal";

                    Preferences.Default.Set(WindowStateKey, state);

                    if (state == "Normal")
                    {
                        Preferences.Default.Set(WindowWidthKey, appWindow.Size.Width);
                        Preferences.Default.Set(WindowHeightKey, appWindow.Size.Height);
                        Preferences.Default.Set(WindowXKey, appWindow.Position.X);
                        Preferences.Default.Set(WindowYKey, appWindow.Position.Y);
                    }
                };
            };
#endif

            return window;
        }

#if WINDOWS
        private static RectInt32 ClampRectToVisibleDisplay(RectInt32 rect)
        {
            // Ensure the restored window is visible even if monitors changed.
            var center = new PointInt32(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            var displayArea = DisplayArea.GetFromPoint(center, DisplayAreaFallback.Primary);
            var work = displayArea.WorkArea; // excludes taskbar

            int minW = 400;
            int minH = 300;

            int w = Math.Max(rect.Width, minW);
            int h = Math.Max(rect.Height, minH);

            int x = rect.X;
            int y = rect.Y;

            // Nudge inside visible area
            if (x < work.X) x = work.X + 20;
            if (y < work.Y) y = work.Y + 20;

            if (x + w > work.X + work.Width) x = (work.X + work.Width) - w - 20;
            if (y + h > work.Y + work.Height) y = (work.Y + work.Height) - h - 20;

            // Clamp if window larger than work area
            if (w > work.Width) w = work.Width;
            if (h > work.Height) h = work.Height;

            return new RectInt32(x, y, w, h);
        }
#endif
    }
}
