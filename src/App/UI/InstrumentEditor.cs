using System;
using System.Linq;
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
    private bool _refreshing;

    private static readonly RecorderKind[] Kinds = Enum.GetValues<RecorderKind>();
    private static readonly FingeringSystem[] Systems = Enum.GetValues<FingeringSystem>();

    /// <summary>Raised after the active recorder changes or is edited.</summary>
    public event Action? Changed;

    public InstrumentEditor(bool showAdvanced = true)
    {
        AddThemeConstantOverride("separation", 10);
        _picker.SizeFlagsHorizontal = SizeFlags.ExpandFill;
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
        AddChild(Ui.HBox(16, Ui.Expand(Ui.VBox(6, Ui.Label("Size"), _kind)), Ui.Expand(Ui.VBox(6, Ui.Label("Fingering system"), _fingering))));
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
