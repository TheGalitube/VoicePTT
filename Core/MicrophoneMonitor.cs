using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoicePTT.Core;

public sealed record DeviceOption(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class MicrophoneMonitor : IDisposable
{
    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00AA00389B71");
    private MMDeviceEnumerator? _enumerator;
    private WasapiCapture? _capture;
    private volatile float _levelDb = -90;
    private bool _stopping;

    public event Action<Exception?>? CaptureStopped;
    public float LevelDb => _levelDb;

    public static List<DeviceOption> ListDevices()
    {
        var result = new List<DeviceOption> { new("", "Windows default microphone") };
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            result.Add(new DeviceOption(device.ID, device.FriendlyName));
        return result;
    }

    public void Start(string deviceId)
    {
        Stop();
        _stopping = false;
        _enumerator = new MMDeviceEnumerator();
        var device = string.IsNullOrWhiteSpace(deviceId)
            ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
            : _enumerator.GetDevice(deviceId);
        _capture = new WasapiCapture(device);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        try { _capture.StartRecording(); }
        catch { Stop(); throw; }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var format = _capture?.WaveFormat;
        if (format is null || e.BytesRecorded == 0) return;
        var bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample is < 2 or > 4) return;
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                      format is WaveFormatExtensible ext && ext.SubFormat == FloatSubFormat;
        var count = e.BytesRecorded / bytesPerSample;
        if (count == 0) return;
        double sum = 0;
        for (var i = 0; i < count; i++)
        {
            var offset = i * bytesPerSample;
            double sample;
            if (isFloat && bytesPerSample == 4)
                sample = BitConverter.ToSingle(e.Buffer, offset);
            else if (bytesPerSample == 2)
                sample = BitConverter.ToInt16(e.Buffer, offset) / 32768.0;
            else if (bytesPerSample == 3)
            {
                var raw = e.Buffer[offset] | e.Buffer[offset + 1] << 8 | e.Buffer[offset + 2] << 16;
                if ((raw & 0x800000) != 0) raw |= unchecked((int)0xFF000000);
                sample = raw / 8388608.0;
            }
            else sample = BitConverter.ToInt32(e.Buffer, offset) / 2147483648.0;
            sum += sample * sample;
        }
        _levelDb = (float)Math.Clamp(20 * Math.Log10(Math.Max(Math.Sqrt(sum / count), 1e-7)), -90, 0);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (!_stopping) CaptureStopped?.Invoke(e.Exception);
    }

    public void Stop()
    {
        _stopping = true;
        var capture = _capture;
        _capture = null;
        if (capture is not null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            try { capture.StopRecording(); } catch { /* Device may already be gone. */ }
            capture.Dispose();
        }
        _enumerator?.Dispose();
        _enumerator = null;
        _levelDb = -90;
    }

    public void Dispose() => Stop();
}
