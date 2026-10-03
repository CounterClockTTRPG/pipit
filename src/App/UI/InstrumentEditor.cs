using System;
using System.Collections.Generic;
using System.Linq;
using Pipit.Core.Analysis;
using Godot;
using Pipit.Core.Instruments;

namespace Pipit.App.UI;

/// <summary>
/// Choose, add, remove and edit recorders. Used by Settings and by the first step of
/// calibration ("which recorder are you calibrating?"). Edits apply to the active recorder.
/// </summary>
public partial class InstrumentEditor : VBoxContainer
{
    private static Services S => Services.Instance;

    private readonly OptionButton _picker = new();
    private readonly Button _remove;
    private readonly LineEdit _name = new() { PlaceholderText = "e.g. Mollenhauer Canta" };
    private readonly OptionButton _kind;
    private readonly OptionButton _fingering;
    private readonly CheckBox _keyed = new();
    private readonly SpinBox _referenceA = new() { MinValue = 400, MaxValue = 466, Step = 1, Suffix = "Hz" };
    private readonly Label _status;
    private readonly Button _detect;
    private bool _refreshing;
    // "Don't know?": the pitches heard while the player holds the lowest note.
    private readonly List<double> _heard = new();
    private double _listenUntil;

    private static readonly RecorderKind[] Kinds = Enum.GetValues<RecorderKind>();
    private static readonly FingeringSystem[] Systems = Enum.GetValues<FingeringSystem>();

    /// <summary>Raised after the active recorder changes or is edited.</summary>
    public event Action? Changed;

    public InstrumentEditor(bool showAdvanced = true)
    {
        AddThemeConstantOverride("separation", 10);
        _picker.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _picker.AutoTranslateMode = AutoTranslateModeEnum.Disabled; // recorder names are the player's own
        _picker.ItemSelected += ix => { S.SetActiveInstrument((string)_picker.GetItemMetadata((int)ix)); Refresh(); Changed?.Invoke(); };
        var add = Ui.Button("+ Add recorder", () => { S.AddInstrument(S.Instrument.Kind, S.Instrument.Fingering); Refresh(); _name.GrabFocus(); _name.SelectAll(); Changed?.Invoke(); }, 18);
        _remove = Ui.Button("Remove", Remove, 18);
        AddChild(Ui.HBox(10, _picker, add, _remove));

        _name.TextChanged += t =>
        {
            if (_refreshing)
                return;
            S.Instrument.Name = t.Trim() == "" ? Ui.T("My recorder") : t.Trim();
            _picker.SetItemText(_picker.Selected, S.Instrument.Name);
            S.Save();
        };
        _kind = Ui.Options(Kinds.Select(k => k.ToString()).ToArray(), 0, ix => Edit(i =>
        {
            if (i.Kind == Kinds[ix])
                return;
            i.Kind = Kinds[ix];
            ForgetTuning(i); // per-note offsets are for the old size's notes
        }));
        _fingering = Ui.Options(Systems.Select(f => f.ToString()).ToArray(), 0, ix => Edit(i => i.Fingering = Systems[ix]));
        _keyed.Text = Ui.T("Holes 6 and 7 are keys (some tenors)");
        _keyed.Toggled += on => Edit(i => i.Keyed = on);
        _referenceA.ValueChanged += v => Edit(i => i.ReferenceA = v);
        _status = Ui.Text("", 15, PipitTheme.InkSoft);

        AddChild(Ui.Label("Name"));
        AddChild(_name);
        _detect = Ui.Quiet(Ui.Button("Don't know? Let Pip listen", Detect, 16));
        AddChild(Ui.HBox(16, Ui.Expand(Ui.VBox(6, Ui.Label("Size"), _kind, _detect)), Ui.Expand(Ui.VBox(6, Ui.Label("Fingering system"), _fingering))));
        AddChild(Ui.Text("Most recorders are baroque (also called English). German-fingered school recorders have a simpler Fa; look for a 'G' or 'German' marking.", 15, PipitTheme.InkSoft));
        // Always in the tree (a control never added is never freed); hidden when not wanted.
        var refAHint = Ui.Text("Pitch of A (440 Hz is standard; 442 is common in Europe; 415 for baroque-pitch instruments)", 15, PipitTheme.InkSoft);
        foreach (var advanced in new Control[] { _keyed, refAHint, _referenceA })
        {
            advanced.Visible = showAdvanced;
            AddChild(advanced);
        }
        AddChild(_status);
        Refresh();
    }

    /// <summary>For `--shots`: detection fed the active recorder's own lowest note (so nothing changes).</summary>
    public void DebugDetect()
    {
        Detect();
        var midi = (double)S.Recorder.BottomSoundingMidi;
        for (var i = 0; i < 45 && _listenUntil > 0; i++)
            S.Audio.InjectFrame(new PitchFrame(S.Audio.Now, 440 * Math.Pow(2, (midi - 69) / 12), 0.95, 0.05, true, midi, midi));
    }

    /// <summary>Listens to the lowest note (every hole closed) and sets the size it belongs to.</summary>
    private void Detect()
    {
        if (_listenUntil > 0)
            return;
        _heard.Clear();
        _listenUntil = S.Audio.Now + 8;
        _detect.Disabled = true;
        _status.Text = Ui.T("Close every hole, the thumb hole too, and blow gently: Pip listens for your lowest note…");
        S.ApplyWideRange();
        S.Audio.FrameReady += OnFrame;
    }

    private void OnFrame(PitchFrame frame)
    {
        if (frame.Voiced && !double.IsNaN(frame.SmoothedMidi))
            _heard.Add(frame.SmoothedMidi);
        // A steady second of sound is enough; give up after eight seconds.
        if (_heard.Count >= 40 || S.Audio.Now > _listenUntil)
            FinishDetect();
    }

    private void FinishDetect()
    {
        S.Audio.FrameReady -= OnFrame;
        _listenUntil = 0;
        _detect.Disabled = false;
        var guess = RecorderSize.Guess(_heard);
        if (guess is { Kind: { } kind })
        {
            if (S.Instrument.Kind != kind)
                Edit(i =>
                {
                    i.Kind = kind;
                    ForgetTuning(i); // per-note offsets are for the old size's notes
                });
            else
                S.ApplyInstrument();
            var bottom = RecorderInfo.Of(kind).BottomWrittenMidi;
            _status.Text = string.Format(Ui.T("That is a {0}: its lowest note is {1}."), Ui.T(kind.ToString()).ToLowerInvariant(), S.NoteName(bottom));
        }
        else
        {
            S.ApplyInstrument();
            _status.Text = guess is null
                ? Ui.T("Pip heard too little. Hold the note a bit longer, with every hole closed.")
                : Ui.T("That note is not the lowest note of a recorder. Check that every hole is closed (press gently with the pads of your fingers) and blow very softly.");
        }
    }

    public override void _ExitTree()
    {
        if (_listenUntil > 0)
            S.Audio.FrameReady -= OnFrame;
    }

    public void Refresh()
    {
        _refreshing = true;
        _picker.Clear();
        foreach (var i in S.Settings.Instruments)
        {
            _picker.AddItem(i.Name);
            _picker.SetItemMetadata(_picker.ItemCount - 1, i.Id);
            if (i.Id == S.Instrument.Id)
                _picker.Select(_picker.ItemCount - 1);
        }
        var active = S.Instrument;
        if (_name.Text != active.Name)
            _name.Text = active.Name;
        _kind.Selected = Array.IndexOf(Kinds, active.Kind);
        _fingering.Selected = Array.IndexOf(Systems, active.Fingering);
        _keyed.SetPressedNoSignal(active.Keyed);
        _referenceA.SetValueNoSignal(active.ReferenceA);
        _remove.Disabled = S.Settings.Instruments.Count <= 1;
        // Recorders tuned before calibration dates were recorded have offsets but no date.
        _status.Text = active.CalibratedAt is { } at
            ? string.Format(Ui.T("Tuning calibrated on {0:d MMM yyyy}: {1:+0;-0;0} cents overall."), at, active.MeasuredOffsetCents)
            : active.PerNoteOffsetCents.Count > 0
                ? string.Format(Ui.T("Tuning calibrated: {0:+0;-0;0} cents overall."), active.MeasuredOffsetCents)
                : Ui.T("Tuning not calibrated yet.");
        _refreshing = false;
    }

    private void Edit(Action<InstrumentProfile> change)
    {
        if (_refreshing)
            return;
        change(S.Instrument);
        S.ApplyInstrument();
        S.Save();
        Refresh();
        Changed?.Invoke();
    }

    private static void ForgetTuning(InstrumentProfile i)
    {
        i.PerNoteOffsetCents.Clear();
        i.MeasuredOffsetCents = 0;
        i.CalibratedAt = null;
    }

    private void Remove()
    {
        var dialog = new ConfirmationDialog
        {
            DialogText = string.Format(Ui.T("Remove \"{0}\" and its tuning? Recordings are kept."), S.Instrument.Name),
            OkButtonText = Ui.T("Remove"),
        };
        dialog.Confirmed += () => { S.RemoveInstrument(S.Instrument.Id); Refresh(); Changed?.Invoke(); dialog.QueueFree(); };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
    }
}
