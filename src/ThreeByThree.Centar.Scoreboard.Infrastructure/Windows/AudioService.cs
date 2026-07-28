using System.Buffers.Binary;
using System.Media;
using System.Text;
using ThreeByThree.Centar.Scoreboard.Application.Operations;
using ThreeByThree.Centar.Scoreboard.Application.Settings;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Windows;

public sealed class AudioService : IAudioService, IDisposable
{
    private const int SampleRate = 44_100;
    private const string ShotClockBuzzerResourceName =
        "ThreeByThree.Centar.Scoreboard.Infrastructure.Assets.Audio.ShotClockBuzzer.wav";
    private static readonly Lazy<byte[]> ShotClockBuzzerWave =
        new(LoadShotClockBuzzerWave);
    private readonly object gate = new();
    private SoundPlayer? activePlayer;
    private MemoryStream? activeStream;
    private bool isEnabled = true;
    private int volumePercent = 80;
    private bool isDisposed;

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            isEnabled = settings.AudioEnabled;
            volumePercent = Math.Clamp(settings.VolumePercent, 0, 100);
        }
    }

    public void Play(BuzzerKind buzzer)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            if (!isEnabled || volumePercent == 0)
            {
                return;
            }

            PlayUnsafe(buzzer, volumePercent);
        }
    }

    public void Test(int volumePercent)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            PlayUnsafe(BuzzerKind.ShotClock, Math.Clamp(volumePercent, 0, 100));
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            activePlayer?.Stop();
            activePlayer?.Dispose();
            activeStream?.Dispose();
            activePlayer = null;
            activeStream = null;
        }

        GC.SuppressFinalize(this);
    }

    private void PlayUnsafe(BuzzerKind buzzer, int volume)
    {
        activePlayer?.Stop();
        activePlayer?.Dispose();
        activeStream?.Dispose();

        activeStream = buzzer == BuzzerKind.ShotClock
            ? CreateRecordedShotClockWave(volume)
            : CreateSynthesizedWave(buzzer, volume);
        activePlayer = new SoundPlayer(activeStream);
        activePlayer.Load();
        activePlayer.Play();
    }

    private static MemoryStream CreateRecordedShotClockWave(int volume)
    {
        var wave = (byte[])ShotClockBuzzerWave.Value.Clone();
        var (dataOffset, dataLength) = FindPcm16Data(wave);

        for (var offset = dataOffset; offset < dataOffset + dataLength; offset += sizeof(short))
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(offset));
            var scaledSample = (short)(sample * volume / 100);
            BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(offset), scaledSample);
        }

        return new MemoryStream(wave, writable: false);
    }

    private static byte[] LoadShotClockBuzzerWave()
    {
        using var resource = typeof(AudioService).Assembly.GetManifestResourceStream(
            ShotClockBuzzerResourceName);
        if (resource is null)
        {
            throw new InvalidOperationException(
                $"Embedded shot-clock buzzer '{ShotClockBuzzerResourceName}' was not found.");
        }

        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static (int DataOffset, int DataLength) FindPcm16Data(byte[] wave)
    {
        if (wave.Length < 12 ||
            !wave.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !wave.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidOperationException("The embedded shot-clock buzzer is not a RIFF WAVE file.");
        }

        var isPcm16 = false;
        var chunkOffset = 12;
        while (chunkOffset <= wave.Length - 8)
        {
            var chunkId = wave.AsSpan(chunkOffset, 4);
            var chunkLength = BinaryPrimitives.ReadInt32LittleEndian(
                wave.AsSpan(chunkOffset + 4, sizeof(int)));
            var dataOffset = chunkOffset + 8;
            if (chunkLength < 0 || chunkLength > wave.Length - dataOffset)
            {
                throw new InvalidOperationException(
                    "The embedded shot-clock buzzer contains an invalid WAVE chunk.");
            }

            if (chunkId.SequenceEqual("fmt "u8))
            {
                if (chunkLength < 16)
                {
                    throw new InvalidOperationException(
                        "The embedded shot-clock buzzer has an invalid WAVE format chunk.");
                }

                var audioFormat = BinaryPrimitives.ReadInt16LittleEndian(
                    wave.AsSpan(dataOffset, sizeof(short)));
                var bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(
                    wave.AsSpan(dataOffset + 14, sizeof(short)));
                isPcm16 = audioFormat == 1 && bitsPerSample == 16;
            }
            else if (chunkId.SequenceEqual("data"u8))
            {
                if (!isPcm16 || chunkLength % sizeof(short) != 0)
                {
                    throw new InvalidOperationException(
                        "The embedded shot-clock buzzer must use 16-bit PCM audio.");
                }

                return (dataOffset, chunkLength);
            }

            chunkOffset = dataOffset + chunkLength + (chunkLength & 1);
        }

        throw new InvalidOperationException(
            "The embedded shot-clock buzzer does not contain playable PCM audio.");
    }

    private static MemoryStream CreateSynthesizedWave(BuzzerKind buzzer, int volume)
    {
        var duration = buzzer switch
        {
            BuzzerKind.GameClock => TimeSpan.FromMilliseconds(1_350),
            BuzzerKind.ShotClockWarning => TimeSpan.FromMilliseconds(140),
            _ => TimeSpan.FromMilliseconds(500),
        };
        var sampleCount = checked((int)(SampleRate * duration.TotalSeconds));
        var dataLength = checked(sampleCount * sizeof(short));
        var stream = new MemoryStream(capacity: 44 + dataLength);

        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);

            var gain = buzzer == BuzzerKind.ShotClockWarning ? 0.24 : 0.42;
            var amplitude = short.MaxValue * gain * (volume / 100d);
            for (var index = 0; index < sampleCount; index++)
            {
                var seconds = index / (double)SampleRate;
                var progress = index / (double)Math.Max(1, sampleCount - 1);
                var envelope = Math.Min(1, progress * 40) *
                    Math.Min(1, (1 - progress) * 24);
                var frequency = GetFrequency(buzzer, seconds);
                var fundamental = Math.Sin(2 * Math.PI * frequency * seconds);
                var harmonic = 0.28 * Math.Sin(2 * Math.PI * frequency * 2 * seconds);
                var sample = amplitude * envelope * (fundamental + harmonic) / 1.28;
                writer.Write((short)Math.Clamp(sample, short.MinValue, short.MaxValue));
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static double GetFrequency(BuzzerKind buzzer, double seconds) => buzzer switch
    {
        BuzzerKind.GameClock => seconds < 0.68 ? 420 : 350,
        BuzzerKind.ShotClockWarning => 1_180,
        _ => 560,
    };
}
