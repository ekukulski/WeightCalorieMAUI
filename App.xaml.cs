using System;
using System.Threading.Tasks;
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

                bool isApplyingRestore = false;

                // Persist whenever the user moves/resizes/maximizes/restores.
                appWindow.Changed += async (sender, args) =>
                {
                    if (isApplyingRestore)
                        return;

                    var op = appWindow.Presenter as OverlappedPresenter;
                    if (op == null)
                        return;

                    var state = (op.State == OverlappedPresenterState.Maximized) ? "Maximized" : "Normal";
                    Preferences.Default.Set(WindowStateKey, state);

                    if (state != "Normal")
                        return;

                    if (!args.DidSizeChange && !args.DidPositionChange)
                        return;

                    // Allow Windows to finish snap/chrome/DPI adjustments
                    await Task.Delay(200);

                    if (isApplyingRestore)
                        return;

                    Preferences.Default.Set(WindowWidthKey, appWindow.Size.Width);
                    Preferences.Default.Set(WindowHeightKey, appWindow.Size.Height);
                    Preferences.Default.Set(WindowXKey, appWindow.Position.X);
                    Preferences.Default.Set(WindowYKey, appWindow.Position.Y);
                };

                if (!hasSavedBounds)
                {
                    // FIRST RUN: open full screen (maximized)
                    presenter?.Maximize();
                    return;
                }

                // Apply restore AFTER first activation (prevents "drift" from Windows final placement)
                bool appliedOnce = false;
                nativeWindow.Activated += async (_, __) =>
                {
                    if (appliedOnce)
                        return;

                    appliedOnce = true;

                    await Task.Delay(150);

                    isApplyingRestore = true;
                    try
                    {
                        // Ensure Normal state before applying normal bounds
                        presenter?.Restore();

                        var rect = new RectInt32(savedX, savedY, savedW, savedH);
                        rect = ClampRectToVisibleDisplay(rect);

                        appWindow.MoveAndResize(rect);

                        // If last state was maximized, maximize AFTER setting normal bounds
                        if (savedState == "Maximized")
                            presenter?.Maximize();
                    }
                    finally
                    {
                        // Give Windows time to settle before allowing saves again
                        await Task.Delay(150);
                        isApplyingRestore = false;
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

            // Clamp size to work area
            if (w > work.Width) w = work.Width;
            if (h > work.Height) h = work.Height;

            // Clamp position (no arbitrary nudges)
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
