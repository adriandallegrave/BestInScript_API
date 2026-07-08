using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using BestInScript.API.Engine;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

// Application exists in both WPF and WinForms; WPF is pulled in globally
// (UseWPF=true) for the overlay, so alias to WinForms here.
using WinFormsApplication = System.Windows.Forms.Application;

namespace BestInScript.API.Tray
{
    /// <summary>
    /// Hosts a system-tray <see cref="NotifyIcon"/> on a dedicated STA thread
    /// with quick actions: open the web UI, stop all scripts, exit the app.
    /// Same thread pattern as <c>OverlayHostedService</c>.
    /// </summary>
    public sealed class TrayIconHostedService : IHostedService
    {
        private readonly HotkeyEngine _engine;
        private readonly IServer _server;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<TrayIconHostedService> _logger;

        private Thread? _uiThread;
        private SynchronizationContext? _syncContext;
        private NotifyIcon? _icon;

        public TrayIconHostedService(
            HotkeyEngine engine,
            IServer server,
            IHostApplicationLifetime lifetime,
            ILogger<TrayIconHostedService> logger)
        {
            _engine = engine;
            _server = server;
            _lifetime = lifetime;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows())
            {
                _logger.LogInformation("Tray icon disabled: not running on Windows.");
                return Task.CompletedTask;
            }

            var ready = new ManualResetEventSlim(false);

            _uiThread = new Thread(() =>
            {
                try
                {
                    SynchronizationContext.SetSynchronizationContext(
                        new WindowsFormsSynchronizationContext());
                    _syncContext = SynchronizationContext.Current;

                    _icon = BuildIcon();

                    ready.Set();
                    WinFormsApplication.Run();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Tray icon thread crashed.");
                    ready.Set(); // unblock caller
                }
            })
            {
                IsBackground = true,
                Name = "BIS_TrayThread"
            };

            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();

            // Wait briefly so the icon is up for early interaction,
            // but don't block startup if WinForms takes its time.
            ready.Wait(TimeSpan.FromSeconds(2));
            _logger.LogInformation("Tray icon started.");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _syncContext?.Post(_ =>
                {
                    try
                    {
                        // Dispose explicitly, otherwise a ghost icon lingers
                        // in the tray until the user hovers over it.
                        _icon?.Dispose();
                        WinFormsApplication.ExitThread();
                    }
                    catch { /* already gone */ }
                }, null);
                _uiThread?.Join(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tray icon shutdown raised.");
            }
            return Task.CompletedTask;
        }

        private NotifyIcon BuildIcon()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open UI", null, (_, _) => OpenUi());
            menu.Items.Add("Stop all scripts", null, (_, _) => StopAll());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => _lifetime.StopApplication());

            var icon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "BestInScript",
                ContextMenuStrip = menu,
                Visible = true
            };
            icon.DoubleClick += (_, _) => OpenUi();
            return icon;
        }

        private static Icon LoadIcon()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (exe is not null && Icon.ExtractAssociatedIcon(exe) is { } extracted)
                    return extracted;
            }
            catch { /* fall through to the stock icon */ }
            return SystemIcons.Application;
        }

        private void OpenUi()
        {
            var url = ResolveUiUrl();
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to open the web UI at {Url}.", url);
            }
        }

        private void StopAll()
        {
            try { _engine.StopAll(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Stop-all from tray failed."); }
        }

        /// <summary>
        /// Server addresses are only bound after all hosted services have
        /// started, so resolve lazily per click rather than in StartAsync.
        /// </summary>
        private string ResolveUiUrl()
        {
            var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;
            var address =
                addresses?.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                ?? addresses?.FirstOrDefault();

            if (string.IsNullOrEmpty(address))
                return "http://localhost:5000"; // Kestrel default for the published exe

            return address
                .Replace("//0.0.0.0", "//localhost")
                .Replace("//[::]", "//localhost")
                .Replace("//+", "//localhost")
                .Replace("//*", "//localhost");
        }
    }
}
