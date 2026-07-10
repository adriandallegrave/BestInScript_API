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
        private readonly ILogger<OverlayHostedService> _logger;

        private Thread? _uiThread;
        private Dispatcher? _dispatcher;
        private OverlayWindow? _window;

        public OverlayHostedService(
            HotkeyEngine engine,
            OverlaySettingsStore store,
            EventScheduleService events,
            OverlayEditModeSignal editSignal,
            ILogger<OverlayHostedService> logger)
        {
            _engine = engine;
            _store = store;
            _events = events;
            _editSignal = editSignal;
            _logger = logger;
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
                    _dispatcher = app.Dispatcher;

                    // Push live settings changes onto the UI thread.
                    _store.Changed += OnSettingsChanged;

                    // Drag-to-position: web arms edit mode, the pill reports the
                    // committed position back for us to persist.
                    _editSignal.EnterRequested += OnEnterEditRequested;
                    _window.PositionCommitted += OnPositionCommitted;

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
                if (_window != null) _window.PositionCommitted -= OnPositionCommitted;
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
            var window = _window;
            if (dispatcher == null || window == null) return;

            dispatcher.InvokeAsync(() => window.ApplySettings(s));
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