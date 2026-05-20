using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Globalization;

namespace AudioABTester.Audio;

public sealed class AudioTrack
{
    public string FilePath { get; }
    public string DisplayName { get; }
    public WaveFormat WaveFormat { get; }
    public float[] Samples { get; }
    public long FrameCount { get; }
    public TimeSpan Duration { get; }

    private AudioTrack(string filePath, WaveFormat waveFormat, float[] samples)
    {
        FilePath = filePath;
        DisplayName = Path.GetFileName(filePath);
        WaveFormat = waveFormat;
        Samples = samples;
        FrameCount = samples.LongLength / waveFormat.Channels;
        Duration = TimeSpan.FromSeconds((double)FrameCount / waveFormat.SampleRate);
    }

    public static AudioTrack Load(string filePath, WaveFormat targetFormat)
    {
        // Do not dispose reader until after all samples are read!
        var reader = new AudioFileReader(filePath);
        try
        {
            ISampleProvider provider = reader;

            provider = ConvertChannelLayout(provider, targetFormat.Channels);

            if (provider.WaveFormat.SampleRate != targetFormat.SampleRate)
            {
                provider = new WdlResamplingSampleProvider(provider, targetFormat.SampleRate);
            }

            if (provider.WaveFormat.Channels != targetFormat.Channels)
            {
                provider = ConvertChannelLayout(provider, targetFormat.Channels);
            }

            var samples = ReadAllSamples(provider);
            return new AudioTrack(filePath, targetFormat, samples);
        }
        finally
        {
            reader.Dispose();
        }
    }

    public void MixInto(float[] destination, int destinationOffset, int sampleCount, long framePosition, float gain)
    {
        if (gain <= 0f)
        {
            return;
        }

        var startSampleIndex = framePosition * WaveFormat.Channels;
        if (startSampleIndex >= Samples.LongLength)
        {
            return;
        }

        var availableSamples = (int)Math.Min(sampleCount, Samples.LongLength - startSampleIndex);
        for (var index = 0; index < availableSamples; index++)
        {
            destination[destinationOffset + index] += Samples[startSampleIndex + index] * gain;
        }
    }

    public string GetDebugStats()
    {
        if (Samples.Length == 0)
        {
            return $"format={WaveFormat.SampleRate}Hz/{WaveFormat.Channels}ch, frames=0, samples=0";
        }

        var first = string.Join(", ", Samples.Take(10).Select(s => s.ToString("G9", CultureInfo.InvariantCulture)));
        var maxAbs = 0f;
        double sumSquares = 0d;

        for (var index = 0; index < Samples.Length; index++)
        {
            var value = Samples[index];
            var abs = Math.Abs(value);
            if (abs > maxAbs)
            {
                maxAbs = abs;
            }

            sumSquares += value * value;
        }

        var rms = Math.Sqrt(sumSquares / Samples.Length);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"format={WaveFormat.SampleRate}Hz/{WaveFormat.Channels}ch, frames={FrameCount}, samples={Samples.Length}, maxAbs={maxAbs:G9}, rms={rms:G9}, first10=[{first}]"
        );
    }

    private static float[] ReadAllSamples(ISampleProvider provider)
    {
        var buffer = new float[Math.Max(provider.WaveFormat.SampleRate * provider.WaveFormat.Channels, 4096)];
        var samples = new List<float>(buffer.Length * 4);

        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var index = 0; index < read; index++)
            {
                samples.Add(buffer[index]);
            }
        }

        return samples.ToArray();
    }

    private static ISampleProvider ConvertChannelLayout(ISampleProvider provider, int targetChannels)
    {
        if (provider.WaveFormat.Channels == targetChannels)
        {
            return provider;
        }

        if (provider.WaveFormat.Channels == 2 && targetChannels == 1)
        {
            return new StereoToMonoSampleProvider(provider)
            {
                LeftVolume = 0.5f,
                RightVolume = 0.5f
            };
        }

        var multiplexer = new MultiplexingSampleProvider(new[] { provider }, targetChannels);

        if (provider.WaveFormat.Channels == 1)
        {
            for (var outputChannel = 0; outputChannel < targetChannels; outputChannel++)
            {
                multiplexer.ConnectInputToOutput(0, outputChannel);
            }

            return multiplexer;
        }

        var routedChannels = Math.Min(provider.WaveFormat.Channels, targetChannels);
        for (var channel = 0; channel < routedChannels; channel++)
        {
            multiplexer.ConnectInputToOutput(channel, channel);
        }

        return multiplexer;
    }
}