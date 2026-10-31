using RbmTool.Core.Models;

namespace RbmTool.Core.Matching;

/// <summary>
/// Pure functions of the matching algorithm (port of portfolio_matcher.py pure helpers).
/// </summary>
public static class MatchingMath
{
    /// <summary>
    /// Variation of sum|S_k| (on the buckets of E) if we add x * E.
    /// Port of l1_change in Python.
    /// </summary>
    public static double L1Change(Sensitivities s, Sensitivities e, double x)
    {
        double delta = 0.0;
        foreach (var (k, ek) in e.Items)
        {
            var sk = s.Get(k);
            delta += Math.Abs(sk + x * ek) - Math.Abs(sk);
        }
        return delta;
    }

    /// <summary>
    /// Subset of E whose buckets have the opposite sign of S.
    /// Port of offsetting_buckets in Python.
    /// </summary>
    public static Sensitivities OffsettingBuckets(Sensitivities s, Sensitivities e)
    {
        var result = new Sensitivities();
        foreach (var (k, ek) in e.Items)
        {
            if (s.Get(k) * ek < 0)
            {
                result.Set(k, ek);
            }
        }
        return result;
    }

    /// <summary>
    /// Exact argmin over x in [0, xMax] of f(x) = sum_k |S_k + x * E_k|.
    ///
    /// f is convex and piecewise linear: we start with the slope at x = 0 and
    /// increase it by 2*|E_k| at each breakpoint x_k = -S_k / E_k.
    /// Returns 0.0 if no usage reduces the L1 norm.
    /// Port of optimal_usage in Python.
    /// </summary>
    public static double OptimalUsage(Sensitivities s, Sensitivities e, double xMax)
    {
        if (xMax <= 0)
        {
            return 0.0;
        }

        double slope = 0.0;
        var breaks = new List<(double Xk, double W)>();

        foreach (var (k, ek) in e.Items)
        {
            if (ek == 0)
            {
                continue;
            }

            var sk = s.Get(k);
            if (sk == 0)
            {
                slope += Math.Abs(ek); // new bucket: f increases from x = 0+
            }
            else
            {
                slope += sk > 0 ? ek : -ek;
                var xk = -sk / ek;
                if (xk > 0)
                {
                    breaks.Add((xk, Math.Abs(ek)));
                }
            }
        }

        if (slope >= 0)
        {
            return 0.0;
        }

        breaks.Sort((a, b) => a.Xk.CompareTo(b.Xk));
        foreach (var (xk, w) in breaks)
        {
            if (xk >= xMax)
            {
                return xMax;
            }
            slope += 2.0 * w;
            if (slope >= 0)
            {
                return xk;
            }
        }

        return xMax;
    }
}
