using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
                if (_settings.HideWhenIdle)
                {
                    if (Visibility == Visibility.Visible) Hide();
                    return;
                }
                rows.Add(new SimpleRow(IdleDot, Brushes.White, "BestInScript · idle"));
            }

            ApplyRows(rows);

            if (_settings.Enabled && Visibility != Visibility.Visible) Show();
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
