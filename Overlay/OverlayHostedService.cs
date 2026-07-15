using System.Windows;
using System.Windows.Threading;
using BestInScript.API.Engine;
using BestInScript.API.Models;
using BestInScript.API.Persistence;
using BestInScript.API.Services;

// Application exists in both WPF and WinForms; WinForms is pulled in globally
// (UseWindowsForms=true) for the Screen API, so alias to WPF here.
using Application = System.Windows.Application;

namespace BestInScript.API.Overlay
{
    /// <summary>
    /// Spins up a dedicated STA thread that hosts the WPF
    /// <see cref="OverlayWindow"/>. Pumps WPF's dispatcher loop alongside
    /// the ASP.NET Core host, and tears it down on shutdown.
    /// </summary>
    public sealed class OverlayHostedService : IHostedService
    {
        private readonly HotkeyEngine _engine;
        private readonly OverlaySettingsStore _store;
        private readonly EventScheduleService _events;
        private readonly OverlayEditModeSignal _editSignal;
        private readonly BuildCardSignal _buildSignal;
        private readonly IBuildCardRepository _cards;
        private readonly ILogger<OverlayHostedService> _logger;
        private readonly ILogger<BuildCardWindow> _cardLogger;

        private Thread? _uiThread;
        private Dispatcher? _dispatcher;
        private OverlayWindow? _window;
        private BuildCardWindow? _buildWindow;

        public OverlayHostedService(
            HotkeyEngine engine,
            OverlaySettingsStore store,
            EventScheduleService events,
            OverlayEditModeSignal editSignal,
            BuildCardSignal buildSignal,
            IBuildCardRepository cards,
            ILogger<OverlayHostedService> logger,
            ILogger<BuildCardWindow> cardLogger)
        {
            _engine = engine;
            _store = store;
            _events = events;
            _editSignal = editSignal;
            _buildSignal = buildSignal;
            _cards = cards;
            _logger = logger;
            _cardLogger = cardLogger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows())
            {
                _logger.LogInformation("Overlay disabled: not running on Windows.");
                return Task.CompletedTask;
            }

            var ready = new ManualResetEventSlim(false);

            _uiThread = new Thread(() =>
            {
                try
                {
                    // A WPF Application is needed so resource lookups and
                    // OnExplicitShutdown semantics work correctly.
                    var app = new Application
                    {
                        ShutdownMode = ShutdownMode.OnExplicitShutdown
                    };

                    _window = new OverlayWindow(_engine, _events, _store.Get());

                    // Second window on the same STA thread/dispatcher: the build-guide
                    // panel. Starts hidden; the cycle hotkey brings it up.
                    _buildWindow = new BuildCardWindow(_cards, _store.Get(), _cardLogger);

                    _dispatcher = app.Dispatcher;

                    // Push live settings changes onto the UI thread.
                    _store.Changed += OnSettingsChanged;

                    // Drag-to-position: web arms edit mode, the pill reports the
                    // committed position back for us to persist.
                    _editSignal.EnterRequested += OnEnterEditRequested;
                    _window.PositionCommitted += OnPositionCommitted;

                    // Build panel: engine raises these from the hook thread.
                    _buildSignal.CycleRequested += OnBuildCycleRequested;
                    _buildSignal.HideRequested += OnBuildHideRequested;
                    _buildSignal.CardsChanged += OnBuildCardsChanged;
                    _buildSignal.EnterEditRequested += OnBuildEnterEditRequested;
                    _buildWindow.PositionCommitted += OnBuildPositionCommitted;

                    ready.Set();
                    app.Run(_window);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Overlay UI thread crashed.");
                    ready.Set(); // unblock caller
                }
            })
            {
                IsBackground = true,
                Name = "BestInScript Overlay UI"
            };

            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();

            // Wait briefly so the dispatcher is available for early calls,
            // but don't block startup if WPF takes its time.
            ready.Wait(TimeSpan.FromSeconds(2));
            _logger.LogInformation("Overlay UI started.");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _store.Changed -= OnSettingsChanged;
                _editSignal.EnterRequested -= OnEnterEditRequested;
                _buildSignal.CycleRequested -= OnBuildCycleRequested;
                _buildSignal.HideRequested -= OnBuildHideRequested;
                _buildSignal.CardsChanged -= OnBuildCardsChanged;
                _buildSignal.EnterEditRequested -= OnBuildEnterEditRequested;
                if (_window != null) _window.PositionCommitted -= OnPositionCommitted;
                if (_buildWindow != null) _buildWindow.PositionCommitted -= OnBuildPositionCommitted;
                _dispatcher?.InvokeAsync(() =>
                {
                    try { Application.Current?.Shutdown(); } catch { /* already gone */ }
                });
                _uiThread?.Join(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Overlay shutdown raised.");
            }
            return Task.CompletedTask;
        }

        private void OnSettingsChanged(OverlaySettings s)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;

            var window = _window;
            var buildWindow = _buildWindow;

            dispatcher.InvokeAsync(() =>
            {
                window?.ApplySettings(s);
                buildWindow?.ApplySettings(s);
            });
        }

        // ── Build panel: hook thread → WPF dispatcher ──────────────────────

        private void OnBuildCycleRequested() => OnBuildPanel(w => w.Cycle());

        private void OnBuildHideRequested() => OnBuildPanel(w => w.HidePanel());

        private void OnBuildCardsChanged() => OnBuildPanel(w => w.ReloadFromRepository());

        private void OnBuildEnterEditRequested() => OnBuildPanel(w => w.EnterEditMode());

        private void OnBuildPanel(Action<BuildCardWindow> action)
        {
            var dispatcher = _dispatcher;
            var window = _buildWindow;
            if (dispatcher == null || window == null) return;

            dispatcher.InvokeAsync(() => action(window));
        }

        // Build panel committed a dragged position (already on the UI thread). Persist it
        // to the panel's own block; the resulting Changed event re-applies it live.
        private void OnBuildPositionCommitted(int screenIndex, double x, double y)
        {
            var s = _store.Get();
            s.BuildPanel.ScreenIndex = screenIndex;
            s.BuildPanel.Anchor = OverlayAnchor.Custom;
            s.BuildPanel.PositionX = x;
            s.BuildPanel.PositionY = y;
            _store.Save(s);
        }

        // Web asked to reposition — marshal the toggle onto the UI thread.
        private void OnEnterEditRequested()
        {
            var dispatcher = _dispatcher;
            var window = _window;
            if (dispatcher == null || window == null) return;

            dispatcher.InvokeAsync(window.EnterEditMode);
        }

        // Pill committed a dragged position (already on the UI thread). Persist it;
        // the resulting Changed event re-applies it live.
        private void OnPositionCommitted(int screenIndex, double x, double y)
        {
            var s = _store.Get();
            s.ScreenIndex = screenIndex;
            s.Anchor = OverlayAnchor.Custom;
            s.PositionX = x;
            s.PositionY = y;
            _store.Save(s);
        }
    }
}