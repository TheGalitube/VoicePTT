using System.Diagnostics;
using System.Windows.Threading;

namespace VoicePTT.Core;

public sealed class PttEngine : IDisposable
{
    private readonly KeyboardControl _keyboard;
    private readonly MicrophoneMonitor _microphone = new();
    private readonly Dispatcher _dispatcher;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private AppSettings _settings;
    private long? _silenceSince;
    private long _tapStarted;
    private bool _tapHasSpeech;
    private bool _tapArmed;

    public bool IsActive { get; private set; }
    public bool IsTransmitting => _keyboard.IsOutputHeld;
    public float LevelDb => _microphone.LevelDb;
    public string StateLabel => !IsActive ? "OFFLINE" : IsTransmitting ? "TRANSMITTING" : "LISTENING";
    public string DetailText => !IsActive ? "Press Enable to start" : IsTransmitting ? "PTT key held" :
        _settings.Mode == "Hold-to-talk" ? "Hold your hotkey to talk" : "Waiting for voice";

    public event Action? StateChanged;
    public event Action<string>? Fault;

    public PttEngine(KeyboardControl keyboard, Dispatcher dispatcher, AppSettings settings)
    {
        _keyboard = keyboard;
        _dispatcher = dispatcher;
        _settings = settings;
        _microphone.CaptureStopped += exception => _dispatcher.BeginInvoke(() =>
        {
            if (!IsActive) return;
            Stop();
            Fault?.Invoke(exception?.Message ?? "The microphone stopped. Check its connection and enable VoicePTT again.");
        });
    }

    public void UpdateSettings(AppSettings settings) => _settings = settings;

    public void Toggle()
    {
        if (IsActive) Stop();
        else Start();
    }

    public void Start()
    {
        if (IsActive) return;
        try
        {
            if (_settings.Mode != "Hold-to-talk") _microphone.Start(_settings.DeviceId);
            IsActive = true;
            _tapArmed = false;
            _tapHasSpeech = false;
            _silenceSince = null;
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _microphone.Stop();
            Fault?.Invoke("Could not open the microphone: " + ex.Message);
        }
    }

    public void Stop()
    {
        IsActive = false;
        _tapArmed = false;
        _silenceSince = null;
        try { _keyboard.ReleaseOutput(); }
        catch (Exception ex) { Fault?.Invoke("Could not release the PTT key: " + ex.Message); }
        _microphone.Stop();
        StateChanged?.Invoke();
    }

    public void OnHotkeyPressed()
    {
        if (_settings.Mode == "Voice activity") { Toggle(); return; }
        if (!IsActive) Start();
        if (!IsActive) return;
        if (_settings.Mode == "Hold-to-talk") { Press(); return; }
        if (_tapArmed)
        {
            _tapArmed = false;
            Release();
            return;
        }
        _tapArmed = true;
        _tapHasSpeech = false;
        _tapStarted = _clock.ElapsedMilliseconds;
        _silenceSince = null;
        Press();
    }

    public void OnHotkeyReleased()
    {
        if (IsActive && _settings.Mode == "Hold-to-talk") Release();
    }

    public void Tick()
    {
        if (!IsActive || _settings.Mode == "Hold-to-talk") return;
        var level = _microphone.LevelDb;
        var now = _clock.ElapsedMilliseconds;
        var activation = _settings.ThresholdDb;
        var release = Math.Min(_settings.ReleaseThresholdDb, activation);
        var delay = Math.Clamp(_settings.ReleaseDelayMs, 0, 2000);

        if (_settings.Mode == "Voice activity")
        {
            if (!IsTransmitting)
            {
                if (level >= activation) { Press(); _silenceSince = null; }
            }
            else if (level >= release) _silenceSince = null;
            else if (QuietLongEnough(now, delay)) Release();
            return;
        }

        if (!_tapArmed) return;
        if (level >= activation) { _tapHasSpeech = true; _silenceSince = null; }
        if (!_tapHasSpeech)
        {
            if (now - _tapStarted >= _settings.TapWindowMs) { _tapArmed = false; Release(); }
        }
        else if (level >= release) _silenceSince = null;
        else if (QuietLongEnough(now, delay)) { _tapArmed = false; Release(); }
    }

    private bool QuietLongEnough(long now, int delay)
    {
        _silenceSince ??= now;
        return now - _silenceSince >= delay;
    }

    private void Press()
    {
        try { _keyboard.PressOutput(_settings.TargetVk); StateChanged?.Invoke(); }
        catch (Exception ex) { Stop(); Fault?.Invoke(ex.Message); }
    }

    private void Release()
    {
        _silenceSince = null;
        try { _keyboard.ReleaseOutput(); StateChanged?.Invoke(); }
        catch (Exception ex) { Stop(); Fault?.Invoke(ex.Message); }
    }

    public void Dispose()
    {
        Stop();
        _microphone.Dispose();
    }
}
