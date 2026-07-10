using System.Runtime.Versioning;

namespace BestInScript.API.Services
{
    /// <summary>
    /// Single-instance gate for the whole app (BACKLOG 6.2).
    ///
    /// <para>The app is a headless web + tray process that binds a fixed Kestrel port, so a
    /// second launch would otherwise crash on a port-bind conflict. This guard lets only the
    /// first launch become the <see cref="IsPrimary">primary</see>; a later launch signals the
    /// primary to surface its web UI and then exits before the web host is built.</para>
    ///
    /// <para>IPC uses <c>Local\</c>-namespaced named kernel objects (a <see cref="Mutex"/> as the
    /// instance gate plus an auto-reset <see cref="EventWaitHandle"/> as a one-way "come to front"
    /// signal). The <c>Local\</c> prefix scopes them to the current interactive session — shared
    /// across elevated/non-elevated processes in that session, and requiring no special
    /// privileges (unlike <c>Global\</c>).</para>
    /// </summary>
    public sealed class SingleInstanceGuard : IDisposable
    {
        private const string MutexName = @"Local\BestInScript.SingleInstance";
        private const string ActivateEventName = @"Local\BestInScript.Activate";

        private readonly Mutex? _mutex;
        private EventWaitHandle? _activateEvent;
        private Thread? _listenerThread;
        private volatile bool _listening;
        private bool _disposed;

        /// <summary>True if this process is the first (owning) instance.</summary>
        public bool IsPrimary { get; }

        private SingleInstanceGuard(Mutex? mutex, bool isPrimary)
        {
            _mutex = mutex;
            IsPrimary = isPrimary;
        }

        /// <summary>
        /// Attempt to become the primary instance. If another instance already owns the gate,
        /// signal it to surface its UI and return a non-primary guard (the caller should exit).
        /// </summary>
        public static SingleInstanceGuard Acquire()
        {
            // Named kernel objects are Windows-only; elsewhere every launch is "primary".
            if (!OperatingSystem.IsWindows())
                return new SingleInstanceGuard(null, isPrimary: true);

            return AcquireWindows();
        }

        [SupportedOSPlatform("windows")]
        private static SingleInstanceGuard AcquireWindows()
        {
            var mutex = new Mutex(initiallyOwned: false, MutexName, out bool createdNew);

            bool owns = createdNew;
            if (!owns)
            {
                try
                {
                    owns = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    // A previous instance crashed without releasing — we now own it.
                    owns = true;
                }
            }

            if (!owns)
            {
                // Another instance is live: poke it to open its UI, then we bow out.
                try
                {
                    if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var evt))
                    {
                        using (evt)
                            evt.Set();
                    }
                }
                catch
                {
                    // Best effort — the primary is up regardless; failing to signal it just
                    // means no browser pops. Never let this crash the exiting process.
                }

                mutex.Dispose();
                return new SingleInstanceGuard(null, isPrimary: false);
            }

            return new SingleInstanceGuard(mutex, isPrimary: true);
        }

        /// <summary>
        /// Primary only: run <paramref name="onActivate"/> whenever a later launch signals us.
        /// No-op on non-primary instances or non-Windows.
        /// </summary>
        public void ListenForActivation(Action onActivate)
        {
            if (!IsPrimary || !OperatingSystem.IsWindows() || _listening)
                return;

            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            _listening = true;

            _listenerThread = new Thread(() =>
            {
                while (_listening)
                {
                    try
                    {
                        // Short timeout so Dispose can unwind the loop promptly.
                        if (_activateEvent.WaitOne(500) && _listening)
                            onActivate();
                    }
                    catch
                    {
                        break;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "BIS_SingleInstanceListener"
            };
            _listenerThread.Start();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            // Stop the listener loop and wake it so it exits its WaitOne promptly.
            _listening = false;
            try { _activateEvent?.Set(); } catch { /* already gone */ }
            _listenerThread?.Join(TimeSpan.FromSeconds(1));
            _activateEvent?.Dispose();

            try { _mutex?.ReleaseMutex(); } catch { /* not owned / already released */ }
            _mutex?.Dispose();
        }
    }
}
