namespace Pipit.Core.Analysis;

public static class Stats
{
    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
        if (sorted.Length == 0)
            return double.NaN;
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>Median absolute deviation: a spread measure that ignores the odd outlier.</summary>
    public static double MedianAbsoluteDeviation(IEnumerable<double> values)
    {
        var list = values.Where(v => !double.IsNaN(v)).ToList();
        var median = Median(list);
        return Median(list.Select(v => Math.Abs(v - median)));
    }

    public static double Percentile(IEnumerable<double> values, double p)
    {
        var sorted = values.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
        if (sorted.Length == 0)
            return double.NaN;
        var rank = Math.Clamp(p, 0, 1) * (sorted.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }
}
