using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BestInScript.API.Engine;
using BestInScript.API.Models;
using BestInScript.API.Persistence;

// Disambiguation. UseWindowsForms=true implicitly imports System.Drawing and
// System.Windows.Forms, both of which collide with WPF on these type names.
using WinFormsScreen = System.Windows.Forms.Screen;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace BestInScript.API.Overlay
{
    /// <summary>
    /// Always-on-top, click-through panel showing one build-guide card — typically a
    /// crop of a build guide's paragon board or target gear, so the user stops
    /// alt-tabbing to a browser mid-session.
    ///
    /// The cycle hotkey walks: hidden → first card → next → … → hidden
    /// (<see cref="BuildCardCycleCalculator"/>). Emergency stop hides it outright.
    ///
    /// Passive by construction: it renders a picture and nothing else — no game reads,
    /// no synthetic input. It is separate from <see cref="OverlayWindow"/> because a
    /// full-size board would drag the status pill's rows around the screen.
    ///
    /// There is no poll timer here, unlike the pill: content changes only on a cycle
    /// press or a web-UI edit, so a big bitmap is composited once, not 5×/second.
    /// </summary>
    public partial class BuildCardWindow : Window
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

        // Amber card border while in drag-to-position edit mode (matches the pill).
        private static readonly Brush EditBorderBrush = Freeze(Color.FromRgb(0xFF, 0xC1, 0x07));

        // ── State ──────────────────────────────────────────────────────────
        private readonly IBuildCardRepository _repo;
        private BuildPanelConfig _config;

        private List<BuildCard> _cards = [];
        private int _index = BuildCardCycleCalculator.Hidden;

        // Decoded, frozen images keyed by card id + the width they were decoded at.
        // Cleared whenever the cards change (so a replaced image can't render stale); the
        // width is part of the key because bitmaps are decoded to their render size, and a
        // cache hit at a stale width would render soft after the panel is resized.
        private readonly Dictionary<(Guid Id, int Width), ImageSource> _imageCache = [];

        // ── Drag-to-position edit mode ─────────────────────────────────────
        private bool _editing;
        private Brush? _savedBorderBrush;
        private Thickness _savedThickness;

        /// <summary>
        /// Raised when the user commits a drag: (screenIndex, PositionX, PositionY),
        /// offsets relative to that screen's top-left in DIP. The hosted service
        /// persists it — this window never touches the settings store.
        /// </summary>
        public event Action<int, double, double>? PositionCommitted;

        public BuildCardWindow(IBuildCardRepository repo, OverlaySettings initialSettings)
        {
            InitializeComponent();
            _repo = repo;
            _config = initialSettings.BuildPanel ?? new BuildPanelConfig();

            // Create the HWND now rather than on first Show(). OnSourceInitialized applies
            // the click-through / no-activate ex-styles, and the panel starts hidden — so
            // without this the first cycle press would briefly flash a focusable window
            // that steals a click from the game.
            new WindowInteropHelper(this).EnsureHandle();

            SizeChanged += (_, __) => ApplyPosition();
            ApplySettings(initialSettings);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Click-through, topmost-tool, non-activating. Must run after the HWND exists.
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE,
                ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        // ── Public API used by the hosted service ──────────────────────────

        /// <summary>Apply a fresh settings snapshot. Disabling the panel hides it immediately.</summary>
        public void ApplySettings(OverlaySettings s)
        {
            _config = s.BuildPanel ?? new BuildPanelConfig();

            CardBorder.Opacity = _config.Opacity;
            TitleText.FontSize = _config.FontSize;
            NotesText.FontSize = Math.Max(8, _config.FontSize - 1);

            if (!_config.Enabled)
                _index = BuildCardCycleCalculator.Hidden;

            Render();
        }

        /// <summary>Advance one step: hidden → first card → next → … → hidden.</summary>
        public void Cycle()
        {
            if (!_config.Enabled) return;

            ReloadCards();
            _index = BuildCardCycleCalculator.Next(_index, _cards.Count);
            Render();
        }

        /// <summary>Take the panel down regardless of state (emergency stop).</summary>
        public void HidePanel()
        {
            _index = BuildCardCycleCalculator.Hidden;
            Render();
        }

        /// <summary>Cards were edited in the web UI: drop cached bitmaps and re-read, so an
        /// open panel reflects the edit instead of showing a deleted or replaced card.</summary>
        public void ReloadFromRepository()
        {
            _imageCache.Clear();
            ReloadCards();
            _index = BuildCardCycleCalculator.Clamp(_index, _cards.Count);
            Render();
        }

        // ── Rendering ──────────────────────────────────────────────────────

        private void ReloadCards()
        {
            try
            {
                _cards = _repo.GetAll();
            }
            catch
            {
                // A missing/corrupt build-cards.json must not take the overlay thread down.
                _cards = [];
            }
        }

        private void Render()
        {
            // Edit mode needs something on screen to grab, even with no card selected.
            if (_index == BuildCardCycleCalculator.Hidden && !_editing)
            {
                if (Visibility == Visibility.Visible) Hide();
                return;
            }

            var card = (_index >= 0 && _index < _cards.Count) ? _cards[_index] : null;

            TitleText.Text = card?.Name ?? "Build panel";

            var scale = Math.Clamp(card?.Scale is > 0 ? card.Scale : 1.0, 0.25, 2.0);
            CardImage.MaxWidth = _config.MaxWidth * scale;
            CardImage.MaxHeight = _config.MaxHeight * scale;
            CardImage.Source = card is null ? null : ResolveImage(card);
            CardImage.Visibility = CardImage.Source is null ? Visibility.Collapsed : Visibility.Visible;

            var notes = card?.Notes;
            NotesText.Text = notes ?? "";
            NotesText.MaxWidth = _config.MaxWidth * scale;
            NotesText.Visibility = string.IsNullOrWhiteSpace(notes)
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (Visibility != Visibility.Visible) Show();
            ApplyPosition();
        }

        /// <summary>
        /// Decode a card's image, downscaled at decode time to the size it will actually
        /// render at, and cache the frozen result. Null when the card has no usable image.
        /// </summary>
        private ImageSource? ResolveImage(BuildCard card)
        {
            var scale = Math.Clamp(card.Scale > 0 ? card.Scale : 1.0, 0.25, 2.0);
            var target = (int)Math.Max(1, _config.MaxWidth * scale);

            if (_imageCache.TryGetValue((card.Id, target), out var cached))
                return cached;

            var path = _repo.ImagePath(card);
            if (path is null) return null;

            try
            {
                // Read the header first so we only downscale — never blow a small
                // image up to the panel's max width.
                int sourceWidth;
                using (var probe = File.OpenRead(path))
                {
                    var decoder = BitmapDecoder.Create(
                        probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    sourceWidth = decoder.Frames[0].PixelWidth;
                }

                var bytes = File.ReadAllBytes(path);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                // A MemoryStream + OnLoad means no lingering file lock, so the web UI can
                // replace the image while the panel holds it.
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                if (sourceWidth > target) bmp.DecodePixelWidth = target;
                bmp.EndInit();
                bmp.Freeze();

                _imageCache[(card.Id, target)] = bmp;
                return bmp;
            }
            catch
            {
                // Unreadable/corrupt image: show the card's name with no picture rather
                // than crashing the overlay thread.
                return null;
            }
        }

        // ── Drag-to-position edit mode (mirrors OverlayWindow) ─────────────

        /// <summary>
        /// Enter edit mode: drop click-through + no-activate so the panel receives the
        /// mouse, reveal the drag chrome, and force it visible so there is always
        /// something to grab. Idempotent.
        /// </summary>
        public void EnterEditMode()
        {
            if (_editing) return;

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, ex & ~(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE));
            }

            _savedBorderBrush = CardBorder.BorderBrush;
            _savedThickness = CardBorder.BorderThickness;
            CardBorder.BorderBrush = EditBorderBrush;
            CardBorder.BorderThickness = new Thickness(2);
            EditChrome.Visibility = Visibility.Visible;

            // Show the first card while positioning, so the user drags something
            // the size of what they'll actually see.
            ReloadCards();
            if (_index == BuildCardCycleCalculator.Hidden && _cards.Count > 0)
                _index = 0;

            _editing = true;
            Render();

            // One-shot anchor apply. ApplyPosition no-ops while editing so a mid-drag
            // SizeChanged can't yank the window out from under the cursor — but on entry
            // the panel must start at its configured spot, not at (0,0) where it would
            // sit if the cycle key had never shown it this session.
            ApplyPosition(force: true);
            Activate();
        }

        /// <summary>
        /// Leave edit mode and restore click-through. Both ✓ and ✕ land here: the panel
        /// goes back to hidden and <see cref="Render"/> re-anchors from the persisted
        /// config, so cancelling reverts on its own and committing is superseded by the
        /// settings round-trip that <see cref="PositionCommitted"/> kicks off.
        /// </summary>
        private void ExitEditMode()
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

            // Positioning is over — leave the screen clear until the user asks for a card.
            _index = BuildCardCycleCalculator.Hidden;
            Render();
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
            // Read the position before exiting — ExitEditMode re-anchors the window.
            var commit = ComputeCommit();
            ExitEditMode();
            if (commit is { } c) PositionCommitted?.Invoke(c.Index, c.X, c.Y);
        }

        private void EditCancel_Click(object sender, RoutedEventArgs e) => ExitEditMode();

        /// <summary>
        /// Translate the panel's current top-left into (screenIndex, relative X, relative Y)
        /// against whichever screen holds its center. Null if no screens.
        /// </summary>
        private (int Index, double X, double Y)? ComputeCommit()
        {
            var rects = ScreenRects();
            if (rects.Count == 0) return null;

            double w = ActualWidth > 0 ? ActualWidth : 400;
            double h = ActualHeight > 0 ? ActualHeight : 300;

            int idx = OverlayPositionCalculator.ScreenIndexAt(
                rects, Left + w / 2, Top + h / 2, fallback: 0);
            var (relX, relY) = OverlayPositionCalculator.ToRelative(rects[idx], Left, Top);
            return (idx, relX, relY);
        }

        // ── Positioning ────────────────────────────────────────────────────

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

        /// <summary>All screen bounds converted from physical pixels to DIP.</summary>
        private List<OverlayPositionCalculator.ScreenRect> ScreenRects()
        {
            var screens = WinFormsScreen.AllScreens;
            var (sx, sy) = DeviceScale();
            var rects = new List<OverlayPositionCalculator.ScreenRect>(screens.Length);
            foreach (var s in screens)
            {
                var b = s.Bounds;
                rects.Add(new OverlayPositionCalculator.ScreenRect(
                    b.Left * sx, b.Top * sy, b.Width * sx, b.Height * sy));
            }
            return rects;
        }

        /// <summary>
        /// Re-anchor the panel from its config. While edit mode is on the drag owns the
        /// position, so this no-ops unless <paramref name="force"/> — otherwise every
        /// SizeChanged mid-drag would yank the window out from under the cursor.
        /// </summary>
        private void ApplyPosition(bool force = false)
        {
            if (_editing && !force) return;

            var screens = WinFormsScreen.AllScreens;
            if (screens.Length == 0) return;

            int idx = (_config.ScreenIndex >= 0 && _config.ScreenIndex < screens.Length)
                ? _config.ScreenIndex
                : Array.FindIndex(screens, s =>
                      WinFormsScreen.PrimaryScreen != null &&
                      s.DeviceName == WinFormsScreen.PrimaryScreen.DeviceName);
            if (idx < 0) idx = 0;

            var rects = ScreenRects();
            if (idx >= rects.Count) return;
            var rect = rects[idx];

            double w = ActualWidth > 0 ? ActualWidth : 400;
            double h = ActualHeight > 0 ? ActualHeight : 300;

            var (x, y) = _config.Anchor == OverlayAnchor.Custom
                ? OverlayPositionCalculator.ToAbsoluteClamped(rect, _config.PositionX, _config.PositionY, w, h)
                : OverlayPositionCalculator.AnchoredTopLeft(rect, _config.Anchor, _config.Margin, w, h);

            Left = x;
            Top = y;
        }

        private static Brush Freeze(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
