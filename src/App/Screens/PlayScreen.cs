using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Pipit.App.Audio;
using Pipit.App.UI;
using Pipit.App.Views;
using Pipit.Core.Analysis;
using Pipit.Core.Game;
using Pipit.Core.Music;
using Pipit.Core.Practice;
using Pipit.Core.Recording;
using Pipit.Core.Scoring;
using Pipit.Core.Synthesis;

namespace Pipit.App.Screens;

/// <summary>
/// Plays one song. Rhythm mode follows the beat (count-in, metronome, live grading); Casual mode
/// waits on every note until it is played. "Listen" plays the tune first; after a run the
/// results show the grade and XP, and "Listen to your take" replays the recording against the bars.
/// </summary>
public partial class PlayScreen : Screen
{
    private enum Mode { Rhythm, Casual, Survival }
    private enum State { Ready, Preview, Running, Results, Replay }

    private readonly Song _song;
    private readonly List<BarNote> _barNotes;
    private readonly BarsView _bars = new();
    private readonly StaffView _staff = new();
    private readonly ProgressBar _progress = new() { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) };
    private readonly CheckBox _names = new() { ButtonPressed = true };
    private double _beat;
    private readonly FingeringDiagram _diagram = new();
    private readonly Label _nextLabel, _feedback, _speedLabel, _best;
    private readonly PanelContainer _intro, _results;
    private readonly VBoxContainer _resultsBox = Ui.VBox(8);
    private readonly Button _start, _listen;
    private readonly OptionButton _modeOption, _difficultyOption;
    private readonly HSlider _speed = new() { MinValue = 20, MaxValue = 200, Step = 5, Value = 100, CustomMinimumSize = new Vector2(170, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter };
    private readonly CheckBox _metronome = new() { ButtonPressed = true };
    private readonly CheckBox _help = new() { ButtonPressed = true };
    private readonly AudioStreamPlayer _player = new();

    private State _state = State.Ready;
    private Mode _mode = Mode.Rhythm;
    private bool Rhythmic => _mode is Mode.Rhythm or Mode.Survival;

    // Survival: life, score and combo shown above the music; the leaderboard rank after a run.
    private readonly HighwayView _highway = new();
    private readonly ProgressBar _life = new() { MinValue = 0, MaxValue = Survival.MaxLife, Value = Survival.MaxLife, ShowPercentage = false, CustomMinimumSize = new Vector2(260, 18) };
    private readonly Label _survivalLabel = new();
    private HBoxContainer _survivalBar = null!;
    private SurvivalState? _survival;
    private int? _rank;
    // Everyone's survival runs on this song on this computer; loaded when first needed.
    private List<ScoreEntry>? _board;
    private ScoreEntry? _lastEntry;
    private List<ScoreEntry> Board => _board ??= S.ComputerBoard(_song.Id);
    // Survival callouts over the music: the grade of each note, the streak, milestones.
    private readonly Label _gradeLabel = new() { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly Label _streakLabel = new() { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly Label _calloutLabel = new() { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
    private Tween? _gradeTween, _calloutTween;
    private int _calledOut;
    private Tolerances _tolerances = Tolerances.Normal;
    private double _tempoFactor = 1;

    // Rhythm run.
    private double _spb, _endTime, _latency, _nextGrade;
    // Beat <-> clock time; the speed dial can change it while the song plays.
    private TempoMap _map = new(0, 0.5);
    // The slowest speed used during the run: results, stars and the leaderboard count that one.
    private double _runTempo = 1;
    private double _endBeat, _firstBeat;
    // A speed change is applied once the dial has rested for a moment (dragging steps through values).
    private double _tempoChangeAt = double.NaN;
    private List<ExpectedNote> _expected = new();
    private NoteSegmenter _segmenter = new();
    private readonly List<DetectedNote> _detected = new();
    private GradedRun? _performance;

    // Casual run.
    private CasualMode? _casual;
    private double _casualStart;
    private float _casualBeat;

    // Audio of the run, for replay.
    private List<float> _take = new();
    private double _takeStart = double.NaN;
    private float[]? _takeSamples;

    // Preview.
    private double _previewLead;

    public override string Title => _song.Title(S.Settings.Language);
    public override bool ShowTitle => false;

    private readonly bool _fromPath;

    private readonly Song _original;
    private readonly int _transposed;
    private readonly int _partIndex;
    private readonly CheckBox _band = new();
    private float[]? _bandSound;
    private (int Part, double Tempo)? _bandFor;
    private double _bandFrom;
    // The band of the current run (null without one) and a counter that drops stale renders.
    private SoundVoice? _bandVoice;
    private int _bandGeneration;

    public PlayScreen(Song song, bool fromPath = false, int part = 0)
    {
        _fromPath = fromPath;
        _original = song;
        _partIndex = part;
        _song = part > 0 ? song.WithPartFirst(part) : song;
        // Moved to fit the player's recorder when needed (an alto piece on a soprano...).
        if (Transposition.FitFor(_song, S.Recorder) is { } fit)
        {
            _song = fit.Song;
            _transposed = fit.Semitones;
        }
        song = _song;
        _barNotes = song.Melody.Performance.Select(n => new BarNote(n.Index, n.Pitch.Midi, n.StartBeat, n.Beats) { Pitch = n.Pitch }).ToList();
        var language = S.Settings.Language;

        var titleLabel = Ui.Label(Title, PipitTheme.Heading + 4, PipitTheme.Ink);
        titleLabel.AddThemeFontOverride("font", PipitTheme.BoldFont);
        var header = Ui.HBox(16, titleLabel, Ui.VBox(0, Ui.Spacer(), Ui.Label(Ui.Composer(song.Composer), PipitTheme.Body, PipitTheme.InkSoft)), Ui.Spacer());
        _best = Ui.Label("", 18, PipitTheme.Accent);
        if (_transposed != 0)
            header.AddChild(Ui.Label(TransposedText(_transposed), 16, PipitTheme.InkSoft));
        header.AddChild(_best);

        _intro = Ui.Card(Ui.VBox(6,
            Ui.Text(song.AboutText(language), 17, PipitTheme.Ink),
            Ui.Text(song.TipText(language) is { Length: > 0 } tip ? Ui.T("Tip: ") + tip : "", 16, PipitTheme.Accent)));

        _bars.BarBeats = song.Time.BarBeats;
        _bars.NoteLabel = S.NoteName;
        _bars.SetNotes(_barNotes);
        _staff.NoteLabel = p => S.NoteName(p);
        _staff.SetSong(song, S.Instrument.Kind);
        SetBeat(_barNotes.Min(n => n.StartBeat) - 0.5);
        Ui.Expand(_bars, true, true);
        Ui.Expand(_staff, true, true);
        _highway.NoteLabel = S.NoteName;
        _highway.SetNotes(_barNotes);
        Ui.Expand(_highway, true, true);
        var view = S.Settings.SongView switch { "score" => 1, "highway" => 2, _ => 0 };
        var score = view == 1;
        _bars.Visible = view == 0;
        _staff.Visible = view == 1;
        _highway.Visible = view == 2;
        _life.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = PipitTheme.Good, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6 });
        _survivalLabel.AddThemeFontSizeOverride("font_size", 20);
        _survivalBar = Ui.HBox(12, Ui.Label("❤", 20, PipitTheme.Bad), _life, _survivalLabel);
        _survivalBar.Visible = false;
        var views = Ui.VBox(4, _survivalBar, _bars, _staff, _highway, _progress);
        views.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        // The callouts float over the music, a third of the way down, and never take a click.
        foreach (var (label, size) in new[] { (_gradeLabel, 46), (_streakLabel, 22), (_calloutLabel, 30) })
        {
            label.AddThemeFontOverride("font", PipitTheme.BoldFont);
            label.AddThemeFontSizeOverride("font_size", size);
            label.AddThemeConstantOverride("outline_size", 8);
            label.AddThemeColorOverride("font_outline_color", PipitTheme.Paper);
        }
        var callouts = Ui.VBox(0, _gradeLabel, _streakLabel, _calloutLabel);
        callouts.MouseFilter = Control.MouseFilterEnum.Ignore;
        callouts.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        callouts.AnchorTop = callouts.AnchorBottom = 0.22f;
        callouts.GrowHorizontal = Control.GrowDirection.Both;
        var stage = Ui.Expand(new Control { MouseFilter = Control.MouseFilterEnum.Ignore }, true, true);
        stage.AddChild(views);
        stage.AddChild(callouts);
        views.MinimumSizeChanged += () => stage.CustomMinimumSize = views.GetCombinedMinimumSize();
        _nextLabel = Ui.Label("", 18, PipitTheme.InkSoft, HorizontalAlignment.Center);
        var side = Ui.VBox(4, Ui.MinSize(_diagram, 200, 330), _nextLabel);
        _feedback = Ui.Text("", 20, PipitTheme.Ink, HorizontalAlignment.Center);

        _modeOption = Ui.Options(new[] { "Rhythm", "Casual", "Survival" }, 0, ix => { _mode = (Mode)ix; UpdateControls(); });
        _modeOption.TooltipText = Ui.T("Rhythm: play along with the beat. Casual: the song waits for each note. Survival: keep going until your life runs out, for a high score.");
        _difficultyOption = Ui.Options(Tolerances.All.Select(t => t.Name).ToArray(), 1, ix => _tolerances = Tolerances.All[ix]);
        _speedLabel = Ui.Label("", PipitTheme.Small + 1, PipitTheme.Ink);
        _speedLabel.CustomMinimumSize = new Vector2(44, 0);
        _speed.ValueChanged += v =>
        {
            _tempoFactor = v / 100;
            UpdateControls();
            if (_state == State.Running && Rhythmic)
                _tempoChangeAt = S.Audio.Now + 0.3;
        };
        _metronome.Text = Ui.T("Metronome");
        _help.Text = Ui.T("Fingering");
        _help.Toggled += on => side.Visible = on;
        _listen = Ui.Button("♪ Listen", ToggleListen, PipitTheme.Body);
        _listen.CustomMinimumSize = new Vector2(120, 52);
        _start = Ui.Primary(Ui.Button("▶ Start", ToggleRun, PipitTheme.Lead));
        _start.CustomMinimumSize = new Vector2(160, 56);
        var viewOption = Ui.Options(new[] { "Bars", "Score", "Highway" }, view, ix =>
        {
            _bars.Visible = ix == 0;
            _staff.Visible = ix == 1;
            _highway.Visible = ix == 2;
            _names.Visible = ix == 1;
            S.Settings.SongView = ix switch { 1 => "score", 2 => "highway", _ => "bars" };
            S.Save();
        });
        _names.Text = Ui.T("Names");
        _names.Visible = score;
        _names.Toggled += on => _staff.ShowNames = on;
        // Your part (rounds, duets, consort pieces) and the band playing the others.
        var partOption = Ui.Options(_original.Parts.Select(p => Ui.PartName(p.Name)).ToArray(), _partIndex, ix =>
        {
            if (_state is State.Ready or State.Results)
            {
                Main.Instance.Back();
                Main.Instance.Show(new PlayScreen(_original, _fromPath, ix));
            }
        });
        partOption.Visible = _original.Parts.Count > 1;
        partOption.TooltipText = Ui.T("Which part you play; the band plays the others.");
        _band.Text = Ui.T("Band");
        _band.Visible = _song.HasOtherParts;
        _band.TooltipText = Ui.T("The app plays the other parts. Use headphones, or the microphone hears the band.");
        _band.ButtonPressed = _song.HasOtherParts && S.Settings.BandOn && S.Machine.HeadphonesConfirmed && Synth.SoundFontInstalled;
        _band.Toggled += OnBandToggled;
        if (_band.ButtonPressed)
            _ = Synth.Renderer(); // start loading the SoundFont now
        // The options wrap onto a second line in a narrow window instead of widening the screen.
        var options = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        options.AddThemeConstantOverride("h_separation", 10);
        options.AddThemeConstantOverride("v_separation", 8);
        // The row holds what changes from run to run; the settings chosen once sit under Options.
        var more = new PopupPanel();
        more.AddThemeStyleboxOverride("panel", PipitTheme.Box(Colors.White, PipitTheme.WoodEdge with { A = 0.5f }, 14, radius: 12, line: 1));
        more.AddChild(Ui.VBox(10, _names, _metronome, _help, Ui.HBox(10, Ui.Label("Strictness", PipitTheme.Small + 2, PipitTheme.InkSoft), _difficultyOption)));
        var moreButton = Ui.Button("Options ▾", () => { }, PipitTheme.Small + 2);
        moreButton.Pressed += () =>
        {
            // Opens upwards: the button sits at the bottom of the screen.
            var size = (Vector2I)more.GetContentsMinimumSize() + new Vector2I(28, 28);
            more.Popup(new Rect2I((Vector2I)moreButton.GlobalPosition - new Vector2I(0, size.Y + 8), size));
        };
        AddChild(more);
        foreach (var c in new Control[] { viewOption, partOption, _band, _modeOption, Ui.Label("Speed", PipitTheme.Small + 1, PipitTheme.InkSoft), _speed, _speedLabel, moreButton })
        {
            if (c is CheckBox or OptionButton)
                c.AddThemeFontSizeOverride("font_size", PipitTheme.Small + 2);
            options.AddChild(c);
        }
        var controls = Ui.HBox(14, options, Ui.VBox(0, Ui.Spacer(), _listen, Ui.Spacer()), _start);

        // Results (and the optional analysis) scroll inside their own area, which shares the
        // height with the music instead of pushing the controls off the screen.
        var resultsScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.9f };
        resultsScroll.AddChild(Ui.Expand(_resultsBox));
        _results = Ui.Card(resultsScroll);
        _results.SizeFlagsVertical = SizeFlags.ExpandFill;
        _results.SizeFlagsStretchRatio = 0.9f;
        _results.Visible = false;

        AddChild(Ui.VBox(10, header, _intro, Ui.Expand(Ui.HBox(16, stage, side), true, true), _feedback, _results, controls));
        AddChild(_player);
        _player.Finished += OnPlayerFinished;
        UpdateControls();
    }

    public override void Enter()
    {
        S.PreferFlats = Core.Notation.KeySignature.Parse(_song.Key).Fifths < 0;
        S.ApplyInstrument();
        _diagram.Keyed = S.Instrument.Keyed;
        S.Audio.FrameReady += OnFrame;
        S.Audio.SamplesCaptured += OnSamples;
        ShowBest();
        ShowFingeringAt(_barNotes.Min(n => n.StartBeat));
    }

    public override void Exit()
    {
        S.PreferFlats = false;
        S.Audio.FrameReady -= OnFrame;
        S.Audio.SamplesCaptured -= OnSamples;
        S.Clicks.Stop();
        _player.Stop();
    }

    /// <summary>Top right: the player's best stars, or in survival the high score to beat on this computer.</summary>
    private void ShowBest()
    {
        var record = S.SongRecord(_song.Id);
        if (_mode == Mode.Survival)
            _best.Text = Board.FirstOrDefault() is { } top
                ? "👑 " + string.Format(Ui.T("High score {0:N0} ({1})"), top.Score, top.Player)
                : Ui.T("No high score yet: be the first!");
        else
            _best.Text = record.Plays == 0 ? "" : $"{Ui.T("Best")}: {new string('★', record.BestStars)}{new string('☆', 3 - record.BestStars)} {record.BestAccuracy:0}%";
    }

    private void UpdateControls()
    {
        ShowBest();
        _speedLabel.Text = $"{_speed.Value:0}%";
        var busy = _state is State.Running or State.Preview or State.Replay;
        _start.Text = Ui.T(_state == State.Running ? "■ Stop" : _state == State.Results ? "▶ Again" : "▶ Start");
        _survivalBar.Visible = _mode == Mode.Survival && _state is State.Running or State.Results;
        _start.Disabled = _state is State.Preview or State.Replay;
        _listen.Text = Ui.T(_state == State.Preview ? "■ Stop" : "♪ Listen");
        _listen.Disabled = _state is State.Running or State.Replay;
        _modeOption.Disabled = busy;
        // The speed can change while a rhythm run plays (not while listening or replaying).
        _speed.Editable = _state is not (State.Preview or State.Replay) && !(_state == State.Running && _mode == Mode.Casual);
        _difficultyOption.Disabled = busy || _mode == Mode.Casual;
        _metronome.Disabled = busy || _mode == Mode.Casual;
        _speed.Visible = _speedLabel.Visible = true;
    }

    private void SetState(State state)
    {
        _state = state;
        _intro.Visible = state == State.Ready;
        _results.Visible = state is State.Results or State.Replay;
        UpdateControls();
    }

    // ---- both views get every update ------------------------------------------------------

    private IEnumerable<ISongView> Views => new ISongView[] { _bars, _staff, _highway };

    private void SetBeat(double beat)
    {
        _beat = beat;
        foreach (var v in Views)
            v.CurrentBeat = beat;
        var first = _barNotes.Min(n => n.StartBeat);
        var last = _barNotes.Max(n => n.StartBeat + n.Beats);
        _progress.Value = _state == State.Running && _mode == Mode.Casual && _casual is not null
            ? _casual.Index / (double)_barNotes.Count
            : Math.Clamp((beat - first) / (last - first), 0, 1);
    }

    private void SetMark(int index, NoteMark mark)
    {
        foreach (var v in Views)
            v.SetMark(index, mark);
    }

    private void ClearMarks()
    {
        foreach (var v in Views)
            v.ClearMarks();
    }

    private void SetOverlay(string text)
    {
        foreach (var v in Views)
            v.Overlay = text;
    }

    private void AddTrace(double beat, double written)
    {
        foreach (var v in Views)
            v.AddTrace(beat, written);
    }

    private void SetWaiting(int? index)
    {
        foreach (var v in Views)
            v.WaitingFor = index;
    }

    // ---- shared helpers ------------------------------------------------------------------

    private double TimeToBeat(double time) => _map.TimeToBeat(time);
    private double BeatToTime(double beat) => _map.BeatToTime(beat);
    private int Sounding(int written) => S.Recorder.WrittenToSounding(written);

    /// <summary>The fingering of the note sounding at (or next after) a beat, plus the following note's name.</summary>
    private void ShowFingeringAt(double beat)
    {
        var current = _barNotes.FirstOrDefault(n => beat < n.StartBeat + n.Beats) ?? _barNotes[^1];
        _diagram.Fingering = S.FingeringFor(current.WrittenMidi);
        var next = _barNotes.FirstOrDefault(n => n.StartBeat > current.StartBeat);
        _nextLabel.Text = S.NoteName(current.WrittenMidi) + (next is null ? "" : "   " + Ui.T("then") + " " + S.NoteName(next.WrittenMidi));
    }

    private void OnSamples(float[] samples, double time)
    {
        if (_state != State.Running)
            return;
        if (double.IsNaN(_takeStart))
            _takeStart = time;
        _take.AddRange(samples);
    }

    // ---- starting and stopping ------------------------------------------------------------

    private void ToggleRun()
    {
        if (_state == State.Running)
        {
            Finish(aborted: true);
            return;
        }
        _player.Stop();
        ClearMarks();
        _take = new List<float>();
        _takeStart = double.NaN;
        _feedback.Text = "";
        if (Rhythmic && BandActive)
            StartWithBand();
        else if (Rhythmic)
            StartRhythm(null);
        else
            StartCasual();
    }

    /// <summary>"An octave lower", "3 semitones higher"... to fit this recorder.</summary>
    public static string TransposedText(int semitones) => Math.Abs(semitones) == 12
        ? Ui.T(semitones > 0 ? "An octave higher, to fit your recorder" : "An octave lower, to fit your recorder")
        : string.Format(Ui.T(semitones > 0 ? "{0} semitones higher, to fit your recorder" : "{0} semitones lower, to fit your recorder"), Math.Abs(semitones));

    private bool BandActive => _band.ButtonPressed && _band.Visible && Synth.SoundFontInstalled;

    /// <summary>Everything the band plays: the other parts and the accompaniment.</summary>
    private List<SongPart> BandParts => _song.Parts.Skip(1).Concat(_song.Accompaniment).ToList();

    private async void StartWithBand()
    {
        var tempo = _tempoFactor;
        if (_bandSound is null || _bandFor != (_partIndex, tempo))
        {
            _start.Disabled = true;
            _feedback.Text = Ui.T("Getting the band ready…");
            var parts = BandParts;
            _bandFrom = parts.SelectMany(p => p.Performance).Select(n => n.StartBeat).DefaultIfEmpty(0).Min();
            _bandSound = await Synth.RenderAsync(_song, parts, _song.SecondsPerBeat(tempo), _bandFrom, 0, natural: true);
            _bandFor = (_partIndex, tempo);
            _start.Disabled = false;
            _feedback.Text = "";
            if (!IsInsideTree() || _state != State.Ready && _state != State.Results)
                return;
        }
        StartRhythm(_bandSound);
    }

    private void OnBandToggled(bool on)
    {
        if (on && !Synth.SoundFontInstalled)
        {
            _band.SetPressedNoSignal(false);
            _feedback.Text = Ui.T("The band needs the SoundFont: run scripts/fetch-soundfont.sh, then restart Pipit.");
            return;
        }
        if (on && !S.Machine.HeadphonesConfirmed)
        {
            var dialog = new ConfirmationDialog
            {
                DialogText = Ui.T("The band plays the other parts while you play. Please use headphones: through the speakers the microphone would hear the band and take its notes for yours."),
                OkButtonText = Ui.T("I'm using headphones"),
            };
            dialog.Confirmed += () => { S.Machine.HeadphonesConfirmed = true; S.Settings.BandOn = true; S.Save(); dialog.QueueFree(); BandChangedDuringRun(true); };
            dialog.Canceled += () => { _band.SetPressedNoSignal(false); dialog.QueueFree(); };
            AddChild(dialog);
            dialog.PopupCentered();
            return;
        }
        S.Settings.BandOn = on;
        S.Save();
        if (on)
            _ = Synth.Renderer();
        BandChangedDuringRun(on);
    }

    /// <summary>The band switched on or off while a rhythm run plays: it fades out, or comes in.</summary>
    private void BandChangedDuringRun(bool on)
    {
        if (_state != State.Running || !Rhythmic)
            return;
        if (_bandVoice is { } voice)
            voice.Muted = !on;
        else if (on && BandActive)
            JoinBand();
    }

    private void StartRhythm(float[]? band)
    {
        _spb = _song.SecondsPerBeat(_tempoFactor);
        _runTempo = _tempoFactor;
        _tempoChangeAt = double.NaN;
        _latency = (S.DeviceLatencyMs ?? 0) / 1000;
        _firstBeat = _barNotes.Min(n => n.StartBeat);
        // With the band, the count-in comes before whoever starts first (in a round, the
        // player may come in later), and the run lasts until the last part ends.
        var earliest = band is null ? _firstBeat : Math.Min(_firstBeat, _bandFrom);
        var barBeats = (int)Math.Round(_song.Time.BarBeats);
        var firstClick = (int)Math.Floor(earliest) - barBeats; // one bar of count-in
        _map = new TempoMap(S.Audio.Now + 1.0 - firstClick * _spb, _spb);
        _endBeat = _barNotes.Max(n => n.StartBeat + n.Beats);
        _bandVoice = null;
        _bandGeneration++;
        if (band is not null)
        {
            _bandVoice = S.Clicks.ScheduleSound(band, BeatToTime(_bandFrom));
            _endBeat = Math.Max(_endBeat, _song.EndBeat);
        }
        ScheduleClicks(firstClick);
        RetimeNotes();

        _segmenter = new NoteSegmenter();
        _detected.Clear();
        _performance = null;
        _nextGrade = 0;
        _survival = null;
        _rank = null;
        _life.Value = Survival.MaxLife;
        _survivalLabel.Text = "";
        _highway.Hud = "";
        _calledOut = 0;
        _gradeLabel.Text = _streakLabel.Text = _calloutLabel.Text = "";
        SetState(State.Running);
    }

    /// <summary>Count-in and metronome clicks from <paramref name="fromBeat"/> on, at the current tempo map.</summary>
    private void ScheduleClicks(int fromBeat)
    {
        var barBeats = (int)Math.Round(_song.Time.BarBeats);
        var lastClick = _metronome.ButtonPressed ? Math.Ceiling(_endBeat) - 1 : Math.Floor(_firstBeat) - 1;
        var clicks = new List<(double, bool)>();
        for (var b = fromBeat; b <= lastClick; b++)
            clicks.Add((BeatToTime(b), (b % barBeats + barBeats) % barBeats == 0));
        S.Clicks.Schedule(clicks);
    }

    /// <summary>The notes' expected times and the end of the run, from the tempo map.</summary>
    private void RetimeNotes()
    {
        _expected = _song.Melody.Performance.Select(n => new ExpectedNote(n.Index, Sounding(n.Pitch.Midi), BeatToTime(n.StartBeat), BeatToTime(n.EndBeat))).ToList();
        _endTime = BeatToTime(_endBeat) + _latency + 0.7;
    }

    /// <summary>
    /// The speed dial moved during a run: from the beat being played now, everything follows the
    /// new tempo. Notes already played keep their times; the clicks still to come and the
    /// expected notes move, and the band is rendered again from the next beat.
    /// </summary>
    private void ApplyTempoChange()
    {
        _tempoChangeAt = double.NaN;
        var spb = _song.SecondsPerBeat(_tempoFactor);
        if (Math.Abs(spb - _map.SecondsPerBeat) < 1e-9)
            return;
        var beat = TimeToBeat(S.Audio.Now);
        _map.Change(beat, spb);
        _spb = spb;
        _runTempo = Math.Min(_runTempo, _tempoFactor);
        S.Clicks.ClearClicks();
        ScheduleClicks((int)Math.Floor(beat) + 1);
        RetimeNotes();
        if (_bandVoice is { } old)
        {
            old.Stop();
            _bandVoice = null;
            JoinBand(muted: old.Muted);
        }
    }

    /// <summary>
    /// Renders the band from the next beat on at the current tempo and starts it in step with the
    /// run. Rendering takes a moment; the band comes in as soon as it is ready, at the right place.
    /// </summary>
    private async void JoinBand(bool muted = false)
    {
        var generation = ++_bandGeneration;
        var bandStart = BandParts.SelectMany(p => p.Performance).Select(n => n.StartBeat).DefaultIfEmpty(0).Min();
        var from = Math.Max(bandStart, Math.Floor(TimeToBeat(S.Audio.Now)) + 1);
        var parts = BandParts.Select(p => p with { Performance = p.Performance.Where(n => n.StartBeat >= from).ToList() }).ToList();
        var sound = await Synth.RenderAsync(_song, parts, _map.SecondsPerBeat, from, 0, natural: true);
        if (!IsInsideTree() || _state != State.Running || generation != _bandGeneration)
            return;
        _bandVoice = S.Clicks.ScheduleSound(sound, BeatToTime(from), muted);
        if (_song.EndBeat > _endBeat)
        {
            _endBeat = _song.EndBeat;
            RetimeNotes();
        }
    }

    private void StartCasual()
    {
        _casual = new CasualMode(_song.Melody.Performance.Select(n => Sounding(n.Pitch.Midi)).ToList(), S.Instrument.MeasuredOffsetCents);
        _casualStart = S.Audio.Now;
        _casualBeat = (float)_barNotes[0].StartBeat;
        SetMark(0, NoteMark.Current);
        SetWaiting(0);
        _feedback.Text = Ui.T("Take your time: play each note when you have found it.");
        SetState(State.Running);
    }

    // ---- live analysis -----------------------------------------------------------------------

    private void OnFrame(PitchFrame frame)
    {
        if (_state != State.Running)
            return;
        if (_mode == Mode.Casual)
        {
            CasualFrame(frame);
            return;
        }
        // Draw what was played where it was meant to be: shift the trace back by the latency.
        var written = frame.Voiced ? frame.SmoothedMidi - S.Recorder.OctaveTransposition * 12 : double.NaN;
        AddTrace(TimeToBeat(frame.Time - _latency), written);
        if (_segmenter.Push(frame) is { } note)
            _detected.Add(note);
    }

    private void CasualFrame(PitchFrame frame)
    {
        var casual = _casual!;
        AddTrace(_beat, frame.Voiced ? frame.SmoothedMidi - S.Recorder.OctaveTransposition * 12 : double.NaN);
        var step = casual.Push(frame);
        if (step is not null)
        {
            SetMark(step.Index, step.FirstTry ? NoteMark.Perfect : NoteMark.Good);
            if (casual.Finished)
            {
                Finish(aborted: false);
                return;
            }
            SetMark(casual.Index, NoteMark.Current);
            SetWaiting(casual.Index);
            _feedback.Text = step.FirstTry ? Ui.T("Yes!") : Ui.T("Got it.");
            _feedback.Modulate = PipitTheme.Good;
        }
        else if (casual.LastWrong is { } wrong)
        {
            var target = _barNotes[casual.Index].WrittenMidi;
            _feedback.Text = string.Format(Ui.T("That was {0}. Look for {1}."), S.NoteName(S.Recorder.SoundingToWritten(wrong)), S.NoteName(target));
            _feedback.Modulate = PipitTheme.Bad;
        }
    }

    public override void _Process(double delta)
    {
        var now = S.Audio.Now;
        switch (_state)
        {
            case State.Running when Rhythmic:
                if (now >= _tempoChangeAt)
                    ApplyTempoChange();
                SetBeat(TimeToBeat(now));
                var firstNote = BeatToTime(_firstBeat);
                SetOverlay(now < firstNote ? Math.Ceiling((firstNote - now) / _spb).ToString("0") : "");
                ShowFingeringAt(_beat);
                if (now >= _nextGrade)
                {
                    _nextGrade = now + 0.1;
                    var live = Grader.Grade(_expected, LiveNotes(), _tolerances, _latency, S.Instrument.MeasuredOffsetCents, _runTempo, until: now);
                    ApplyMarks(live, live: true);
                    if (_mode == Mode.Survival)
                    {
                        UpdateSurvival(Survival.Evaluate(live.Notes));
                        CallOut(live.Notes);
                        if (_survival is { Alive: false })
                        {
                            Finish(aborted: false);
                            break;
                        }
                    }
                }
                if (now >= _endTime)
                    Finish(aborted: false);
                break;
            case State.Running when _mode == Mode.Casual && _casual is not null:
                // Glide to the note being waited for.
                var target = (float)_barNotes[Math.Min(_casual.Index, _barNotes.Count - 1)].StartBeat - 0.3f;
                _casualBeat += (target - _casualBeat) * (float)Math.Min(1, delta * 6);
                SetBeat(_casualBeat);
                ShowFingeringAt(_barNotes[Math.Min(_casual.Index, _barNotes.Count - 1)].StartBeat);
                break;
            case State.Preview:
                SetBeat(_previewFrom + (_player.GetPlaybackPosition() - _previewLead) / _spb);
                ShowFingeringAt(_beat);
                break;
            case State.Replay:
                SetBeat(TimeToBeat(_takeStart + _player.GetPlaybackPosition() - _latency));
                ShowFingeringAt(_beat);
                break;
        }
    }

    /// <summary>The grade of each newly judged note, the streak, and milestones or a lost streak.</summary>
    private void CallOut(IReadOnlyList<NoteJudgement> judgements)
    {
        var fresh = Callouts.From(judgements, _calledOut);
        _calledOut = judgements.Count;
        if (fresh.Count == 0)
            return;
        var last = fresh[^1];
        var (text, colour) = last.Grade switch
        {
            Grade.Perfect => (Ui.T("Perfect!"), PipitTheme.Accent),
            Grade.Great => (Ui.T("Great!"), PipitTheme.Good),
            Grade.Good => (Ui.T("Good"), PipitTheme.Good),
            Grade.Early => (Ui.T("Early"), PipitTheme.Warn),
            Grade.Late => (Ui.T("Late"), PipitTheme.Warn),
            Grade.Miss => (Ui.T("Miss"), PipitTheme.Bad),
            _ => (Ui.T("Wrong note"), PipitTheme.Bad),
        };
        _gradeTween = Pop(_gradeLabel, text, colour, _gradeTween, hold: 0.35, grow: last.Grade == Grade.Perfect ? 1.35f : 1.15f);
        _streakLabel.Text = last.Streak >= 3 ? string.Format(Ui.T("{0} in a row"), last.Streak) + (last.Multiplier > 1 ? $"   ×{last.Multiplier}" : "") : "";
        _streakLabel.AddThemeColorOverride("font_color", last.Multiplier > 1 ? PipitTheme.Accent : PipitTheme.Ink);
        // A milestone or a lost streak among the new notes gets the big line.
        if (fresh.LastOrDefault(c => c.Milestone is not null) is { Milestone: { } m } milestone)
            _calloutTween = Pop(_calloutLabel, m <= 30 ? string.Format(Ui.T("Streak bonus: points ×{0}!"), milestone.Multiplier) : string.Format(Ui.T("{0} in a row!"), m),
                PipitTheme.Accent, _calloutTween, hold: 1.2, grow: 1.4f);
        else if (fresh.LastOrDefault(c => c.StreakLost is not null) is { StreakLost: { } lost })
            _calloutTween = Pop(_calloutLabel, string.Format(Ui.T("Streak of {0} lost"), lost), PipitTheme.Bad, _calloutTween, hold: 1.0, grow: 1.1f);
    }

    /// <summary>Shows a callout that bounces in and fades out.</summary>
    private Tween Pop(Label label, string text, Color colour, Tween? running, double hold, float grow)
    {
        running?.Kill();
        label.Text = text;
        label.AddThemeColorOverride("font_color", colour);
        label.Modulate = Colors.White;
        var size = label.GetCombinedMinimumSize();
        label.PivotOffset = new Vector2(Math.Max(label.Size.X, size.X) / 2, size.Y / 2);
        label.Scale = new Vector2(grow, grow);
        var tween = CreateTween();
        tween.TweenProperty(label, "scale", Vector2.One, 0.18).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenInterval(hold);
        tween.TweenProperty(label, "modulate:a", 0.0, 0.35);
        return tween;
    }

    private void UpdateSurvival(SurvivalState state)
    {
        _survival = state;
        _life.Value = state.Life;
        var fill = (StyleBoxFlat)_life.GetThemeStylebox("fill");
        fill.BgColor = state.Life > 50 ? PipitTheme.Good : state.Life > 25 ? PipitTheme.Warn : PipitTheme.Bad;
        _survivalLabel.Text = string.Format(Ui.T("{0:N0} points   combo {1}   ×{2}"), state.Score, state.Combo, state.Multiplier);
        _highway.Hud = $"{state.Score:N0}   ×{state.Multiplier}";
    }

    private List<DetectedNote> LiveNotes()
    {
        var notes = _detected.ToList();
        if (_segmenter.Current is { } current)
            notes.Add(current);
        return notes;
    }

    private void ApplyMarks(GradedRun performance, bool live)
    {
        foreach (var j in performance.Notes)
            SetMark(j.Expected.Index, Mark(j));
        if (live && performance.Notes.Count > 0)
        {
            var last = performance.Notes[^1];
            (_feedback.Text, _feedback.Modulate) = last.Result switch
            {
                NoteResult.Missed => (Ui.T("Missed"), PipitTheme.InkSoft),
                NoteResult.WrongNote => (string.Format(Ui.T("Wrong note: that was {0}"), S.NoteName(S.Recorder.SoundingToWritten(last.Played!.Semitone))), PipitTheme.Bad),
                _ => (Ui.T(last.Timing.ToString()!) + (last.InTune ? "" : " · " + Ui.T(last.CentsOff > 0 ? "a bit high" : "a bit low")),
                      last.Timing is Timing.Perfect or Timing.Great ? PipitTheme.Good : PipitTheme.Warn),
            };
        }
    }

    private static NoteMark Mark(NoteJudgement j) => j.Result switch
    {
        NoteResult.Missed => NoteMark.Missed,
        NoteResult.WrongNote => NoteMark.Wrong,
        _ => j.Timing is Timing.Perfect or Timing.Great && j.InTune ? NoteMark.Perfect : NoteMark.Good,
    };

    // ---- finishing ------------------------------------------------------------------------------

    private void Finish(bool aborted)
    {
        S.Clicks.Stop();
        _bandVoice = null;
        _bandGeneration++;
        _tempoChangeAt = double.NaN;
        SetWaiting(null);
        SetOverlay("");
        _takeSamples = _take.ToArray();
        if (aborted)
        {
            SetState(State.Ready);
            _feedback.Text = "";
            return;
        }
        SaveTake();
        var record = S.SongRecord(_song.Id);
        var minutes = _takeSamples.Length / (double)S.Audio.SampleRate / 60;
        List<XpLine> lines;
        if (Rhythmic)
        {
            if (_segmenter.Flush() is { } last)
                _detected.Add(last);
            _performance = Grader.Grade(_expected, _detected, _tolerances, _latency, S.Instrument.MeasuredOffsetCents, _runTempo);
            S.RecordNotes(_song, _performance);
            ApplyMarks(_performance, live: false);
            if (_mode == Mode.Survival)
            {
                UpdateSurvival(Survival.Evaluate(_performance.Notes));
                var st = _survival!;
                var judged = _performance.Notes.OrderBy(n => n.Expected.Start).Take(st.Judged).ToList();
                var accuracy = judged.Count == 0 ? 0 : judged.Average(n => n.Score);
                _lastEntry = new ScoreEntry(S.Player.Name, st.Score, accuracy, st.Judged / (double)_expected.Count, st.BestCombo, _runTempo, DateTime.Now);
                Leaderboard.Add(S.Progress, PlayerProgress.SongKey(_song.Id, S.Instrument.Kind.ToString()), _lastEntry);
                // The rank that counts is on this computer's board, among all players.
                _board = null;
                var at = Board.IndexOf(_lastEntry);
                _rank = at >= 0 ? at + 1 : null;
                lines = XpRules.SurvivalRun(record, judged.Count(n => n.Result == NoteResult.Hit), st.Score, st.Alive, DateTime.Now);
            }
            else
                lines = XpRules.RhythmRun(record, _performance.Hits, _performance.Stars, _performance.Accuracy, DateTime.Now);
        }
        else
        {
            var casual = _casual!;
            lines = XpRules.CasualRun(record, casual.Steps.Count, casual.Steps.Count(s => s.FirstTry), casual.Accuracy, DateTime.Now);
        }
        lines.Add(XpRules.Practice(minutes));
        S.Progress.PracticeMinutes += minutes;
        var award = S.AwardXp(lines, new AchievementEvent
        {
            SongId = _song.Id, Casual = _mode == Mode.Casual, Run = _mode == Mode.Rhythm ? _performance : null, Survival = _survival,
            CasualAccuracy = _casual?.Accuracy ?? 0, TempoFactor = Rhythmic ? _runTempo : _tempoFactor,
        });
        ShowResults(award);
        SetState(State.Results);
        _feedback.Text = "";
    }

    private void ShowResults(XpAward award)
    {
        foreach (var child in _resultsBox.GetChildren())
            child.QueueFree();
        var summary = Ui.VBox(4);
        if (_mode == Mode.Survival && _survival is { } st)
        {
            summary.AddChild(Ui.HBox(16, Ui.Label($"{st.Score:N0}", 44, PipitTheme.Accent), Ui.Label(Ui.T("points"), 20, PipitTheme.InkSoft)));
            summary.AddChild(Ui.Label(st.Alive
                ? Ui.T("You survived to the end!")
                : string.Format(Ui.T("Out of life after {0:0}% of the song."), 100.0 * st.Judged / _expected.Count), 18, st.Alive ? PipitTheme.Good : PipitTheme.Bad));
            if (Callouts.FullCombo(st, _expected.Count))
                summary.AddChild(Ui.Label(Ui.T("Full combo! Every note hit."), 20, PipitTheme.Accent));
            if (_rank == 1 && Board.Count > 1)
                summary.AddChild(Ui.Label("👑 " + Ui.T("New high score on this computer!"), 20, PipitTheme.Accent));
            summary.AddChild(Ui.Label(string.Format(Ui.T("Best combo {0}"), st.BestCombo) + (_rank is { } r ? "   " + string.Format(Ui.T("Rank {0} on this song's board"), r) : ""), 16));
            // The board of everyone on this computer, so players can challenge each other.
            var rows = Ui.VBox(2, Ui.Label(Ui.T("High scores on this computer"), 15, PipitTheme.InkSoft));
            foreach (var (entry, i) in Board.Take(5).Select((e, i) => (e, i)))
                rows.AddChild(Ui.Label($"{i + 1}.  {entry.Player}   {entry.Score,8:N0}   {entry.Reached:P0}   {Ui.T("combo")} {entry.BestCombo}   {entry.When:d MMM HH:mm}" + (entry.TempoFactor < 0.999 ? $"   ({entry.TempoFactor:P0} {Ui.T("speed")})" : ""),
                    15, ReferenceEquals(entry, _lastEntry) ? PipitTheme.Accent : PipitTheme.Ink));
            summary.AddChild(rows);
        }
        else if (Rhythmic && _performance is { } p)
        {
            var stars = new string('★', p.Stars) + new string('☆', 3 - p.Stars);
            summary.AddChild(Ui.HBox(16, Ui.Label($"{p.Accuracy:0}%", 44, PipitTheme.Accent), Ui.Label(stars, 40, PipitTheme.Warn)));
            if (_runTempo < 0.999)
                summary.AddChild(Ui.Label(Ui.T("Stars count at 100% speed; this was practice."), 15, PipitTheme.InkSoft));
            int Count(Func<NoteJudgement, bool> f) => p.Notes.Count(f);
            summary.AddChild(Ui.Label(string.Format(Ui.T("Perfect {0} · Great {1} · Good {2} · Early/late {3} · Wrong {4} · Missed {5}"),
                Count(n => n.Timing == Timing.Perfect), Count(n => n.Timing == Timing.Great), Count(n => n.Timing == Timing.Good),
                Count(n => n.Timing is Timing.Early or Timing.Late), Count(n => n.Result == NoteResult.WrongNote), Count(n => n.Result == NoteResult.Missed)), 16));
            var tips = new List<string>();
            if (Math.Abs(p.TimingTendencyMs) > 40)
                tips.Add(string.Format(Ui.T(p.TimingTendencyMs < 0 ? "You tend to play about {0:0} ms early: relax and wait for the beat." : "You tend to play about {0:0} ms late: get your fingers ready a little sooner."), Math.Abs(p.TimingTendencyMs)));
            foreach (var (expected, played, count) in p.Confusions.Take(2))
                tips.Add(string.Format(Ui.T("You played {1} instead of {0} ({2}×)."), S.NoteName(S.Recorder.SoundingToWritten(expected)), S.NoteName(S.Recorder.SoundingToWritten(played)), count));
            var outOfTune = p.Notes.Where(n => n.Result == NoteResult.Hit && !n.InTune).ToList();
            if (outOfTune.Count >= 3)
                tips.Add(Ui.T(outOfTune.Average(n => n.CentsOff) > 0 ? "Several notes were high: blow more gently." : "Several notes were low: blow a little more firmly."));
            foreach (var tip in tips)
                summary.AddChild(Ui.Text("• " + tip, 16, PipitTheme.Ink));
        }
        else if (_casual is { } c)
        {
            summary.AddChild(Ui.Label(string.Format(Ui.T("{0:0}% found first time"), c.Accuracy), 36, PipitTheme.Accent));
            var slow = c.Steps.OrderByDescending(s => s.SecondsToFind).Take(3).Where(s => s.SecondsToFind > 3).ToList();
            if (slow.Count > 0)
                summary.AddChild(Ui.Text(Ui.T("Took longest to find: ") + string.Join(", ", slow.Select(s => $"{S.NoteName(S.Recorder.SoundingToWritten(s.SoundingMidi))} ({s.SecondsToFind:0} s)")), 16));
        }

        var xp = Ui.VBox(4, Ui.Label($"+{award.Total} XP", 26, PipitTheme.Good));
        foreach (var line in award.Lines.Where(l => l.Amount > 0 || l.Reason.Contains("half")))
            xp.AddChild(Ui.Label($"{Ui.Reason(line.Reason)}  {(line.Amount > 0 ? "+" + line.Amount : "")}", 15, PipitTheme.InkSoft));
        var level = S.Progress.Level;
        var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = Levels.Fraction(S.Progress.Xp), ShowPercentage = false, CustomMinimumSize = new Vector2(220, 14) };
        xp.AddChild(Ui.HBox(8, Ui.Label(award.LevelledUp ? string.Format(Ui.T("Level up! Level {0}"), level) : string.Format(Ui.T("Level {0}"), level), 18, award.LevelledUp ? PipitTheme.Accent : PipitTheme.Ink), bar));

        foreach (var a in award.Unlocked)
            summary.AddChild(Ui.Label($"{a.Icon}  {Ui.T("Achievement unlocked")}: {Ui.T(a.Title)}  (+{a.Xp} XP)", 18, PipitTheme.Accent));

        var analysis = Rhythmic && _performance is { } graded ? AnalysisPanel(graded) : null;
        var analyse = Ui.Button("Show analysis", () => { if (analysis is not null) analysis.Visible = !analysis.Visible; }, 16);
        analyse.Visible = analysis is not null;

        var replay = Ui.Button("♪ Listen to your take", ToggleReplay, 16);
        replay.Disabled = _takeSamples is not { Length: > 0 } || _mode == Mode.Casual;
        // On the study path, say whether this song step is now done and offer the next step.
        var pathStep = new Core.Lessons.PathStep(Core.Lessons.StepKind.Song, _song.Id);
        Button? nextStep = null;
        if (_fromPath)
        {
            var done = Core.Lessons.StudyPath.SongDone(S.SongRecord(_song.Id));
            summary.AddChild(Ui.Text(Ui.T(done
                ? "✓ This step of the study path is done."
                : "To finish this step: earn a star in Rhythm mode, or find at least 80% of the notes first time in Casual mode."), 16, done ? PipitTheme.Good : PipitTheme.InkSoft));
            if (done && S.Study.After(pathStep) is { } next)
                nextStep = Ui.Primary(Ui.Button("Next step →", () => { Main.Instance.Back(); StudyScreen.Open(next); }, 18));
        }
        var back = Ui.Button(_fromPath ? "Study path" : "Songs", Main.Instance.Back, 16);
        var buttons = Ui.VBox(8, analyse, replay, back);
        if (nextStep is not null)
            buttons.AddChild(nextStep);
        var pip = new PipView { CustomMinimumSize = new Vector2(120, 120) };
        var good = _mode switch
        {
            Mode.Survival => _survival?.Alive == true || _rank == 1,
            Mode.Rhythm => _performance?.Stars > 0,
            _ => _casual?.Accuracy >= 80,
        };
        pip.SetMood(good ? PipMood.Cheering : PipMood.Thinking, good ? 3 : double.PositiveInfinity, PipMood.Singing);
        _resultsBox.AddChild(Ui.HBox(30, pip, Ui.Expand(summary), xp, buttons));
        if (analysis is not null)
            _resultsBox.AddChild(analysis);
    }

    /// <summary>"Going well" and "To work on", from <see cref="RunAnalysis"/>; hidden until asked for.</summary>
    private Control AnalysisPanel(GradedRun run)
    {
        var findings = RunAnalysis.Analyse(run);
        Control Column(string title, Color color, IEnumerable<Finding> items)
        {
            var box = Ui.VBox(4, Ui.Label(title, 20, color));
            var any = false;
            foreach (var f in items)
            {
                box.AddChild(Ui.Text("• " + Describe(f), 16, PipitTheme.Ink));
                any = true;
            }
            if (!any)
                box.AddChild(Ui.Text(Ui.T("Nothing stood out."), 16, PipitTheme.InkSoft));
            return Ui.Expand(box);
        }
        var panel = Ui.HBox(30, Column(Ui.T("Going well"), PipitTheme.Good, findings.Where(f => f.Positive)),
            Column(Ui.T("To work on"), PipitTheme.Bad, findings.Where(f => !f.Positive)));
        panel.Visible = false;
        return panel;
    }

    private string Describe(Finding f)
    {
        var name = f.SoundingMidi is { } m ? S.NoteName(S.Recorder.SoundingToWritten(m)) : "";
        return f.Kind switch
        {
            FindingKind.RhythmSteady => Ui.T("Your rhythm is steady."),
            FindingKind.RhythmEarly => string.Format(Ui.T("You rush a little: notes come about {0:0} ms early. Listen for the click and wait for it."), f.Value),
            FindingKind.RhythmLate => string.Format(Ui.T("You drag a little: notes come about {0:0} ms late. Get your fingers to the next note a bit sooner."), f.Value),
            FindingKind.RhythmUneven => string.Format(Ui.T("Your rhythm is a bit uneven (notes vary by about ±{0:0} ms). Try a slower speed with the metronome."), f.Value),
            FindingKind.LongNotesHeld => Ui.T("You hold the long notes for their full length."),
            FindingKind.LongNotesShort => string.Format(Ui.T("You let long notes go too early (about {0:0}% of their length). Keep blowing until the next note."), f.Value * 100),
            FindingKind.NotesShort => string.Format(Ui.T("Many notes are short ({0} of {1}). Let each note last until the next one starts."), f.Count, f.Total),
            FindingKind.NoteInTune => string.Format(Ui.T("Your {0} is nicely in tune."), name),
            FindingKind.NoteHigh => string.Format(Ui.T("Your {0} is a bit high (+{1:0} cents): blow it more gently."), name, f.Value),
            FindingKind.NoteLow => string.Format(Ui.T("Your {0} is a bit low ({1:0} cents): blow it a little more firmly, and check the holes are sealed."), name, f.Value),
            FindingKind.NoteSure => string.Format(Ui.T("You found {0} every time."), name),
            FindingKind.NoteHard => string.Format(Ui.T("{0} was hard: {1} of {2} missed or wrong. Practise it in the fingering trainer."), name, f.Count, f.Total),
            FindingKind.RepeatedNotesMerged => Ui.T("Repeated notes ran together. Tongue each one with a fresh \"du\"."),
            _ => f.Kind.ToString(),
        };
    }

    /// <summary>Keeps the last 20 run recordings next to the Studio's takes.</summary>
    private void SaveTake()
    {
        if (_takeSamples is not { Length: > 0 })
            return;
        Directory.CreateDirectory(Services.RunsDir);
        var name = $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{_song.Id}_{_mode.ToString().ToLowerInvariant()}";
        WavFile.Write(Path.Combine(Services.RunsDir, name + ".wav"), _takeSamples, S.Audio.SampleRate);
        new TakeInfo
        {
            Name = name, SampleRate = S.Audio.SampleRate, DurationSeconds = _takeSamples.Length / (double)S.Audio.SampleRate,
            InputDevice = S.Audio.CurrentDevice, Session = "song:" + _song.Id, Tempo = _song.Tempo * (Rhythmic ? _runTempo : _tempoFactor),
            LatencyMs = S.DeviceLatencyMs, Instrument = S.Instrument.Kind, Fingering = S.Instrument.Fingering,
            ReferenceA = S.Instrument.ReferenceA, GateRms = S.DeviceGate, TuningOffsetCents = S.Instrument.MeasuredOffsetCents,
        }.Save(Path.Combine(Services.RunsDir, name + ".json"));
        foreach (var old in Directory.GetFiles(Services.RunsDir, "*.json").OrderByDescending(f => f).Skip(20))
        {
            File.Delete(old);
            File.Delete(Path.ChangeExtension(old, ".wav"));
        }
    }

    // ---- listening -------------------------------------------------------------------------------

    /// <summary>For `--shots`.</summary>
    public void DebugListen() => ToggleListen();

    /// <summary>For `--shots`: show the highway.</summary>
    public void DebugShowHighway()
    {
        _bars.Visible = _staff.Visible = false;
        _highway.Visible = true;
    }

    /// <summary>For `--shots`: a survival run's callouts after ten perfect notes (a milestone).</summary>
    public void DebugSurvivalCallouts()
    {
        var notes = _song.Melody.Performance.Take(10)
            .Select(n => new NoteJudgement(new ExpectedNote(n.Index, n.Pitch.Midi, n.StartBeat, n.EndBeat), NoteResult.Hit, null, 0, Timing.Perfect, true, true, 0)).ToList();
        _mode = Mode.Survival;
        _survivalBar.Visible = true;
        ShowBest();
        UpdateSurvival(Survival.Evaluate(notes));
        CallOut(notes);
    }

    /// <summary>For `--shots`: show the score instead of the bars.</summary>
    public void DebugShowScore()
    {
        _bars.Visible = false;
        _staff.Visible = true;
    }

    private double _previewFrom;

    private async void ToggleListen()
    {
        if (_state == State.Preview)
        {
            _player.Stop();
            OnPlayerFinished();
            return;
        }
        _spb = _song.SecondsPerBeat(_tempoFactor);
        _previewLead = 0.4;
        var parts = BandActive ? new[] { _song.Melody }.Concat(BandParts).ToList() : new List<SongPart> { _song.Melody };
        _previewFrom = parts.SelectMany(p => p.Performance).Select(n => n.StartBeat).Min();
        _listen.Disabled = true;
        var audio = await Synth.RenderAsync(_song, parts, _spb, _previewFrom, _previewLead, natural: S.Settings.Voice == "natural");
        _listen.Disabled = false;
        if (!IsInsideTree() || _state is not (State.Ready or State.Results))
            return;
        _player.Stream = Playback.ToStream(audio, S.Audio.SampleRate);
        _player.Play();
        ClearMarks();
        SetState(State.Preview);
    }

    private void ToggleReplay()
    {
        if (_state == State.Replay)
        {
            _player.Stop();
            OnPlayerFinished();
            return;
        }
        _player.Stream = Playback.ToStream(_takeSamples!, S.Audio.SampleRate);
        _player.Play();
        SetState(State.Replay);
    }

    private void OnPlayerFinished()
    {
        if (_state == State.Preview)
            SetState(State.Ready);
        else if (_state == State.Replay)
            SetState(State.Results);
    }
}
