using Pipit.Core.Game;

namespace Pipit.Core.Practice;

/// <summary>
/// The fingering trainer's learning mode, in the spirit of Leitner flash cards. Notes come in
/// the order the study path teaches them; the player starts with <see cref="StartWith"/> and a
/// new one joins once every note in play has been found a few times in a row (box 2 or more).
/// Notes in low boxes come up more often, the newest note gets an extra turn, and the same note
/// is not asked twice running.
/// </summary>
public sealed class LearningDeck
{
    public const int StartWith = 3;
    /// <summary>Box every active note must reach before a new one joins.</summary>
    public const int ReadyBox = 2;

    private readonly IReadOnlyList<int> _order;
    private readonly Func<int, NoteStat?> _stat;
    private readonly Random _rng;

    /// <param name="order">Written notes in teaching order.</param>
    /// <param name="introduced">How many have joined so far (saved with the player's progress).</param>
    /// <param name="stat">The learning record of a note, if any.</param>
    public LearningDeck(IReadOnlyList<int> order, int introduced, Func<int, NoteStat?> stat, Random rng)
    {
        _order = order;
        _stat = stat;
        _rng = rng;
        Introduced = Math.Clamp(Math.Max(introduced, StartWith), 0, order.Count);
    }

    public int Introduced { get; private set; }
    public IReadOnlyList<int> Order => _order;
    public IEnumerable<int> Active => _order.Take(Introduced);

    private int Box(int note) => _stat(note)?.Box ?? 0;

    /// <summary>Brings in the next note when the ones in play are ready; returns it, or null.</summary>
    public int? IntroduceIfReady()
    {
        if (Introduced >= _order.Count || Active.Any(n => Box(n) < ReadyBox))
            return null;
        return _order[Introduced++];
    }

    /// <summary>The next note to ask: low boxes more often, the newest note a little more.</summary>
    public int Next(int? previous)
    {
        var active = Active.ToList();
        var candidates = active.Count > 1 && previous is { } p ? active.Where(n => n != p).ToList() : active;
        var weights = candidates.Select(n =>
        {
            var w = Math.Pow(NoteStat.TopBox + 1 - Box(n), 2);
            return n == _order[Introduced - 1] ? w * 1.5 : w;
        }).ToList();
        var pick = _rng.NextDouble() * weights.Sum();
        for (var i = 0; i < candidates.Count; i++)
        {
            pick -= weights[i];
            if (pick <= 0)
                return candidates[i];
        }
        return candidates[^1];
    }

    /// <summary>0-1: how far along the whole range the player is (boxes filled).</summary>
    public double Mastery => _order.Count == 0 ? 0 : _order.Sum(n => (double)Box(n)) / (NoteStat.TopBox * _order.Count);

    /// <summary>Notes found reliably (box 4 or 5).</summary>
    public int Known => _order.Count(n => Box(n) >= 4);
}
