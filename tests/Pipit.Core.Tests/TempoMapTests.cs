using Pipit.Core.Music;
using Xunit;

namespace Pipit.Core.Tests;

public class TempoMapTests
{
    [Fact]
    public void A_steady_tempo_is_a_straight_line()
    {
        var map = new TempoMap(10, 0.5);
        Assert.Equal(10, map.BeatToTime(0), 9);
        Assert.Equal(12, map.BeatToTime(4), 9);
        Assert.Equal(8, map.BeatToTime(-4), 9); // the count-in
        Assert.Equal(4, map.TimeToBeat(12), 9);
    }

    [Fact]
    public void A_change_moves_only_what_comes_after_it()
    {
        var map = new TempoMap(0, 0.5);
        map.Change(8, 1.0); // half speed from beat 8 (at 4 s)
        Assert.Equal(2, map.BeatToTime(4), 9);
        Assert.Equal(4, map.BeatToTime(8), 9);
        Assert.Equal(6, map.BeatToTime(10), 9);
        Assert.Equal(10, map.TimeToBeat(6), 9);
        Assert.Equal(4, map.TimeToBeat(2), 9);
        Assert.Equal(1.0, map.SecondsPerBeat);
    }

    [Fact]
    public void Later_changes_replace_the_tail_and_keep_times_continuous()
    {
        var map = new TempoMap(0, 0.5);
        map.Change(8, 1.0);
        map.Change(12, 0.25); // at 8 s
        Assert.Equal(8, map.BeatToTime(12), 9);
        Assert.Equal(9, map.BeatToTime(16), 9);
        map.Change(10, 0.5); // a change before the last one replaces it
        Assert.Equal(2, map.Changes);
        Assert.Equal(6, map.BeatToTime(10), 9);
        Assert.Equal(7, map.BeatToTime(12), 9);
        for (var b = -4.0; b < 20; b += 0.75)
            Assert.Equal(b, map.TimeToBeat(map.BeatToTime(b)), 9);
    }

    [Fact]
    public void A_change_in_the_count_in_retimes_the_whole_run()
    {
        var map = new TempoMap(4, 1.0); // count-in from beat -4 at 0 s
        map.Change(-2, 0.5);           // at 2 s, two beats before the song
        Assert.Equal(2, map.BeatToTime(-2), 9);
        Assert.Equal(3, map.BeatToTime(0), 9);
        Assert.Equal(0, map.Changes);
    }
}
