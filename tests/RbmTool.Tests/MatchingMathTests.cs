using RbmTool.Core.Models;
using RbmTool.Core.Matching;
using Xunit;

namespace RbmTool.Tests;

public class MatchingMathTests
{
    private static Sensitivities S(params (string Index, string Tenor, double Value)[] buckets)
    {
        var s = new Sensitivities();
        foreach (var (index, tenor, value) in buckets)
        {
            s.Set((index, tenor), value);
        }
        return s;
    }

    private static Sensitivities E(params (string Index, string Tenor, double Value)[] buckets) => S(buckets);

    [Fact]
    public void OptimalUsage_NoOverlap_ReturnsZero()
    {
        // E only adds new buckets: no L1 reduction possible
        var s = S(("EURIBOR", "1Y", 100.0));
        var e = E(("SONIA", "1Y", 50.0));
        Assert.Equal(0.0, MatchingMath.OptimalUsage(s, e, 1.0));
    }

    [Fact]
    public void OptimalUsage_SameSign_ReturnsZero()
    {
        // Same sign: adding E increases |S|
        var s = S(("EURIBOR", "1Y", 100.0));
        var e = E(("EURIBOR", "1Y", 50.0));
        Assert.Equal(0.0, MatchingMath.OptimalUsage(s, e, 1.0));
    }

    [Fact]
    public void OptimalUsage_PerfectOffset_ReturnsFullUsage()
    {
        var s = S(("EURIBOR", "1Y", 100.0));
        var e = E(("EURIBOR", "1Y", -100.0));
        Assert.Equal(1.0, MatchingMath.OptimalUsage(s, e, 1.0));
    }

    [Fact]
    public void OptimalUsage_PartialOffset_StopsAtBreakpoint()
    {
        // Optimum: cancel exactly the bucket (-100/50 = 2 > xMax=1 -> xMax)
        var s = S(("EURIBOR", "1Y", 100.0));
        var e = E(("EURIBOR", "1Y", -50.0));
        Assert.Equal(1.0, MatchingMath.OptimalUsage(s, e, 1.0));

        // With a bigger S, breakpoint at 2 remains beyond xMax
        var s2 = S(("EURIBOR", "1Y", 200.0));
        Assert.Equal(1.0, MatchingMath.OptimalUsage(s2, e, 1.0));
    }

    [Fact]
    public void OptimalUsage_MultiBucket_OptimalBreakpoint()
    {
        // S = {A: 100, B: -10}, E = {A: -100, B: -100}
        // Breakpoints: A at 1.0, B at 0.1. Slope starts negative, stops at B's breakpoint.
        var s = S(("A", "1Y", 100.0), ("B", "1Y", -10.0));
        var e = E(("A", "1Y", -100.0), ("B", "1Y", -100.0));
        Assert.Equal(0.1, MatchingMath.OptimalUsage(s, e, 1.0), 10);
    }

    [Fact]
    public void L1Change_NegativeWhenOffsetting()
    {
        var s = S(("A", "1Y", 100.0));
        var e = E(("A", "1Y", -50.0));
        Assert.Equal(-50.0, MatchingMath.L1Change(s, e, 1.0));
    }

    [Fact]
    public void OffsettingBuckets_KeepsOnlyOppositeSigns()
    {
        var s = S(("A", "1Y", 100.0), ("B", "1Y", -100.0));
        var e = E(("A", "1Y", -50.0), ("B", "1Y", -50.0), ("C", "1Y", 10.0));
        var offset = MatchingMath.OffsettingBuckets(s, e);
        Assert.Equal(1, offset.Count);
        Assert.Equal(-50.0, offset.Get(("A", "1Y")));
    }

    [Fact]
    public void Sensitivities_L1_SumsAbsoluteValues()
    {
        var s = S(("A", "1Y", 10.0), ("B", "1Y", -5.0));
        Assert.Equal(15.0, s.L1());
    }

    [Fact]
    public void Sensitivities_Apply_RemovesZeroedBuckets()
    {
        var s = S(("A", "1Y", 10.0));
        var impact = E(("A", "1Y", -10.0));
        s.Apply(impact, 1.0, 0.01);
        Assert.Equal(0, s.Count);
    }
}
