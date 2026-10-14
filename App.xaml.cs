using System.Windows;

namespace VoicePTT;

public partial class App : System.Windows.Application
{
    private const string InstanceName = @"Local\VoicePTT-7E83A932-79D2-4C5A-843A-E6CB6CC88E61";
    private const string ShowName = @"Local\VoicePTT-Show-7E83A932-79D2-4C5A-843A-E6CB6CC88E61";
    private Mutex? _instance;
    private EventWaitHandle? _showEvent;
    private volatile bool _exiting;
    private bool _ownsInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new Mutex(true, InstanceName, out var firstInstance);
        _ownsInstance = firstInstance;
        if (!firstInstance)
        {
            try { using var signal = EventWaitHandle.OpenExisting(ShowName); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName);
        base.OnStartup(e);
        _ = Task.Run(() =>
        {
            while (!_exiting)
            {
                try
                {
                    if (_showEvent?.WaitOne(1000) == true)
                        Dispatcher.BeginInvoke(() => (MainWindow as VoicePTT.MainWindow)?.ShowWindow());
                }
                catch (ObjectDisposedException) { break; }
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        _showEvent?.Dispose();
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
