using System.Linq;
using Godot;
using Pipit.Core.Music;

namespace Pipit.App;

/// <summary>Placeholder front screen for M0: proves the Godot assembly can use Pipit.Core.</summary>
public partial class Main : Control
{
    public override void _Ready()
    {
        var title = (string)ProjectSettings.GetSetting("application/config/name");
        GetWindow().Title = title;
        GetNode<Label>("%Title").Text = title;
        var scale = "C4 D4 E4 F4 G4 A4 B4".Split(' ').Select(p => NoteNames.Name(Pitch.Parse(p)));
        GetNode<Label>("%Subtitle").Text = string.Join("  ", scale);
    }
}
