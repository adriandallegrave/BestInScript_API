using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using BestInScript.API.Engine;
using BestInScript.API.Models;
using BestInScript.API.Persistence;
using BestInScript.API.Services;

// Disambiguation. UseWindowsForms=true implicitly imports System.Drawing and
// System.Windows.Forms, both of which collide with WPF on these type names.
// Aliasing each one to the WPF (Media / Controls) version makes the
// short names in this file refer to WPF without per-usage qualification.
using WinFormsScreen = System.Windows.Forms.Screen;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;

namespace BestInScript.API.Overlay
{
    /// <summary>
    /// Tiny always-on-top, click-through status panel that shows one row per
    /// script the user has marked "ShowInOverlay". Driven in-process by polling
    /// <see cref="HotkeyEngine.GetStatus"/> every 200 ms.
    ///
    /// Row contents:
    ///   • Blind-loop script (no PixelTrigger): green dot + "Name · ON".
    ///   • Pixel-triggered script: dot + state ("READY", "waiting", "unreadable").
    ///
    /// When no script is eligible to display, falls back to the legacy "idle"
    /// row, or hides entirely if <see cref="OverlaySettings.HideWhenIdle"/>.
    /// </summary>
    public partial class OverlayWindow : Window
    {
        // ── Win32 (click-through, no-activate) ─────────────────────────────
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
        private readonly EventScheduleService _events;
        private readonly DispatcherTimer _pollTimer;
        private OverlaySettings _settings;

        // Cached, frozen brushes for the status dots.
        private static readonly Brush ActiveDot = Freeze(Color.FromRgb(0x3D, 0xDC, 0x84)); // green – running / READY
        private static readonly Brush WaitingDot = Freeze(Color.FromRgb(0xFF, 0xAA, 0x55)); // orange – watching / waiting
        private static readonly Brush ErrorDot = Freeze(Color.FromRgb(0xE8, 0x52, 0x52)); // red    – screen unreadable
        private static readonly Brush IdleDot = Freeze(Color.FromRgb(0x77, 0x77, 0x77)); // grey   – idle fallback

        // Alarm blink flashes a countdown between its normal color and this red.
        private static readonly Brush AlarmBrush = Freeze(Color.FromRgb(0xFF, 0x4D, 0x4D));

        // Amber card border while in drag-to-position edit mode.
        private static readonly Brush EditBorderBrush = Freeze(Color.FromRgb(0xFF, 0xC1, 0x07));

        // Shared so we don't allocate a FontFamily per row per refresh.
        private static readonly FontFamily SegoeUiFont = new("Segoe UI");

        // ── Row model ──────────────────────────────────────────────────────
        // A displayable row is either a "simple" dot+label line (scripts, presets,
        // idle) or an "event" line laid out in three shared-size columns
        // (name / zone / countdown) with an optional blinking countdown.
        private abstract record OvRow;
        private sealed record SimpleRow(Brush Dot, Brush Label, string Text) : OvRow;
        private sealed record EventRow(string Name, string Zone, string Time, Brush Label, Brush TimeBrush) : OvRow;

        private static Brush Freeze(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // Per-entry accent brushes, cached so we reuse one frozen brush per color.
        // Only touched from Refresh (WPF dispatcher thread), so no lock needed.
        private static readonly Dictionary<string, Brush> _labelBrushes = new();

        /// <summary>
        /// Resolve a per-entry label brush from an optional [R,G,B] accent.
        /// Null/malformed falls back to the default white label.
        /// </summary>
        private static Brush ResolveLabelBrush(int[]? rgb)
        {
            if (rgb is not { Length: 3 }) return Brushes.White;

            int r = rgb[0], g = rgb[1], b = rgb[2];
            if (r < 0 || r > 255 || g < 0 || g > 255 || b < 0 || b > 255)
                return Brushes.White;

            var key = $"{r},{g},{b}";
            if (!_labelBrushes.TryGetValue(key, out var brush))
            {
                brush = Freeze(Color.FromRgb((byte)r, (byte)g, (byte)b));
                _labelBrushes[key] = brush;
            }
            return brush;
        }

        /// <summary>Icon prefix (with trailing space) for a row, or "" when unset.</summary>
        private static string IconPrefix(string? icon)
            => string.IsNullOrWhiteSpace(icon) ? "" : icon.Trim() + " ";

        // Used to skip rebuilding the row list when nothing visible changed.
        private string _lastSignature = "";

        // ── Drag-to-position edit mode ─────────────────────────────────────
        private bool _editing;
        private Brush? _savedBorderBrush;
        private Thickness _savedThickness;

        /// <summary>
        /// Raised when the user commits a drag: (screenIndex, PositionX, PositionY)
        /// with the offsets relative to that screen's top-left in DIP. The hosted
        /// service persists it so the window itself never touches the store.
        /// </summary>
        public event Action<int, double, double>? PositionCommitted;

        // ── Construction ───────────────────────────────────────────────────
        public OverlayWindow(HotkeyEngine engine, EventScheduleService events, OverlaySettings initialSettings)
        {
            InitializeComponent();
            _engine = engine;
            _events = events;
            _settings = initialSettings;

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

        /// <summary>Apply a fresh settings snapshot (visibility, opacity, font size, position).</summary>
        public void ApplySettings(OverlaySettings s)
        {
            _settings = s;
            CardBorder.Opacity = s.Opacity;

            // Force a rebuild so the new font size lands on the row TextBlocks
            // (we build them in code, no XAML binding).
            _lastSignature = "";
            Refresh();

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

        // ── Drag-to-position edit mode ─────────────────────────────────────

        /// <summary>
        /// Enter edit mode: drop click-through + no-activate so the pill receives
        /// the mouse, reveal the drag chrome, and force it visible so there is
        /// always something to grab. Idempotent.
        /// </summary>
        public void EnterEditMode()
        {
            if (_editing) return;
            _editing = true;

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                // Keep WS_EX_LAYERED (opacity) + WS_EX_TOOLWINDOW; drop transparency
                // + no-activate so clicks land and the ✓/✕ buttons work.
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, ex & ~(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE));
            }

            _savedBorderBrush = CardBorder.BorderBrush;
            _savedThickness = CardBorder.BorderThickness;
            CardBorder.BorderBrush = EditBorderBrush;
            CardBorder.BorderThickness = new Thickness(2);
            EditChrome.Visibility = Visibility.Visible;

            if (Visibility != Visibility.Visible) Show();
            _lastSignature = "";  // force a rebuild so a row exists even when idle
            Refresh();
            Activate();
        }

        /// <summary>
        /// Leave edit mode, restoring click-through. When <paramref name="restore"/>
        /// is true (Cancel), re-applies the persisted settings so the pill jumps
        /// back; on commit it is false so the pill stays put until the saved
        /// settings round-trip through <see cref="ApplySettings"/>.
        /// </summary>
        private void ExitEditMode(bool restore)
        {
            if (!_editing) return;
            _editing = false;

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
            }

            EditChrome.Visibility = Visibility.Collapsed;
            if (_savedBorderBrush != null) CardBorder.BorderBrush = _savedBorderBrush;
            CardBorder.BorderThickness = _savedThickness;

            if (restore) ApplySettings(_settings);
        }

        private void CardBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_editing) return;
            // The ✓/✕ Buttons mark their own MouseLeftButtonDown handled, so this
            // only fires for the draggable card surface.
            try { DragMove(); } catch { /* not the primary button / already released */ }
        }

        private void EditSave_Click(object sender, RoutedEventArgs e)
        {
            var commit = ComputeCommit();
            ExitEditMode(restore: false);
            if (commit is { } c) PositionCommitted?.Invoke(c.Index, c.X, c.Y);
        }

        private void EditCancel_Click(object sender, RoutedEventArgs e) => ExitEditMode(restore: true);

        /// <summary>
        /// Translate the pill's current top-left into (screenIndex, relative X, relative Y)
        /// against whichever screen holds the pill's center. Null if no screens.
        /// </summary>
        private (int Index, double X, double Y)? ComputeCommit()
        {
            var screens = WinFormsScreen.AllScreens;
            if (screens.Length == 0) return null;

            var (sx, sy) = DeviceScale();
            var rects = new List<OverlayPositionCalculator.ScreenRect>(screens.Length);
            foreach (var s in screens)
            {
                var b = s.Bounds;
                rects.Add(new OverlayPositionCalculator.ScreenRect(
                    b.Left * sx, b.Top * sy, b.Width * sx, b.Height * sy));
            }

            double w = ActualWidth > 0 ? ActualWidth : 200;
            double h = ActualHeight > 0 ? ActualHeight : 34;

            int idx = OverlayPositionCalculator.ScreenIndexAt(
                rects, Left + w / 2, Top + h / 2, fallback: 0);
            var (relX, relY) = OverlayPositionCalculator.ToRelative(rects[idx], Left, Top);
            return (idx, relX, relY);
        }

        // ── Status refresh ─────────────────────────────────────────────────

        private void Refresh()
        {
            List<ScriptStatus> statuses;
            List<PresetStatus> presets;
            try
            {
                statuses = _engine.GetStatus().ToList();
                presets  = _engine.GetPresetStatus().ToList();
            }
            catch
            {
                // Engine not ready / disposed — keep last state.
                return;
            }

            var rows = new List<OvRow>();

            // Active presets first — shown like blind-loop scripts (green dot +
            // "Name · ON"). Presets render above scripts so the overlay reads
            // top-down as "group then members".
            foreach (var p in presets.Where(p => p.IsActive && p.ShowInOverlay))
                rows.Add(new SimpleRow(
                    ActiveDot,
                    ResolveLabelBrush(p.OverlayColor),
                    $"{IconPrefix(p.OverlayIcon)}[{p.Name}] · ON"));

            // Scripts the user opted into via ShowInOverlay AND that are toggled
            // on. Both kinds (pixel + blind) share the list; BuildRow picks style.
            foreach (var s in statuses.Where(s => s.IsRunning && s.ShowInOverlay))
                rows.Add(BuildRow(s));

            // Diablo 4 event timers (world boss / helltide / legion), below the
            // script/preset rows.
            AppendEventRows(rows);

            if (rows.Count == 0)
            {
                // While editing, never hide — the user needs a pill to grab.
                if (_settings.HideWhenIdle && !_editing)
                {
                    if (Visibility == Visibility.Visible) Hide();
                    return;
                }
                rows.Add(new SimpleRow(IdleDot, Brushes.White, "BestInScript · idle"));
            }

            ApplyRows(rows);

            if ((_settings.Enabled || _editing) && Visibility != Visibility.Visible) Show();
        }

        /// <summary>
        /// Decide the dot + label for one displayable script.
        /// Pixel scripts get informational state; blind scripts get on/off.
        /// </summary>
        private static SimpleRow BuildRow(ScriptStatus s)
        {
            var label = ResolveLabelBrush(s.OverlayColor);
            var icon = IconPrefix(s.OverlayIcon);

            var (dot, word) = !s.HasPixelTrigger
                ? (ActiveDot, "ON")
                : s.PixelState switch
                {
                    PixelOverlayState.Ready => (ActiveDot, "READY"),
                    PixelOverlayState.Waiting => (WaitingDot, "waiting"),
                    PixelOverlayState.Unreadable => (ErrorDot, "unreadable"),
                    _ => (WaitingDot, "…")
                };

            return new SimpleRow(dot, label, $"{icon}{s.Name} · {word}");
        }

        /// <summary>
        /// Append the enabled D4 event rows. Countdowns come from the cached
        /// schedule via <see cref="EventScheduleCalculator"/>; a per-event alarm
        /// flashes the countdown when it's within the configured lead time.
        /// </summary>
        private void AppendEventRows(List<OvRow> rows)
        {
            if (!_settings.EventsEnabled) return;

            var now = DateTimeOffset.UtcNow;
            var snap = _events.GetSnapshot(now);
            if (!snap.HasData) return;

            long nowUnix = now.ToUnixTimeSeconds();
            // Blink phase flips every 500 ms → ~1 Hz flash. It's folded into the
            // row signature so ApplyRows rebuilds at that cadence while an alarm
            // is live (and stays static otherwise).
            bool blinkOn = (Environment.TickCount64 / 500) % 2 == 0;

            if (_settings.WorldBoss.Show && snap.Boss is { } wb)
                rows.Add(MakeEventRow(_settings.WorldBoss, wb.Name, wb.Zone, wb.StartUnix, nowUnix, blinkOn));

            if (_settings.Legion.Show && snap.LegionStartUnix is { } legionUnix)
                rows.Add(MakeEventRow(_settings.Legion, "Legion", "", legionUnix, nowUnix, blinkOn));

            if (_settings.Helltide.Show && snap.Helltide is { } ht)
                rows.Add(MakeEventRow(
                    _settings.Helltide, "Helltide", ht.Active ? "active" : "locked",
                    ht.TargetUnix, nowUnix, blinkOn));
        }

        private static EventRow MakeEventRow(
            EventOverlayConfig cfg, string name, string zone,
            long targetUnix, long nowUnix, bool blinkOn)
        {
            var label = ResolveLabelBrush(cfg.Color);
            string time = EventScheduleCalculator.FormatRemaining(targetUnix, nowUnix);

            long remaining = targetUnix - nowUnix;
            bool alarm = cfg.AlarmEnabled && remaining >= 0 && remaining <= cfg.AlarmLeadMinutes * 60L;
            var timeBrush = alarm && blinkOn ? AlarmBrush : label;

            return new EventRow(name, zone, time, label, timeBrush);
        }

        /// <summary>
        /// Rebuild the RowsPanel children only when the visible signature has
        /// actually changed — avoids 5/second flicker when nothing moved.
        /// </summary>
        private void ApplyRows(List<OvRow> rows)
        {
            var sig = string.Join("||", rows.Select(RowSignature)) + "@@fs=" + _settings.FontSize;
            if (sig == _lastSignature) return;
            _lastSignature = sig;

            RowsPanel.Children.Clear();
            foreach (var row in rows)
            {
                switch (row)
                {
                    case SimpleRow sr: RowsPanel.Children.Add(BuildSimpleRow(sr)); break;
                    case EventRow er: RowsPanel.Children.Add(BuildEventRow(er)); break;
                }
            }
        }

        private static string RowSignature(OvRow row) => row switch
        {
            SimpleRow sr => "S::" + BrushKey(sr.Dot) + "::" + BrushKey(sr.Label) + "::" + sr.Text,
            EventRow er => "E::" + BrushKey(er.Label) + "::" + BrushKey(er.TimeBrush)
                           + "::" + er.Name + "::" + er.Zone + "::" + er.Time,
            _ => ""
        };

        private static string BrushKey(Brush b) => b is SolidColorBrush sc ? sc.Color.ToString() : "";

        private UIElement BuildSimpleRow(SimpleRow r)
        {
            var rowPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 1, 0, 1)
            };
            rowPanel.Children.Add(new Ellipse
            {
                Width = 9,
                Height = 9,
                Margin = new Thickness(0, 1, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = r.Dot
            });
            rowPanel.Children.Add(MakeCell(r.Text, r.Label));
            return rowPanel;
        }

        // Event rows use three shared-size columns so name / zone / countdown line
        // up as a table across all event rows (see Grid.IsSharedSizeScope on RowsPanel).
        private UIElement BuildEventRow(EventRow r)
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "bisEvtName" });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "bisEvtZone" });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = MakeCell(r.Name, r.Label);
            name.Margin = new Thickness(0, 0, 14, 0);
            Grid.SetColumn(name, 0);

            var zone = MakeCell(r.Zone, r.Label);
            zone.Margin = new Thickness(0, 0, 14, 0);
            Grid.SetColumn(zone, 1);

            var time = MakeCell(r.Time, r.TimeBrush);
            Grid.SetColumn(time, 2);

            grid.Children.Add(name);
            grid.Children.Add(zone);
            grid.Children.Add(time);
            return grid;
        }

        private TextBlock MakeCell(string text, Brush brush) => new()
        {
            Text = text,
            Foreground = brush,
            FontFamily = SegoeUiFont,
            FontSize = _settings.FontSize,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        // ── Positioning ────────────────────────────────────────────────────

        /// <summary>
        /// Physical-pixel → DIP scale for this window. Uses the window's own HWND
        /// so it picks up the monitor it currently sits on; falls back to 1:1.
        /// </summary>
        private (double sx, double sy) DeviceScale()
        {
            var src = PresentationSource.FromVisual(this);
            if (src?.CompositionTarget != null)
            {
                var m = src.CompositionTarget.TransformFromDevice;
                return (m.M11, m.M22);
            }
            return (1.0, 1.0);
        }

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
            var (sx, sy) = DeviceScale();

            double screenLeft = b.Left * sx;
            double screenTop = b.Top * sy;
            double screenWidth = b.Width * sx;
            double screenHeight = b.Height * sy;

            double w = ActualWidth > 0 ? ActualWidth : 200;
            double h = ActualHeight > 0 ? ActualHeight : 34;
            double m2 = s.Margin;

            // Custom (dragged) position: offset from the target screen's top-left,
            // clamped so the pill can't be parked fully off-screen.
            if (s.Anchor == OverlayAnchor.Custom)
            {
                var rect = new OverlayPositionCalculator.ScreenRect(
                    screenLeft, screenTop, screenWidth, screenHeight);
                var (cx, cy) = OverlayPositionCalculator.ToAbsoluteClamped(
                    rect, s.PositionX, s.PositionY, w, h);
                Left = cx;
                Top = cy;
                return;
            }

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
