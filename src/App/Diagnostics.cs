using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pipit.App.Audio;
using Pipit.Core.Analysis;

namespace Pipit.App;

/// <summary>
/// `-- --loopback`: plays recorder-like tones and clicks through the speakers while listening
/// with the microphone, then prints what the tracker saw. Diagnoses the real hardware path
/// (gate, clarity, noise suppression, click timing) without anyone having to play.
/// </summary>
public partial class Diagnostics : Node
{
    public async void Loopback()
    {
        var s = Services.Instance;
        var audio = s.Audio;
        s.ApplyInstrument();
        var rate = audio.SampleRate;
        var frames = new List<PitchFrame>();
        var raw = new List<(double Time, double Rms)>();
        audio.FrameReady += frames.Add;
        audio.SamplesCaptured += (block, t) =>
        {
            // 5 ms RMS blocks, for click detection (clicks are unpitched, so pitch frames miss them).
            var n = rate / 200;
            for (var i = 0; i + n <= block.Length; i += n)
                raw.Add((t + (double)i / rate, YinPitchDetector.Rms(block.AsSpan(i, n))));
        };

        var tones = new[] { 79, 84, 81 }; // G5, C6, A5: soprano Sol, Do', La
        var player = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = rate, BufferLength = 0.5f } };
        AddChild(player);
        player.Play();
        var playback = (AudioStreamGeneratorPlayback)player.GetStreamPlayback();
        var start = audio.Now + 0.5;
        GD.Print($"LOOPBACK device='{audio.CurrentDevice}' rate={rate} gate={audio.GateRms:0.0000} output_latency={AudioServer.GetOutputLatency() * 1000:0} ms");

        // Tones: 1 s each with 0.5 s gaps, rendered into the generator as time passes.
        var phase = 0.0;
        var clickTimes = Enumerable.Range(0, 6).Select(i => start + 5.0 + i * 0.75).ToList();
        s.Clicks.Schedule(clickTimes, 0);
        var end = start + 10;
        var pushed = 0L;
        while (audio.Now < end)
        {
            var free = playback.GetFramesAvailable();
            var buf = new Vector2[free];
            for (var i = 0; i < free; i++, pushed++)
            {
                var t = pushed / (double)rate - 0.3;
                var idx = (int)Math.Floor(t / 1.5);
                float v = 0;
                if (t >= 0 && idx < tones.Length && t - idx * 1.5 < 1.0)
                {
                    var hz = 440 * Math.Pow(2, (tones[idx] - 69) / 12.0);
                    phase += 2 * Math.PI * hz / rate;
                    v = (float)(0.25 * (Math.Sin(phase) + 0.15 * Math.Sin(2 * phase) + 0.08 * Math.Sin(3 * phase)));
                }
                buf[i] = new Vector2(v, v);
            }
            playback.PushBuffer(buf);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        audio.FrameReady -= frames.Add;

        string P(IEnumerable<double> xs) => $"p10={Stats.Percentile(xs, 0.1):0.000} p50={Stats.Percentile(xs, 0.5):0.000} p90={Stats.Percentile(xs, 0.9):0.000}";
        var loud = frames.Where(f => f.Rms >= audio.GateRms).ToList();
        GD.Print($"LOOPBACK frames={frames.Count} above_gate={loud.Count} voiced={frames.Count(f => f.Voiced)}");
        GD.Print($"LOOPBACK dB all: {P(frames.Select(f => f.Db))}");
        GD.Print($"LOOPBACK clarity above gate: {P(loud.Select(f => f.Clarity))}");
        foreach (var group in loud.GroupBy(f => (int)Math.Round(double.IsNaN(f.Midi) ? Math.Log2(Math.Max(f.Hz, 1) / 440) * 12 + 69 : f.Midi)).OrderByDescending(g => g.Count()).Take(6))
            GD.Print($"LOOPBACK pitch {group.Key}: {group.Count()} frames, voiced {group.Count(f => f.Voiced)}");

        // Level timeline, 100 ms per character: tones play 0.3-1.3, 1.8-2.8, 3.3-4.3 s after start.
        var t0 = start - 0.5;
        var line = new System.Text.StringBuilder();
        for (var t = t0; t < t0 + 6; t += 0.1)
        {
            var block = raw.Where(r => r.Time >= t && r.Time < t + 0.1).Select(r => r.Rms).DefaultIfEmpty(0).Max();
            var db = 20 * Math.Log10(block + 1e-9);
            line.Append(db > -30 ? '#' : db > -45 ? '+' : db > -60 ? '.' : db > -100 ? ',' : ' ');
        }
        GD.Print($"LOOPBACK level |{line}|  (# > -30, + > -45, . > -60, , > -100 dBFS, blank = digital silence)");

        // Click arrival: first 5 ms block after each scheduled click that jumps 15 dB over the median.
        var floor = Stats.Median(raw.Select(r => r.Rms));
        var delays = new List<double>();
        foreach (var c in clickTimes)
        {
            var hit = raw.FirstOrDefault(r => r.Time >= c - 0.1 && r.Time <= c + 0.5 && r.Rms > floor * 5.6);
            if (hit != default)
                delays.Add((hit.Time - c) * 1000);
        }
        GD.Print($"LOOPBACK click arrival ms: {string.Join(", ", delays.Select(d => d.ToString("0")))} (heard {delays.Count}/{clickTimes.Count})");
        var tester = new MicTester();
        AddChild(tester);
        GD.Print($"LOOPBACK mic check: {await tester.Run()}");
        GetTree().Quit();
    }
}
