namespace Pipit.App.Views;

/// <summary>What the play screen tells a song view (bars or score) while a song plays.</summary>
public interface ISongView
{
    /// <summary>Position in the song, in beats; drives the playhead or cursor.</summary>
    double CurrentBeat { get; set; }
    /// <summary>In casual mode, the note being waited for (performed index); null otherwise.</summary>
    int? WaitingFor { get; set; }
    string Overlay { get; set; }
    void SetMark(int performedIndex, NoteMark mark);
    void ClearMarks();
    /// <summary>The player's pitch at a beat (written MIDI, NaN for silence).</summary>
    void AddTrace(double beat, double writtenMidi);
}
