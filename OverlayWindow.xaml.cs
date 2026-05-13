using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using BestInScript.API.Services;

// WinForms is pulled in globally (UseWindowsForms=true) for the Screen API,
// so we must disambiguate WPF media types from System.Drawing.
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace BestInScript.API.Overlay
{
    /// <summary>
    /// Tiny always-on-top, click-through status pill that shows which script
    /// is currently running. Driven in-process by polling
    /// <see cref="HotkeyEngine.GetStatus"/>.
    /// </summary>
    public partial class OverlayWindow : Window
    {
        // ── Win32 ──────────────────────────────────────────────────────────
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        // ── State ──────────────────────────────────────────────────────────
        private readonly HotkeyEngine _engine;
        private readonly DispatcherTimer _pollTimer;
        private OverlaySettings _settings;

        // Cached brushes
        private static readonly Brush ActiveDot = new SolidColorBrush(Color.FromRgb(0x3D, 0xDC, 0x84));
        private static readonly Brush IdleDot = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));

        public OverlayWindow(HotkeyEngine engine, OverlaySettings initialSettings)
        {
            InitializeComponent();
            _engine = engine;
            _settings = initialSettings;

            ActiveDot.Freeze();
            IdleDot.Freeze();

            Loaded += OnLoaded;
            SizeChanged += (_, __) => ApplyPosition(_settings);

            _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _pollTimer.Tick += (_, __) => Refresh();
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            ApplySettings(_settings);
            Refresh();
            _pollTimer.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Make the window click-through, topmost-tool, and non-activating.
            // Must run after the HWND exists.
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE,
                ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        // ── Public API used by the hosted service ──────────────────────────

        /// <summary>Apply a fresh settings snapshot (visibility, style, position).</summary>
        public void ApplySettings(OverlaySettings s)
        {
            _settings = s;

            CardBorder.Opacity = s.Opacity;
            StatusLabel.FontSize = s.FontSize;

            if (s.Enabled)
            {
                if (Visibility != Visibility.Visible) Show();
                ApplyPosition(s);
            }
            else
            {
                Hide();
            }
        }

        // ── Status refresh ─────────────────────────────────────────────────

        private void Refresh()
        {
            ScriptStatus? active = null;
            try
            {
                active = _engine.GetStatus().FirstOrDefault(x => x.IsRunning);
            }
            catch
            {
                // Engine not ready / disposed — keep last state.
                return;
            }

            if (active != null)
            {
                StatusDot.Fill = ActiveDot;
                StatusLabel.Text = $"▶  {active.Name}  ·  [{active.TriggerKey}]";
                if (_settings.Enabled && Visibility != Visibility.Visible) Show();
            }
            else
            {
                StatusDot.Fill = IdleDot;
                StatusLabel.Text = "BestInScript · idle";

                if (_settings.HideWhenIdle)
                {
                    if (Visibility == Visibility.Visible) Hide();
                }
                else if (_settings.Enabled && Visibility != Visibility.Visible)
                {
                    Show();
                }
            }
        }

        // ── Positioning ────────────────────────────────────────────────────

        private void ApplyPosition(OverlaySettings s)
        {
            var screens = WinFormsScreen.AllScreens;
            if (screens.Length == 0) return;

            // Resolve target screen
            WinFormsScreen target;
            if (s.ScreenIndex >= 0 && s.ScreenIndex < screens.Length)
                target = screens[s.ScreenIndex];
            else
                target = WinFormsScreen.PrimaryScreen ?? screens[0];

            var b = target.Bounds; // physical pixels

            // Convert physical pixels → WPF device-independent pixels.
            // Uses this window's HWND so we pick up the *target* monitor's DPI
            // when the window is already on it; falls back to system DPI otherwise.
            double sx = 1.0, sy = 1.0;
            var src = PresentationSource.FromVisual(this);
            if (src?.CompositionTarget != null)
            {
                var m = src.CompositionTarget.TransformFromDevice;
                sx = m.M11;
                sy = m.M22;
            }

            double screenLeft = b.Left * sx;
            double screenTop = b.Top * sy;
            double screenWidth = b.Width * sx;
            double screenHeight = b.Height * sy;

            double w = ActualWidth > 0 ? ActualWidth : 200;
            double h = ActualHeight > 0 ? ActualHeight : 34;
            double m2 = s.Margin;

            double x = s.Anchor switch
            {
                OverlayAnchor.TopLeft or OverlayAnchor.MiddleLeft or OverlayAnchor.BottomLeft
                    => screenLeft + m2,
                OverlayAnchor.TopRight or OverlayAnchor.MiddleRight or OverlayAnchor.BottomRight
                    => screenLeft + screenWidth - w - m2,
                _ => screenLeft + (screenWidth - w) / 2
            };

            double y = s.Anchor switch
            {
                OverlayAnchor.TopLeft or OverlayAnchor.TopCenter or OverlayAnchor.TopRight
                    => screenTop + m2,
                OverlayAnchor.BottomLeft or OverlayAnchor.BottomCenter or OverlayAnchor.BottomRight
                    => screenTop + screenHeight - h - m2,
                _ => screenTop + (screenHeight - h) / 2
            };

            Left = x;
            Top = y;
        }

        protected override void OnClosed(EventArgs e)
        {
            _pollTimer.Stop();
            base.OnClosed(e);
        }
    }
}