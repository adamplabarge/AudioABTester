using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using AudioABTester.Services;
using System.Globalization;
using System.IO;

namespace AudioABTester.Audio;

public enum PlaybackSource
{
    A,
    B
}

public sealed record AudioOutputDevice(string Id, string Name, bool IsDefault);

public sealed class AudioEngine : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly MMDeviceEnumerator _deviceEnumerator;
    private MMDevice _outputDevice;
    private WasapiOut _output;
    private readonly SynchronizedAbSampleProvider _sampleProvider;
    private readonly VolumeMatchService _volumeMatchService;

    private long _framePosition;
    private float _trimGainA = 1f;
    private float _trimGainB = 1f;

    public AudioEngine(VolumeMatchService volumeMatchService)
    {
        _volumeMatchService = volumeMatchService;

        _deviceEnumerator = new MMDeviceEnumerator();
        _outputDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        var mixFormat = _outputDevice.AudioClient.MixFormat;
        // Use a stable internal stereo mix format, then let WASAPI shared mode handle endpoint conversion.
        OutputFormat = WaveFormat.CreateIeeeFloatWaveFormat(mixFormat.SampleRate, 2);
        WriteDebugLine($"Output device: {_outputDevice.FriendlyName}");
        WriteDebugLine($"Endpoint mix format: {mixFormat.SampleRate} Hz, {mixFormat.Channels} ch, {mixFormat.BitsPerSample} bit, {mixFormat.Encoding}");
        WriteDebugLine($"Engine output format: {OutputFormat.SampleRate} Hz, {OutputFormat.Channels} ch, {OutputFormat.BitsPerSample} bit, {OutputFormat.Encoding}");

        // One output device and one provider chain ensures the two sources share a single hardware clock.
        // That prevents long-term drift that would happen with independent WasapiOut instances.
        _sampleProvider = new SynchronizedAbSampleProvider(this);
        _output = CreateOutput(_outputDevice);
    }

    public WaveFormat OutputFormat { get; }

    public AudioTrack? TrackA { get; private set; }

    public AudioTrack? TrackB { get; private set; }

    public PlaybackSource CurrentSource { get; private set; } = PlaybackSource.A;

    public string CurrentOutputDeviceId => _outputDevice.ID;

    public string CurrentOutputDeviceName => _outputDevice.FriendlyName;

    public bool CanStart => TrackA is not null && TrackB is not null;

    public bool IsPlaying => _output.PlaybackState == PlaybackState.Playing;

    public PlaybackState PlaybackState => _output.PlaybackState;

    public TimeSpan CurrentPosition
    {
        get
        {
            lock (_syncRoot)
            {
                return TimeSpan.FromSeconds((double)_framePosition / OutputFormat.SampleRate);
            }
        }
    }

    public TimeSpan TotalDuration
    {
        get
        {
            lock (_syncRoot)
            {
                var totalFrames = Math.Max(TrackA?.FrameCount ?? 0, TrackB?.FrameCount ?? 0);
                return TimeSpan.FromSeconds((double)totalFrames / OutputFormat.SampleRate);
            }
        }
    }

    public AudioTrack LoadTrackA(string filePath)
    {
        var track = AudioTrack.Load(filePath, OutputFormat);
        WriteDebugLine($"Loaded A: {track.DisplayName}");
        WriteDebugLine(track.GetDebugStats());

        lock (_syncRoot)
        {
            TrackA = track;
            RecalculateTrimGainsUnsafe();
            ClampPositionUnsafe();
            WriteDebugLine($"State after Load A: A='{TrackA.DisplayName}', B='{TrackB?.DisplayName ?? "(none)"}'");
        }

        return track;
    }

    public AudioTrack LoadTrackB(string filePath)
    {
        var track = AudioTrack.Load(filePath, OutputFormat);
        WriteDebugLine($"Loaded B: {track.DisplayName}");
        WriteDebugLine(track.GetDebugStats());

        lock (_syncRoot)
        {
            TrackB = track;
            RecalculateTrimGainsUnsafe();
            ClampPositionUnsafe();
            WriteDebugLine($"State after Load B: A='{TrackA?.DisplayName ?? "(none)"}', B='{TrackB.DisplayName}'");

            if (TrackA is not null)
            {
                WriteDebugLine(ComputeTrackDifferenceSummaryUnsafe(TrackA, TrackB));
            }
        }

        return track;
    }

    public void Start()
    {
        if (!CanStart)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_framePosition >= GetTotalFramesUnsafe())
            {
                _framePosition = 0;
            }

            WriteDebugLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Start playback. posFrames={_framePosition}, source={CurrentSource}, A='{TrackA?.DisplayName ?? "(none)"}', B='{TrackB?.DisplayName ?? "(none)"}'"));
        }

        _output.Play();
    }

    public void Pause() => _output.Pause();

    public void Stop()
    {
        _output.Stop();

        lock (_syncRoot)
        {
            _framePosition = 0;
        }
    }

    public void ClearTracks()
    {
        _output.Stop();

        lock (_syncRoot)
        {
            TrackA = null;
            TrackB = null;
            CurrentSource = PlaybackSource.A;
            _framePosition = 0;
            _trimGainA = 1f;
            _trimGainB = 1f;
        }
    }

    public void Seek(TimeSpan position)
    {
        lock (_syncRoot)
        {
            var requestedFrame = (long)Math.Round(position.TotalSeconds * OutputFormat.SampleRate);
            var totalFrames = GetTotalFramesUnsafe();
            _framePosition = Math.Clamp(requestedFrame, 0, totalFrames);
        }
    }

    public void SeekBy(TimeSpan delta) => Seek(CurrentPosition + delta);

    public void ListenTo(PlaybackSource source)
    {
        lock (_syncRoot)
        {
            CurrentSource = source;
            WriteDebugLine($"Listen source changed to {CurrentSource}");
        }
    }

    public void ToggleSource()
    {
        ListenTo(CurrentSource == PlaybackSource.A ? PlaybackSource.B : PlaybackSource.A);
    }

    public bool IsAtEnd
    {
        get
        {
            lock (_syncRoot)
            {
                return _framePosition >= GetTotalFramesUnsafe();
            }
        }
    }

    public IReadOnlyList<AudioOutputDevice> GetOutputDevices()
    {
        var result = new List<AudioOutputDevice>();
        var defaultDeviceId = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
        var devices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        foreach (var device in devices)
        {
            result.Add(new AudioOutputDevice(device.ID, device.FriendlyName, device.ID == defaultDeviceId));
        }

        return result;
    }

    public void SetOutputDevice(string deviceId)
    {
        var devices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        var selected = devices.FirstOrDefault(d => string.Equals(d.ID, deviceId, StringComparison.Ordinal));
        if (selected is null)
        {
            return;
        }

        var wasPlaying = _output.PlaybackState == PlaybackState.Playing;
        _output.Stop();
        _output.Dispose();
        _outputDevice.Dispose();

        _outputDevice = selected;
        _output = CreateOutput(_outputDevice);

        WriteDebugLine($"Output device switched to: {_outputDevice.FriendlyName}");

        if (wasPlaying && CanStart)
        {
            _output.Play();
        }
    }

    public void Dispose()
    {
        _output.Dispose();
        _outputDevice.Dispose();
        _deviceEnumerator.Dispose();
    }

    private WasapiOut CreateOutput(MMDevice device)
    {
        var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 20);
        // Emit 16-bit PCM to avoid device-specific float-format interpretation issues.
        output.Init(new SampleToWaveProvider16(_sampleProvider));
        return output;
    }

    private void RecalculateTrimGainsUnsafe()
    {
        // TODO: Replace this placeholder with per-track analysis and persistent calibration controls.
        var adjustmentDb = _volumeMatchService.GetSuggestedGainDb(TrackA, TrackB);
        _trimGainA = 1f;
        _trimGainB = (float)Math.Pow(10d, adjustmentDb / 20d);
    }

    private long GetTotalFramesUnsafe() => Math.Max(TrackA?.FrameCount ?? 0, TrackB?.FrameCount ?? 0);

    private void ClampPositionUnsafe()
    {
        _framePosition = Math.Clamp(_framePosition, 0, GetTotalFramesUnsafe());
    }

    private sealed class SynchronizedAbSampleProvider : ISampleProvider
    {
        private readonly AudioEngine _engine;

        public SynchronizedAbSampleProvider(AudioEngine engine)
        {
            _engine = engine;
        }

        public WaveFormat WaveFormat => _engine.OutputFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);

            lock (_engine._syncRoot)
            {
                if (_engine.TrackA is null && _engine.TrackB is null)
                {
                    return count;
                }

                if (_engine.CurrentSource == PlaybackSource.A)
                {
                    _engine.TrackA?.MixInto(buffer, offset, count, _engine._framePosition, _engine._trimGainA);
                }
                else
                {
                    _engine.TrackB?.MixInto(buffer, offset, count, _engine._framePosition, _engine._trimGainB);
                }

                // Prevent hard clipping artifacts from summing or source material above 0 dBFS.
                for (var i = 0; i < count; i++)
                {
                    var sample = buffer[offset + i];
                    if (sample > 1f)
                    {
                        buffer[offset + i] = 1f;
                    }
                    else if (sample < -1f)
                    {
                        buffer[offset + i] = -1f;
                    }
                }

                _engine._framePosition += count / WaveFormat.Channels;
            }

            return count;
        }
    }

    private static void WriteDebugLine(string line)
    {
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioABTester");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "AudioABTester-debug.log");
        File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
    }

    private static string ComputeTrackDifferenceSummaryUnsafe(AudioTrack a, AudioTrack b)
    {
        var samplesToCompare = (int)Math.Min(a.Samples.Length, b.Samples.Length);
        if (samplesToCompare == 0)
        {
            return "A/B diff check: no samples to compare.";
        }

        double sumSquares = 0d;
        var maxAbs = 0f;
        for (var i = 0; i < samplesToCompare; i++)
        {
            var diff = a.Samples[i] - b.Samples[i];
            var abs = Math.Abs(diff);
            if (abs > maxAbs)
            {
                maxAbs = abs;
            }

            sumSquares += diff * diff;
        }

        var rms = Math.Sqrt(sumSquares / samplesToCompare);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"A/B diff check: compared={samplesToCompare} samples, diffMaxAbs={maxAbs:G9}, diffRms={rms:G9}");
    }
}