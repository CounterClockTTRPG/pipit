using System.Collections.Generic;
using Godot;
using Pipit.App.UI;
using Pipit.App.Views;

namespace Pipit.App.Screens;

/// <summary>Pip introduces itself and the parts of the app to a new player, a page at a time.</summary>
public partial class IntroScreen : Screen
{
    private sealed record Page(PipMood Mood, string Section, string En, string Nl);

    private static readonly List<Page> Pages = new()
    {
        new(PipMood.Singing, "",
            "Hi {0}, I'm Pip! I'm a meadow pipit, a small songbird that lives in fields and sings while it flies. I'll help you learn the recorder, one note at a time.",
            "Hallo {0}, ik ben Pip! Ik ben een graspieper, een klein zangvogeltje dat in de velden woont en zingt terwijl het vliegt. Ik help je blokfluit leren, noot per noot."),
        new(PipMood.Cheering, "Study",
            "Study is where you start. Short lessons teach you one new note at a time, with little exercises and songs in between. Press Continue and follow me.",
            "Bij Studeren begin je. Korte lesjes leren je telkens één nieuwe noot, met kleine oefeningen en liedjes ertussen. Druk op Verder en volg mij."),
        new(PipMood.Singing, "Songs",
            "Songs has music from your very first tunes to Bach. Play along with the beat, or in Casual mode, where the song waits for you. Later you can play with a band, or in Survival mode for a high score.",
            "Bij Liedjes vind je muziek, van je allereerste liedjes tot Bach. Speel mee op de tel, of in de rustige modus, waar het liedje op jou wacht. Later kan je met een band spelen, of in de overlevingsmodus voor een topscore."),
        new(PipMood.Listening, "Tuner and Fingering trainer",
            "The Tuner shows which note you play and whether it is in tune. The Fingering trainer shows you a fingering, and you play it.",
            "De Stemmer toont welke noot je speelt en of ze zuiver is. De Vingertrainer toont een greep, en jij speelt ze."),
        new(PipMood.Thinking, "Studio, Achievements and Settings",
            "In the Studio you can record yourself and listen back. Achievements shows what you have unlocked, and in Settings you set up your recorders and the microphone.",
            "In de Studio kan je jezelf opnemen en terugluisteren. Bij Prestaties zie je wat je al ontgrendeld hebt, en bij Instellingen stel je je blokfluiten en de microfoon in."),
        new(PipMood.Listening, "Calibrate",
            "One more thing: before we start, I'd like to listen to your room and your recorder, so I hear you well. That's Calibrate, and it takes about two minutes.",
            "Nog één ding: voor we beginnen wil ik even naar je kamer en je blokfluit luisteren, zodat ik je goed hoor. Dat is Kalibreren, en het duurt ongeveer twee minuten."),
    };

    private readonly PipView _pip = new() { CustomMinimumSize = new Vector2(300, 300) };
    private readonly Label _section, _text;
    private readonly Button _back, _next, _calibrate;
    private int _page;

    public override string Title => "Welcome";

    public IntroScreen()
    {
        _section = Ui.Label("", 30, PipitTheme.Accent);
        _text = Ui.Text("", 24, PipitTheme.Ink);
        _back = Ui.Button("Back", () => Go(_page - 1), 20);
        _next = Ui.Button("Next", () => { if (_page == Pages.Count - 1) Finish(calibrate: false); else Go(_page + 1); }, 22);
        _calibrate = Ui.Button("Calibrate now", () => Finish(calibrate: true), 22);
        var skip = Ui.Button("Skip", () => Finish(calibrate: false), 18);
        var bubble = new PanelContainer();
        bubble.AddThemeStyleboxOverride("panel", PipitTheme.Box(Colors.White, PipitTheme.Accent, 24));
        bubble.AddChild(Ui.VBox(14, _section, _text));
        var text = Ui.Expand(Ui.VBox(24, Ui.Spacer(), bubble, Ui.HBox(14, _back, _next, _calibrate, Ui.Spacer(), skip), Ui.Spacer()), true, true);
        AddChild(Ui.HBox(30, Ui.VBox(0, Ui.Spacer(), _pip, Ui.Spacer()), text));
        Go(0);
    }

    private void Go(int page)
    {
        _page = Mathf.Clamp(page, 0, Pages.Count - 1);
        var p = Pages[_page];
        var dutch = S.Settings.Language == "nl";
        _section.Text = Ui.T(p.Section);
        _section.Visible = p.Section != "";
        _text.Text = string.Format(dutch ? p.Nl : p.En, S.Player.Name);
        _pip.SetMood(p.Mood);
        _back.Disabled = _page == 0;
        var last = _page == Pages.Count - 1;
        _next.Text = Ui.T(last ? "Go to Home" : "Next");
        _calibrate.Visible = last;
    }

    private void Finish(bool calibrate)
    {
        S.Player.IntroSeen = true;
        S.Save();
        Main.Instance.Back();
        if (calibrate)
            Main.Instance.Show(new CalibrationScreen());
    }
}
