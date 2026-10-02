using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Pipit.App.Audio;

/// <summary>
/// A scheduled sound (the band). It keeps playing in time while muted, so it can be switched
/// back on mid-song; changes of <see cref="Muted"/> fade over a few milliseconds.
/// </summary>
public sealed class SoundVoice(float[] sound)
{
    public float[] Sound { get; } = sound;
    public bool Muted { get; set; }
    /// <summary>Fades out and is dropped (a band rendered for another tempo).</summary>
    public bool Stopped { get; private set; }
    internal float Gain = 1;

    public void Stop() => Stopped = true;
    internal float Target => Muted || Stopped ? 0 : 1;
}

/// <summary>
/// Metronome clicks placed sample-accurately in a generator stream. Clicks are scheduled in
/// capture-clock time (see <see cref="AudioHub"/>) and rendered when the generator buffer is
/// refilled, at the sample that will leave the speaker at that moment.
/// </summary>
public partial class ClickPlayer : Node
{
    private AudioStreamPlayer _player = null!;
    private AudioStreamGeneratorPlayback _playback = null!;
    private AudioHub _hub = null!;
    private readonly List<(double Time, bool Accent)> _pending = new();
    private readonly List<(double Time, SoundVoice Voice)> _pendingSounds = new();
    private float[] _click = Array.Empty<float>();
    private float[] _accent = Array.Empty<float>();
    private readonly List<(SoundVoice Voice, int Position)> _sounding = new();
    private int _rate;
    private int _capacity;

    public void Init(AudioHub hub) => _hub = hub;

    public override void _Ready()
    {
        _rate = (int)AudioServer.GetMixRate();
        _click = Synth(_rate, 1500, 0.35f);
        _accent = Synth(_rate, 2200, 0.5f);
        _player = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = _rate, BufferLength = 0.15f } };
        AddChild(_player);
        _player.Play();
        _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
    }

    /// <summary>Clicks at the given capture times; the first of every <paramref name="accentEvery"/> is accented.</summary>
    public void Schedule(IEnumerable<double> times, int accentEvery = 4)
    {
        var i = 0;
        foreach (var t in times)
            _pending.Add((t, accentEvery > 0 && i++ % accentEvery == 0));
        _pending.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    /// <summary>Clicks at the given capture times, each with its own accent (bar downbeats).</summary>
    public void Schedule(IEnumerable<(double Time, bool Accent)> clicks)
    {
        _pending.AddRange(clicks);
        _pending.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    /// <summary>Any sound (the band) starting at a capture time, mixed in with the clicks. A time
    /// already past starts the sound part-way, in step with the clock.</summary>
    public SoundVoice ScheduleSound(float[] sound, double time, bool muted = false)
    {
        var voice = new SoundVoice(sound) { Muted = muted, Gain = muted ? 0 : 1 };
        _pendingSounds.Add((time, voice));
        return voice;
    }

    /// <summary>Drops the clicks not yet played (the tempo changed); sounds keep playing.</summary>
    public void ClearClicks() => _pending.Clear();

    public void Stop()
    {
        _pending.Clear();
        _pendingSounds.Clear();
        _sounding.Clear();
    }

    public override void _Process(double delta)
    {
        if (_playback is null)
            return;
        var free = _playback.GetFramesAvailable();
        if (free <= 0)
            return;

        // The generator's real capacity may be rounded up from BufferLength, so learn it from
        // the most free space ever seen. The first frame pushed now plays after the frames
        // still queued, plus the driver's output latency.
        _capacity = Math.Max(_capacity, free);
        var queued = _capacity - free;
        var firstFrameWall = AudioHub.Wall() + queued / (double)_rate + AudioServer.GetOutputLatency();
        var firstFrameCapture = _hub.FromWall(firstFrameWall);

        while (_pending.Count > 0)
        {
            var offset = (int)Math.Round((_pending[0].Time - firstFrameCapture) * _rate);
            if (offset >= free)
                break;
            var accent = _pending[0].Accent;
            _pending.RemoveAt(0);
            if (offset < -_rate / 20)
                continue; // more than 50 ms late: drop rather than click off the beat
            // Position = index into the sound that lines up with buffer frame 0.
            _sounding.Add((new SoundVoice(accent ? _accent : _click), offset > 0 ? -offset : 0));
        }

        for (var i = _pendingSounds.Count - 1; i >= 0; i--)
        {
            var offset = (int)Math.Round((_pendingSounds[i].Time - firstFrameCapture) * _rate);
            if (offset >= free)
                continue;
            // A late start skips the part already missed, so the band stays on the beat.
            _sounding.Add((_pendingSounds[i].Voice, -offset)); // position in the sound at buffer frame 0
            _pendingSounds.RemoveAt(i);
        }

        var buffer = new Vector2[free];
        var fade = 1f / (0.03f * _rate); // 30 ms from silent to full
        for (var s = _sounding.Count - 1; s >= 0; s--)
        {
            var (voice, pos) = _sounding[s];
            var sound = voice.Sound;
            var target = voice.Target;
            for (var i = Math.Max(0, -pos); i < free && pos + i < sound.Length; i++)
            {
                if (voice.Gain != target)
                    voice.Gain = voice.Gain < target ? Math.Min(target, voice.Gain + fade) : Math.Max(target, voice.Gain - fade);
                var v = sound[pos + i] * voice.Gain;
                buffer[i] += new Vector2(v, v);
            }
            pos += free;
            if (pos >= sound.Length || voice.Stopped && voice.Gain <= 0)
                _sounding.RemoveAt(s);
            else
                _sounding[s] = (voice, pos);
        }
        _playback.PushBuffer(buffer);
    }

    /// <summary>A short pitched click: decaying sine with a noisy start.</summary>
    private static float[] Synth(int rate, double hz, float amplitude)
    {
        var n = (int)(0.025 * rate);
        var rng = new Random(7);
        return Enumerable.Range(0, n).Select(i =>
        {
            var t = (double)i / rate;
            var env = Math.Exp(-t * 180);
            var noise = i < rate / 1000 ? (rng.NextDouble() * 2 - 1) * 0.5 : 0;
            return (float)(amplitude * env * (Math.Sin(2 * Math.PI * hz * t) + noise));
        }).ToArray();
    }
}
