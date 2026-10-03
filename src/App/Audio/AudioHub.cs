using System;
using System.Collections.Generic;
using Godot;
using Pipit.Core.Analysis;

namespace Pipit.App.Audio;

/// <summary>
/// Owns the microphone. Captures from a muted bus (so the mic is never heard), converts to mono,
/// feeds the pitch tracker and anyone listening for raw samples (the recorder).
///
/// Time base: the <b>capture clock</b>, seconds of audio captured since start-up. Pitch frames,
/// recordings and scheduled clicks all use it, so they line up with each other; the fixed part
/// of the remaining offset is what latency calibration measures.
/// </summary>
public partial class AudioHub : Node
{
    public const string CaptureBus = "MicCapture";

    private AudioEffectCapture _capture = null!;
    private AudioStreamPlayer _mic = null!;
    private PitchTracker _tracker = null!;
    private readonly List<PitchFrame> _frames = new();
    private long _captured;
    private long _trackerStart; // capture sample at which the current tracker started
    private double _wallMinusCapture = double.NaN;
    private double _levelDb = -120;

    public int SampleRate { get; private set; }
    public double LevelDb => _levelDb;
    public PitchFrame? LastFrame { get; private set; }
    public double GateRms => _tracker.GateRms;

    /// <summary>Mono samples as captured, with the capture time of the first one.</summary>
    public event Action<float[], double>? SamplesCaptured;
    public event Action<PitchFrame>? FrameReady;

    public override void _Ready()
    {
        SampleRate = (int)AudioServer.GetMixRate();
        var bus = AudioServer.GetBusIndex(CaptureBus);
        if (bus < 0)
        {
            AudioServer.AddBus();
            bus = AudioServer.BusCount - 1;
            AudioServer.SetBusName(bus, CaptureBus);
        }
        AudioServer.SetBusMute(bus, true);
        _capture = new AudioEffectCapture { BufferLength = 0.5f };
        AudioServer.AddBusEffect(bus, _capture);

        _mic = new AudioStreamPlayer { Stream = new AudioStreamMicrophone(), Bus = CaptureBus };
        AddChild(_mic);
        Configure(new PitchTrackerOptions(), null);
        _mic.Play();

        // Android asks the player before an app may use the microphone; the stream only
        // delivers sound after that, so it is restarted once permission is granted.
        if (Services.IsAndroid && Array.IndexOf(OS.GetGrantedPermissions(), MicPermission) < 0)
        {
            GetTree().OnRequestPermissionsResult += (permission, granted) =>
            {
                if (granted && permission == MicPermission)
                    Restart();
            };
            OS.RequestPermissions();
        }
    }

    private const string MicPermission = "android.permission.RECORD_AUDIO";

    private void Restart()
    {
        _mic.Stop();
        _capture.ClearBuffer();
        _mic.Play();
    }

    public static string[] InputDevices() => AudioServer.GetInputDeviceList();

    public string CurrentDevice => AudioServer.InputDevice;

    public void SelectDevice(string name)
    {
        if (Array.IndexOf(InputDevices(), name) < 0)
            name = "Default";
        if (AudioServer.InputDevice == name)
            return;
        AudioServer.InputDevice = name;
        // Restart so the stream picks the new device up; drop what the old one left behind.
        Restart();
    }

    /// <summary>Replaces the tracker (range, reference A); frames restart from the current capture time.</summary>
    public void Configure(PitchTrackerOptions options, double? gateRms)
    {
        _tracker = new PitchTracker(SampleRate, options);
        if (gateRms is { } g)
            _tracker.GateRms = g;
        _trackerStart = _captured;
    }

    public void SetGate(double gateRms) => _tracker.GateRms = gateRms;

    /// <summary>Current position of the capture clock, estimated from the wall clock.</summary>
    public double Now => double.IsNaN(_wallMinusCapture) ? (double)_captured / SampleRate : Wall() - _wallMinusCapture;

    public static double Wall() => Time.GetTicksUsec() / 1e6;

    /// <summary>Capture-clock time of a wall-clock moment (for scheduling output).</summary>
    public double FromWall(double wall) => double.IsNaN(_wallMinusCapture) ? Now + (wall - Wall()) : wall - _wallMinusCapture;

    public override void _Process(double delta)
    {
        var available = _capture.GetFramesAvailable();
        if (available <= 0)
        {
            _levelDb = Math.Max(-120, _levelDb - 60 * delta);
            return;
        }

        var stereo = _capture.GetBuffer(available);
        var mono = new float[stereo.Length];
        double sum = 0;
        for (var i = 0; i < stereo.Length; i++)
        {
            mono[i] = (stereo[i].X + stereo[i].Y) * 0.5f;
            sum += mono[i] * mono[i];
        }

        var startTime = (double)_captured / SampleRate;
        _captured += mono.Length;
        // The last captured sample is "now" give or take a buffer. The smallest observed
        // offset is the least delayed read, so drift towards it slowly and jump down to it.
        var offset = Wall() - (double)_captured / SampleRate;
        _wallMinusCapture = double.IsNaN(_wallMinusCapture) || offset < _wallMinusCapture
            ? offset
            : _wallMinusCapture + (offset - _wallMinusCapture) * 0.01;

        var db = 10 * Math.Log10(sum / mono.Length + 1e-12);
        _levelDb = db > _levelDb ? db : Math.Max(db, _levelDb - 60 * delta); // fast attack, 60 dB/s fall

        SamplesCaptured?.Invoke(mono, startTime);

        _frames.Clear();
        _tracker.Push(mono, _frames);
        var trackerOffset = (double)_trackerStart / SampleRate;
        foreach (var f in _frames)
        {
            var frame = f with { Time = f.Time + trackerOffset };
            LastFrame = frame;
            FrameReady?.Invoke(frame);
        }
    }
}
