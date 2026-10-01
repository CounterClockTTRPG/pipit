using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Pipit.Core.Analysis;

namespace Pipit.App.Audio;

/// <summary>Plays a 1.5 s recorder-like tone through the speakers and judges what the microphone made of it.</summary>
public partial class MicTester : Node
{
    public const double ToneSeconds = 1.5;

    public async Task<MicCheckResult> Run()
    {
        var audio = Services.Instance.Audio;
        var rate = audio.SampleRate;
        var blocks = new List<(double, double)>();
        void OnSamples(float[] block, double t)
        {
            var n = rate / 200; // 5 ms
            for (var i = 0; i + n <= block.Length; i += n)
                blocks.Add((t + (double)i / rate, YinPitchDetector.Rms(block.AsSpan(i, n))));
        }
        audio.SamplesCaptured += OnSamples;

        // Half a second of room first, then the tone (Sol on a soprano: 784 Hz).
        await Wait(0.5);
        var toneStart = audio.Now;
        var player = new AudioStreamPlayer { Stream = Playback.ToStream(Tone(784, ToneSeconds, rate), rate) };
        AddChild(player);
        player.Play();
        await Wait(ToneSeconds + 0.4);
        audio.SamplesCaptured -= OnSamples;
        player.QueueFree();
        var result = MicCheck.Evaluate(blocks, toneStart, toneStart + ToneSeconds);
        GD.Print($"Mic check on '{audio.CurrentDevice}': {result}");
        return result;
    }

    private async Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private static float[] Tone(double hz, double seconds, int rate)
    {
        var n = (int)(seconds * rate);
        var samples = new float[n];
        var ramp = rate / 50;
        for (var i = 0; i < n; i++)
        {
            var phase = 2 * Math.PI * hz * i / rate;
            var env = Math.Min(1.0, Math.Min((double)i / ramp, (double)(n - 1 - i) / ramp));
            samples[i] = (float)(0.3 * env * (Math.Sin(phase) + 0.15 * Math.Sin(2 * phase) + 0.08 * Math.Sin(3 * phase)));
        }
        return samples;
    }
}
