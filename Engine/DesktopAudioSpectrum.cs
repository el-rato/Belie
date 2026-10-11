using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace WallpaperProfiles.Engine;

// Loopback samples stay in memory and are discarded after spectrum analysis.
internal sealed class DesktopAudioSpectrum : IDisposable
{
    private readonly MMDeviceEnumerator _devices = new();
    private MMDevice? _device;
    private WasapiLoopbackCapture? _capture;
    private readonly SpectrumAnalyzer _analyzer = new();
    private volatile bool _stopped;
    private DateTime _nextDeviceCheck;
    public string? Error { get; private set; }

    public float[] Read()
    {
        if (DateTime.UtcNow >= _nextDeviceCheck)
        {
            _nextDeviceCheck = DateTime.UtcNow.AddSeconds(3);
            MMDevice? selected = null;
            try
            {
                selected = _devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (_capture == null || _stopped || selected.ID != _device?.ID)
                {
                    Stop();
                    _device = selected; selected = null;
                    _capture = new WasapiLoopbackCapture(_device) { WaveFormat = new WaveFormat(SpectrumAnalyzer.SampleRate, 16, 2) };
                    _capture.DataAvailable += Samples;
                    _capture.RecordingStopped += Stopped;
                    _stopped = false;
                    _capture.StartRecording();
                }
                Error = null;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
            {
                Stop();
                Error = "Audio unavailable · reconnect your speakers or headphones";
            }
            finally { selected?.Dispose(); }
        }
        return _analyzer.Read(Environment.TickCount64);
    }

    private void Samples(object? sender, WaveInEventArgs e) => _analyzer.AddPcm16Stereo(e.Buffer, e.BytesRecorded, Environment.TickCount64);
    private void Stopped(object? sender, StoppedEventArgs e) => _stopped = true;

    private void Stop()
    {
        if (_capture != null)
        {
            _capture.DataAvailable -= Samples; _capture.RecordingStopped -= Stopped;
            _capture.Dispose(); _capture = null;
        }
        _device?.Dispose(); _device = null;
        _analyzer.Clear();
    }

    public void Dispose() { Stop(); _devices.Dispose(); }
}

internal sealed class SpectrumAnalyzer
{
    internal const int SampleRate = 44100;
    internal const int BarCount = 56;
    private const int FftSize = 2048;
    private readonly object _gate = new();
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly float[] _bands = new float[BarCount];
    private int _position;
    private long _lastSamples;

    public void AddPcm16Stereo(byte[] buffer, int count, long timestamp)
    {
        lock (_gate)
        {
            for (var offset = 0; offset + 3 < Math.Min(count, buffer.Length); offset += 4)
            {
                var left = BitConverter.ToInt16(buffer, offset) / 32768f;
                var right = BitConverter.ToInt16(buffer, offset + 2) / 32768f;
                // Select the stronger channel so opposite-phase stereo does not cancel.
                var sample = Math.Abs(left) >= Math.Abs(right) ? left : right;
                _fft[_position].X = sample * (float)FastFourierTransform.HannWindow(_position, FftSize);
                _fft[_position].Y = 0;
                if (++_position != FftSize) continue;
                _position = 0;
                FastFourierTransform.FFT(true, 11, _fft);
                for (var band = 0; band < BarCount; band++)
                {
                    var low = Math.Clamp((int)(45 * Math.Pow(16000d / 45, band / (double)BarCount) * FftSize / SampleRate), 1, FftSize / 2 - 1);
                    var high = Math.Clamp((int)(45 * Math.Pow(16000d / 45, (band + 1d) / BarCount) * FftSize / SampleRate), low + 1, FftSize / 2);
                    double peak = 0;
                    for (var bin = low; bin < high; bin++) peak = Math.Max(peak, Math.Sqrt(_fft[bin].X * _fft[bin].X + _fft[bin].Y * _fft[bin].Y));
                    _bands[band] = (float)Math.Clamp((20 * Math.Log10(Math.Max(peak, 1e-8)) + 65) / 55, 0, 1);
                }
                _lastSamples = timestamp;
            }
        }
    }

    public float[] Read(long timestamp)
    {
        lock (_gate) return timestamp - _lastSamples > 250 ? new float[BarCount] : (float[])_bands.Clone();
    }

    public void Clear()
    {
        lock (_gate) { Array.Clear(_fft); Array.Clear(_bands); _position = 0; _lastSamples = 0; }
    }
}
