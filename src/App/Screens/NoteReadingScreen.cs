using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pipit.App.Lessons;
using Pipit.App.UI;
using Pipit.App.Views;
using Pipit.Core.Game;
using Pipit.Core.Lessons;
using Pipit.Core.Practice;
using Pipit.Core.Scoring;

namespace Pipit.App.Screens;

/// <summary>
/// Note reading: eight notes on the staff, a count-in, then the player names each note on its
/// beat by tapping its name (or pressing its letter key, or 1-7). The notes come from a learning
/// deck of their own ("read" records): the path's first notes, then more as they are known.
/// Wrong or late names are marked on the staff; the results go into My notes.
///
/// With "Say the names" the player speaks instead: <see cref="Audio.SpeechListener"/> hears the
/// names with the times they were said, and they are graded the same way.
/// </summary>
public partial class NoteReadingScreen : Screen
{
    private const int Notes = 8, CountIn = 4;
    private static readonly int[] NaturalOffsets = { 0, 2, 4, 5, 7, 9, 11 };

    private readonly StaffView _staff = new() { ShowNames = false, CustomMinimumSize = new Vector2(0, 230) };
    private readonly Label _status;
    private readonly Button _start;
    private readonly HBoxContainer _names = Ui.HBox(10);
    private readonly OptionButton _speed;
    private readonly OptionButton _answerBy;
    private Audio.SpeechListener? _speech;
    private readonly Label _heard = Ui.Label("", PipitTheme.Lead, PipitTheme.Sky, HorizontalAlignment.Center);
    private readonly PipView _pip = new() { CustomMinimumSize = new Vector2(110, 110) };
    private readonly Random _rng = new();
    private LearningDeck _deck = null!;
    private List<int> _notes = new();
    private readonly List<NameAnswer> _answers = new();
    private readonly HashSet<int> _marked = new();
    private double _t0 = double.NaN, _spb = 1;
    private Core.Music.Song? _song;

    private static readonly int[] Tempos = { 35, 50, 70, 90 };
    private int _seenWords;
    private readonly Button _test;
    private bool _testing;

    public override string Title => "Note reading";

    public NoteReadingScreen()
    {
        _status = Ui.Text("", PipitTheme.Lead, PipitTheme.InkSoft, HorizontalAlignment.Center);
        _start = Ui.Primary(Ui.Button("▶ Start", Start, PipitTheme.Lead));
        _start.CustomMinimumSize = new Vector2(160, 56);
        _speed = Ui.Options(new[] { "Very slow", "Slow", "Medium", "Fast" }, 1, _ => { });
        _test = Ui.Button("🎤 Test", ToggleTest, PipitTheme.Small + 2);
        _test.TooltipText = Ui.T("Say a note name and see what Pipit hears.");
        _test.Visible = false;
        _answerBy = Ui.Options(new[] { "Tap the names", "Say the names" }, 0, ix =>
        {
            _test.Visible = ix == 1;
            // Start loading the speech model now, so it is ready by the time Start is pressed.
            if (ix == 1)
                Audio.SpeechListener.Preload(S.Settings.Language);
            else if (_testing)
                ToggleTest();
        });
        _names.Alignment = BoxContainer.AlignmentMode.Center;
        var top = Ui.HBox(14, Ui.Label("Speed", PipitTheme.Body, PipitTheme.InkSoft), _speed, _answerBy, _test, Ui.Spacer(),
            Ui.Quiet(Ui.Button("My notes", () => Main.Instance.Show(new NoteStatsScreen()), 18)));
        AddChild(Ui.VBox(18, top,
            Ui.Text("Name each note on its beat: tap its name, or press its letter. The count-in gives four clicks first.", PipitTheme.Body, PipitTheme.InkSoft),
            Ui.Expand(_staff), _status, _heard, _names,
            Ui.HBox(16, Ui.Spacer(), _pip, _start, Ui.Spacer())));
    }

    private static string Kind => S.Instrument.Kind.ToString();
    private static string DeckKey => $"{PlayerProgress.Reading}|{Kind.ToLowerInvariant()}";

    public override void Enter()
    {
        foreach (var child in _names.GetChildren())
            child.QueueFree();
        for (var letter = 0; letter < 7; letter++)
        {
            var l = letter;
            var button = Ui.Button(S.NoteName(60 + NaturalOffsets[letter]), () => Answer(l), PipitTheme.Heading);
            button.CustomMinimumSize = new Vector2(112, 76);
            _names.AddChild(button);
        }
        var order = TrainerScreen.TeachingOrder().Where(NameDrill.IsNatural).ToList();
        _deck = new LearningDeck(order, S.Progress.NotesIntroduced.GetValueOrDefault(DeckKey),
            n => S.Progress.FindNote(PlayerProgress.Reading, Kind, n), _rng);
        NewNotes();
        _status.Text = Ui.T("Press Start when you are ready.");
        _answerBy.Visible = Audio.SpeechListener.Available(S.Settings.Language);
    }

    private bool Speaking => _answerBy.Visible && _answerBy.Selected == 1;
    private bool Letters => S.Settings.Naming == Core.Music.NoteNaming.Letters;

    public override void Exit()
    {
        _testing = false;
        _speech?.Stop();
        _speech?.Dispose();
        _speech = null;
        S.Clicks.Stop();
        _t0 = double.NaN;
        S.SaveProgress();
    }

    private void NewNotes()
    {
        if (_deck.IntroduceIfReady() is { } newcomer)
        {
            S.Progress.NotesIntroduced[DeckKey] = _deck.Introduced;
            _status.Text = string.Format(Ui.T("A new note to read: {0}."), S.NoteName(newcomer));
        }
        _notes.Clear();
        int? previous = null;
        for (var i = 0; i < Notes; i++)
            _notes.Add((previous = _deck.Next(previous)).Value);
        // Two bars of quarter notes, spelt as plain naturals.
        var music = string.Join(" | ", _notes.Chunk(4).Select(bar => string.Join(" ", bar.Select(n => Core.Music.Transposition.SpellIn(n, new Core.Notation.KeySignature(0)) + "/q"))));
        _song = LessonBlocks.SnippetSong(new DirectiveBlock("staff", null, new Dictionary<string, string> { ["music"] = music }));
        _staff.SetSong(_song, S.Instrument.Kind);
        _staff.ClearMarks();
        _staff.CurrentBeat = -0.5;
    }

    private async void Start()
    {
        if (!double.IsNaN(_t0))
            return;
        if (_testing)
            ToggleTest();
        if (Speaking && !await EnsureSpeech())
            return;
        if (_played)
            NewNotes();
        _played = false;
        _answers.Clear();
        _marked.Clear();
        _staff.ClearMarks();
        _spb = 60.0 / Tempos[_speed.Selected];
        _t0 = S.Audio.Now + 1.0; // the first count-in click
        S.Clicks.Schedule(Enumerable.Range(0, CountIn + Notes).Select(i => (_t0 + i * _spb, i % 4 == 0)));
        _start.Disabled = true;
        _heard.Text = "";
        _seenWords = 0;
        if (Speaking)
            _speech?.Start();
        _status.Text = Ui.T(Speaking ? "Listen to the count-in, then say each name on its click…" : "Listen to the count-in…");
        _status.Modulate = Colors.White;
    }

    /// <summary>Loads the speech model if needed; false (with a message) when it cannot listen.</summary>
    private async System.Threading.Tasks.Task<bool> EnsureSpeech()
    {
        if (_speech is not null)
            return true;
        _start.Disabled = _test.Disabled = true;
        _status.Text = Ui.T("Getting ready to listen… (the first time takes a little while)");
        try
        {
            _speech = await Audio.SpeechListener.Create(S.Audio, S.Settings.Language, Core.Practice.NoteWords.Grammar(Letters));
        }
        catch (Exception e)
        {
            GD.PushError($"Speech: {e}");
            _status.Text = string.Format(Ui.T("Pipit could not start listening ({0}). Tap the names instead."), e.Message);
        }
        _start.Disabled = _test.Disabled = false;
        if (_speech is null || !IsInsideTree())
            return false;
        _status.Text = "";
        return true;
    }

    /// <summary>The Test button: listens until pressed again, showing every name it hears.</summary>
    private async void ToggleTest()
    {
        if (_testing)
        {
            _testing = false;
            _speech?.Stop();
            _test.Text = Ui.T("🎤 Test");
            return;
        }
        if (!double.IsNaN(_t0) || !await EnsureSpeech())
            return;
        _testing = true;
        _speech!.Start();
        _test.Text = Ui.T("■ Stop test");
        _status.Text = Ui.T("Say a note name…");
        _status.Modulate = Colors.White;
    }

    private double NoteTime(int i) => _t0 + (CountIn + i) * _spb;

    /// <summary>For `--shots`: a finished round with one of each kind of answer.</summary>
    public void DebugResults()
    {
        _spb = 1;
        _t0 = S.Audio.Now - 100;
        _answers.Clear();
        var offsets = new[] { 0.02, -0.35, 0.3, 0.05, double.NaN, 0.1, 0.4, -0.05 };
        for (var i = 0; i < _notes.Count; i++)
        {
            if (double.IsNaN(offsets[i]))
                continue; // missed
            var letter = i == 3 ? (NameDrill.LetterOf(_notes[i]) + 2) % 7 : NameDrill.LetterOf(_notes[i]); // one wrong
            _answers.Add(new NameAnswer(letter, NoteTime(i) + offsets[i]));
        }
        Finish();
    }

    private static string LetterName(int letter) => S.NoteName(60 + NaturalOffsets[letter]);

    /// <summary>Marks the note being named around <paramref name="time"/> and writes the name under it.</summary>
    private void ShowNamed(int letter, double time)
    {
        for (var i = 0; i < _notes.Count; i++)
        {
            if (_marked.Contains(i) || Math.Abs(time - NoteTime(i)) > _spb * 0.75)
                continue;
            _marked.Add(i);
            var right = letter == NameDrill.LetterOf(_notes[i]);
            _staff.SetMark(i, right ? NoteMark.Perfect : NoteMark.Wrong);
            _staff.SetCaption(i, LetterName(letter), right ? PipitTheme.Good : PipitTheme.Bad);
            return;
        }
    }

    private void Answer(int letter)
    {
        if (double.IsNaN(_t0))
            return;
        var now = S.Audio.Now;
        _answers.Add(new NameAnswer(letter, now));
        ShowNamed(letter, now); // straight away; the grading at the end has the last word
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        var letter = key.Keycode switch
        {
            Key.C or Key.Key1 => 0, Key.D or Key.Key2 => 1, Key.E or Key.Key3 => 2, Key.F or Key.Key4 => 3,
            Key.G or Key.Key5 => 4, Key.A or Key.Key6 => 5, Key.B or Key.Key7 => 6, _ => -1,
        };
        if (letter < 0)
            return;
        Answer(letter);
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_testing && _speech is not null)
        {
            // The last few words heard, as note names ("?" for a word that is not one).
            var words = _speech.WordsSoFar.Where(w => w != "[unk]").TakeLast(8)
                .Select(w => Core.Practice.NoteWords.LetterFor(w, Letters) is { } l ? LetterName(l) : "?");
            _heard.Text = string.Format(Ui.T("I hear: {0}"), string.Join("  ", words));
        }
        if (double.IsNaN(_t0))
            return;
        var now = S.Audio.Now;
        var beat = (now - NoteTime(0)) / _spb;
        _staff.CurrentBeat = beat;
        _staff.Overlay = beat < 0 ? Math.Ceiling(-beat).ToString("0") : "";
        if (beat >= 0 && now < NoteTime(Notes - 1) + _spb / 2)
            _status.Text = "";
        if (Speaking && _speech is not null)
        {
            _heard.Text = _speech.Partial;
            // Words appear a moment after they are said: put each new one under the note it was
            // probably meant for. The exact times come with the final result.
            var words = _speech.WordsSoFar;
            if (words.Count < _seenWords)
                _seenWords = words.Count;
            for (; _seenWords < words.Count; _seenWords++)
                if (Core.Practice.NoteWords.LetterFor(words[_seenWords], Letters) is { } letter)
                    ShowNamed(letter, now - 0.4);
        }
        if (now > NoteTime(Notes - 1) + _spb * 0.6)
            Finish();
    }

    private void Finish()
    {
        if (Speaking && _speech is not null)
        {
            // Spoken names count from when they were said, less the microphone's delay.
            var latency = (S.DeviceLatencyMs ?? 0) / 1000;
            var heard = _speech.Stop();
            foreach (var (word, time) in heard)
                if (Core.Practice.NoteWords.LetterFor(word, Letters) is { } letter)
                    _answers.Add(new NameAnswer(letter, time - latency));
            _heard.Text = string.Join(" ", heard.Select(w => w.Word).Where(w => w != "[unk]"));
        }
        var results = NameDrill.Grade(_notes.Select((n, i) => (n, NoteTime(i))).ToList(), _answers, _spb);
        _t0 = double.NaN;
        _staff.Overlay = "";
        var now = DateTime.Now;
        foreach (var r in results)
        {
            _staff.SetMark(r.Index, r.RightOnTime ? NoteMark.Perfect : r.Result == NameResult.Missed ? NoteMark.Missed : r.Result == NameResult.Right ? NoteMark.Good : NoteMark.Wrong);
            // Under each note: what was said or tapped, placed early (left) or late (right) of the
            // note by when it came, with an arrow when it missed the beat; in grey under a wrong
            // or missed one, what the note is.
            var arrow = r.Timing switch { Timing.Early => "◂", Timing.Late => "▸", _ => "" };
            var said = r.Said is { } letter ? LetterName(letter) : "–";
            var text = r.Timing == Timing.Early ? arrow + said : said + arrow;
            var shift = r.ErrorMs is { } ms ? ms / 1000 / _spb : 0;
            var should = r.Result == NameResult.Right ? "" : LetterName(NameDrill.LetterOf(r.WrittenMidi));
            _staff.SetCaption(r.Index, text,
                r.RightOnTime ? PipitTheme.Good : r.Result == NameResult.Right ? PipitTheme.Warn : r.Result == NameResult.Missed ? PipitTheme.InkSoft : PipitTheme.Bad,
                should, shift);
            S.Progress.Note(PlayerProgress.Reading, Kind, r.WrittenMidi).Record(r.RightOnTime, 0, now);
        }
        var good = results.Count(r => r.RightOnTime);
        var wrong = results.Where(r => r.Result == NameResult.WrongName).Select(r => S.NoteName(r.WrittenMidi)).Distinct().ToList();
        var late = results.Count(r => r.Result == NameResult.Right && !r.RightOnTime);
        var lines = new List<string> { string.Format(Ui.T("{0} of {1} named right and on the beat."), good, results.Count) };
        if (wrong.Count > 0)
            lines.Add(string.Format(Ui.T("Look again at: {0}."), string.Join(", ", wrong)));
        if (late > 0)
            lines.Add(Ui.T("Some names came early or late: name each note right on its click."));
        _status.Text = string.Join(" ", lines);
        _status.Modulate = good == results.Count ? PipitTheme.Good : good >= results.Count * 3 / 4 ? PipitTheme.Warn : PipitTheme.Bad;
        _pip.SetMood(good == results.Count ? PipMood.Cheering : PipMood.Thinking, 2.0);
        if (good > 0)
            S.AwardXp(XpRules.Trainer(good), new AchievementEvent { ReadingPerfect = good == results.Count });
        S.SaveProgress();
        _start.Disabled = false;
        _start.Text = Ui.T("▶ Again");
        _played = true; // the marked notes stay on the staff until the next round starts
    }

    private bool _played;
}
