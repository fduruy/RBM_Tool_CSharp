namespace RbmTool.Core.Models;

/// <summary>
/// Configuration of the matching algorithm (mirrors MatcherConfig in Python).
/// </summary>
public sealed record MatcherConfig
{
    /// <summary>Safety cap of iterations per sub-portfolio.</summary>
    public int MaxIterations { get; init; } = 100;

    /// <summary>Buckets below this threshold are considered zero.</summary>
    public double ZeroTol { get; init; } = 0.01;

    /// <summary>Minimum usage of a match (avoids micro-matches).</summary>
    public double MinUsage { get; init; } = 1e-6;

    /// <summary>Minimum L1 gain to accept a match.</summary>
    public double MinGain { get; init; } = 1e-9;

    /// <summary>Equality tolerance of gains (tie-break by GptId).</summary>
    public double GainTol { get; init; } = 1e-9;

    /// <summary>External must have the same CCY and PricingModel.</summary>
    public bool MatchCcyPm { get; init; } = true;

    /// <summary>True: the external impacts ALL its buckets. False: only offsetting buckets.</summary>
    public bool FullImpact { get; init; } = true;

    /// <summary>Process sub-portfolios from the largest to the smallest.</summary>
    public bool LargestFirst { get; init; } = true;
}

/// <summary>
/// State of one internal sub-portfolio during matching.
/// </summary>
public sealed class SubPortfolioState
{
    public SubPortfolioState(string ccy, string pricingModel, string way,
                             IReadOnlyList<TradeInfo> trades,
                             Sensitivities initial, Sensitivities current)
    {
        Ccy = ccy;
        PricingModel = pricingModel;
        Way = way;
        Trades = trades;
        Initial = initial;
        Current = current;
    }

    public string Ccy { get; }
    public string PricingModel { get; }
    public string Way { get; }
    public IReadOnlyList<TradeInfo> Trades { get; }

    public Sensitivities Initial { get; }
    public Sensitivities Current { get; }

    public (string Ccy, string PricingModel, string Way) Key => (Ccy, PricingModel, Way);

    public string Name => $"{Ccy}_{PricingModel}_{Way}";

    public double L1 => Current.L1();

    public double InitialL1 => Initial.L1();
}

/// <summary>
/// One accepted match: a fraction of an external trade consumed by a sub-portfolio.
/// </summary>
public sealed record Match(
    string SubPortfolio,
    int ExternalTranNum,
    double Usage,
    double Reduction,
    int Iteration);
