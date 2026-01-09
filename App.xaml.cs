using System;
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
            MainPage = new NavigationPage(new MainPage());
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = base.CreateWindow(activationState);

#if WINDOWS
            window.HandlerChanged += (s, e) =>
            {
                var nativeWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                if (nativeWindow == null) return;

                var hwnd = WindowNative.GetWindowHandle(nativeWindow);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);

                if (appWindow.Presenter is not OverlappedPresenter presenter)
                    return;

                // 1) Restore on startup
                bool hasSaved =
                    Preferences.ContainsKey(WindowWidthKey) &&
                    Preferences.ContainsKey(WindowHeightKey) &&
                    Preferences.ContainsKey(WindowXKey) &&
                    Preferences.ContainsKey(WindowYKey);

                if (!hasSaved)
                {
                    // First run on this PC: open full-size (maximized) on whichever monitor we’re on.
                    presenter.Maximize();
                    Preferences.Set(WindowStateKey, "Maximized");
                }
                else
                {
                    int w = Preferences.Get(WindowWidthKey, 1200);
                    int h = Preferences.Get(WindowHeightKey, 800);
                    int x = Preferences.Get(WindowXKey, 100);
                    int y = Preferences.Get(WindowYKey, 100);
                    string state = Preferences.Get(WindowStateKey, "Normal");

                    // Clamp to visible area (handles monitor changes/docking)
                    var clamped = ClampRectToVisibleDisplay(new RectInt32(x, y, w, h));

                    // Apply position/size before state
                    appWindow.MoveAndResize(clamped);

                    if (string.Equals(state, "Maximized", StringComparison.OrdinalIgnoreCase))
                        presenter.Maximize();
                    else
                        presenter.Restore();
                }

                // 2) Persist any user changes (resize/move/maximize/restore)
                // Note: AppWindow.Changed fires a lot; keep the handler fast.
                appWindow.Changed += (sender, args) =>
                {
                    try
                    {
                        var pos = sender.Position;
                        var size = sender.Size;

                        // Some transitions can briefly report tiny/0 sizes; ignore those.
                        if (size.Width < 200 || size.Height < 200) return;

                        // Determine if "looks maximized" by comparing to the current display work area.
                        var display = DisplayArea.GetFromWindowId(sender.Id, DisplayAreaFallback.Primary);
                        var work = display.WorkArea; // excludes taskbar

                        bool looksMaximized =
                            Math.Abs(pos.X - work.X) <= 2 &&
                            Math.Abs(pos.Y - work.Y) <= 2 &&
                            Math.Abs(size.Width - work.Width) <= 2 &&
                            Math.Abs(size.Height - work.Height) <= 2;

                        // Always save state
                        Preferences.Set(WindowStateKey, looksMaximized ? "Maximized" : "Normal");

                        // Only persist restore bounds when NOT maximized,
                        // so maximizing doesn't overwrite the user's preferred normal size.
                        if (!looksMaximized)
                        {
                            Preferences.Set(WindowXKey, pos.X);
                            Preferences.Set(WindowYKey, pos.Y);
                            Preferences.Set(WindowWidthKey, size.Width);
                            Preferences.Set(WindowHeightKey, size.Height);
                        }
                    }
                    catch
                    {
                        // Never crash because of persistence
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

            const int minW = 400;
            const int minH = 300;

            int w = Math.Max(rect.Width, minW);
            int h = Math.Max(rect.Height, minH);

            // Clamp size to work area
            if (w > work.Width) w = work.Width;
            if (h > work.Height) h = work.Height;

            // Clamp position
            int maxX = (work.X + work.Width) - w;
            int maxY = (work.Y + work.Height) - h;

            int x = rect.X;
            int y = rect.Y;

            if (x < work.X) x = work.X;
            if (y < work.Y) y = work.Y;

            if (x > maxX) x = maxX;
            if (y > maxY) y = maxY;

            return new RectInt32(x, y, w, h);
        }
#endif
    }
}
