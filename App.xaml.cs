using System.Threading;
using System.Windows;

namespace FortniteTweakTool;

/// <summary>
/// Stellt sicher, dass Rage Tweaks nur einmal gleichzeitig läuft. Startet der Benutzer die App ein zweites Mal,
/// wird stattdessen das bereits offene Fenster nach vorne geholt und die zweite Instanz beendet sich sofort.
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\RageTweaks.SingleInstance.v1";
    private const string ActivateEventName = @"Local\RageTweaks.Activate.v1";

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private volatile bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);

        // Beim Neustart als Administrator läuft die alte Instanz noch kurz: auf ihr Ende warten statt sich zu beenden.
        if (!createdNew && e.Args.Contains("--wait-previous"))
        {
            try { createdNew = _mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { createdNew = true; }
        }

        if (!createdNew)
        {
            // Es läuft schon eine Instanz: ihr ein Signal schicken und sofort beenden (kein Fenster, kein Flackern).
            try { using var ev = EventWaitHandle.OpenExisting(ActivateEventName); ev.Set(); } catch { }
            try { _mutex.Dispose(); } catch { }
            _mutex = null;
            Shutdown();
            return;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        var listener = new Thread(() =>
        {
            try
            {
                while (!_exiting && _activateEvent.WaitOne())
                {
                    if (_exiting) break;
                    Dispatcher.BeginInvoke(new Action(ActivateMainWindow));
                }
            }
            catch { }
        })
        { IsBackground = true, Name = "RageTweaks.Activate" };
        listener.Start();

        base.OnStartup(e);
    }

    private void ActivateMainWindow()
    {
        var w = MainWindow;
        if (w == null) return;
        if (!w.IsVisible) w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        // Kurzer Trick, damit Windows das Fenster wirklich nach vorne holt; der Benutzerwert für "immer im Vordergrund" bleibt erhalten.
        bool was = w.Topmost;
        w.Topmost = true;
        w.Topmost = was;
        w.Focus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        try { _activateEvent?.Set(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
