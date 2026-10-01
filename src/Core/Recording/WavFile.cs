using System.Text;

namespace Pipit.Core.Recording;

/// <summary>Minimal WAV I/O. Writes mono 16-bit PCM; reads 16-bit PCM or 32-bit float, mono or multi-channel (downmixed).</summary>
public static class WavFile
{
    public static void Write(string path, ReadOnlySpan<float> samples, int sampleRate)
    {
        using var stream = File.Create(path);
        Write(stream, samples, sampleRate);
    }

    public static void Write(Stream stream, ReadOnlySpan<float> samples, int sampleRate)
    {
        using var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var dataBytes = samples.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);              // fmt chunk size
        w.Write((short)1);        // PCM
        w.Write((short)1);        // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2);  // byte rate
        w.Write((short)2);        // block align
        w.Write((short)16);       // bits per sample
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in samples)
            w.Write(ToPcm16(s));
    }

    public static short ToPcm16(float sample) => (short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue);

    public static (float[] Samples, int SampleRate) Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static (float[] Samples, int SampleRate) Read(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "RIFF")
            throw new InvalidDataException("Not a RIFF file");
        r.ReadInt32();
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "WAVE")
            throw new InvalidDataException("Not a WAVE file");

        short format = 0, channels = 0, bits = 0;
        var rate = 0;
        while (stream.Position < stream.Length)
        {
            var id = Encoding.ASCII.GetString(r.ReadBytes(4));
            var size = r.ReadInt32();
            if (id == "fmt ")
            {
                format = r.ReadInt16();
                channels = r.ReadInt16();
                rate = r.ReadInt32();
                r.ReadInt32();
                r.ReadInt16();
                bits = r.ReadInt16();
                stream.Seek(size - 16, SeekOrigin.Current);
                if (format == unchecked((short)0xFFFE)) // WAVE_FORMAT_EXTENSIBLE: trust the bit depth
                    format = bits == 32 ? (short)3 : (short)1;
            }
            else if (id == "data")
            {
                if (channels == 0)
                    throw new InvalidDataException("data chunk before fmt chunk");
                var bytes = r.ReadBytes(size);
                return (Decode(bytes, format, channels, bits), rate);
            }
            else
            {
                stream.Seek(size + (size & 1), SeekOrigin.Current);
            }
        }
        throw new InvalidDataException("No data chunk");
    }

    private static float[] Decode(byte[] bytes, short format, short channels, short bits)
    {
        var bytesPerSample = bits / 8;
        var frames = bytes.Length / (bytesPerSample * channels);
        var result = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var offset = (f * channels + c) * bytesPerSample;
                sum += (format, bits) switch
                {
                    (1, 16) => BitConverter.ToInt16(bytes, offset) / 32768f,
                    (3, 32) => BitConverter.ToSingle(bytes, offset),
                    _ => throw new NotSupportedException($"WAV format {format} with {bits} bits"),
                };
            }
            result[f] = sum / channels;
        }
        return result;
    }
}
