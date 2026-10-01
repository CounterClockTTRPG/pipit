using System;
using Godot;
using Pipit.Core.Recording;

namespace Pipit.App;

public static class Playback
{
    /// <summary>Mono float samples as a playable 16-bit stream.</summary>
    public static AudioStreamWav ToStream(float[] samples, int sampleRate)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var s = WavFile.ToPcm16(samples[i]);
            bytes[2 * i] = (byte)(s & 0xff);
            bytes[2 * i + 1] = (byte)((s >> 8) & 0xff);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = sampleRate, Stereo = false, Data = bytes };
    }
}
