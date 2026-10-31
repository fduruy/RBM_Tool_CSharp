using System.Collections;
using RbmTool.Core.Models;

namespace RbmTool.Core.Matching;

/// <summary>
/// Result of one full matching run (port of PortfolioMatcher.get_results()).
/// All tables are plain rows: (SubPortfolio, Index, Tenor, Delta) etc.
/// </summary>
public sealed class MatchResults
{
    public IReadOnlyList<(string SubPortfolio, string Index, string Tenor, double Delta)> InternalSubPortfolios { get; init; } = [];
    public IReadOnlyList<Match> MatchedTrades { get; init; } = [];
    public IReadOnlyList<ExternalUsageRow> ExternalUsage { get; init; } = [];
    public IReadOnlyList<(string SubPortfolio, string Index, string Tenor, double Delta)> UnmatchedSensitivities { get; init; } = [];
    public IReadOnlyList<(string SubPortfolio, double InitialL1, double FinalL1, int NbMatches, bool CapReached)> SubPortfolioSummary { get; init; } = [];
}

public sealed record ExternalUsageRow(
    int TranNum,
    int? InsNum,
    string ExternalParty,
    string Ccy,
    string PricingModel,
    string Way,
    double Usage);

/// <summary>
/// Greedy portfolio matcher with exact local optimality (port of portfolio_matcher.py).
///
/// 1. Internal trades are grouped into sub-portfolios (CCY, PricingModel, Way).
/// 2. For each sub-portfolio we iteratively pick the external trade (opposite Way,
///    same CCY/PricingModel by default) that reduces the most the total absolute
///    sensitivity sum_k |S_k|.
/// 3. The fraction of the external trade used (0..1) is the exact optimum of the
///    convex problem min_x sum_k |S_k + x * E_k|, x in [0, remaining part].
/// 4. External trade usage is cumulated across sub-portfolios (never above 100%).
/// Priority rule: at equal gain (within gainTol), the trade with the highest GptId wins.
/// </summary>
public sealed class PortfolioMatcher
{
    private readonly MatcherConfig _config;
    private readonly IReadOnlyList<TradeInfo> _internalTrades;
    private readonly IReadOnlyList<TradeInfo> _externalTrades;

    // Sensitivities per trade: TranNum -> (Index, Tenor) -> Delta
    private readonly Dictionary<int, Sensitivities> _sensiByTrade = new();
    // Max GptId per trade (for tie-breaking)
    private readonly Dictionary<int, int> _maxGpt = new();

    private List<TradeInfo> _externalsSorted = [];
    private readonly Dictionary<(string Ccy, string PricingModel), List<TradeInfo>> _externalsByGroup = new();

    private readonly Dictionary<(string Ccy, string PricingModel, string Way), SubPortfolioState> _subPortfolios = new();
    private readonly Dictionary<int, double> _externalUsage = new();
    private readonly List<Match> _matches = new();
    private readonly Dictionary<(string Ccy, string PricingModel, string Way), bool> _capReached = new();

    public PortfolioMatcher(
        IReadOnlyList<TradeInfo> internalTrades,
        IReadOnlyList<TradeInfo> externalTrades,
        IReadOnlyList<SensitivityRow> repSensi,
        MatcherConfig? config = null)
    {
        _internalTrades = internalTrades;
        _externalTrades = externalTrades;
        _config = config ?? new MatcherConfig();

        Aggregate(repSensi);

        _externalsSorted = externalTrades
            .OrderByDescending(t => _maxGpt.GetValueOrDefault(t.TranNum, 0))
            .ToList();
        foreach (var t in _externalsSorted)
        {
            var key = (t.Ccy, t.PricingModel);
            if (!_externalsByGroup.TryGetValue(key, out var list))
            {
                list = new List<TradeInfo>();
                _externalsByGroup[key] = list;
            }
            list.Add(t);
        }
    }

    /// <summary>One raw sensitivity row from Rep_Sensi.</summary>
    public sealed record SensitivityRow(int TranNum, int InsNum, string Index, string Tenor, string GptId, double Delta);

    private void Aggregate(IReadOnlyList<SensitivityRow> repSensi)
    {
        // Group by (TranNum, Index, Tenor) and sum deltas
        var grouped = new Dictionary<(int TranNum, string Index, string Tenor), double>();
        foreach (var row in repSensi)
        {
            var key = (row.TranNum, row.Index, row.Tenor);
            grouped[key] = grouped.GetValueOrDefault(key, 0.0) + row.Delta;
        }

        foreach (var ((tran, index, tenor), delta) in grouped)
        {
            if (!_sensiByTrade.TryGetValue(tran, out var sensi))
            {
                sensi = new Sensitivities();
                _sensiByTrade[tran] = sensi;
            }
            sensi.Set((index, tenor), delta);
        }

        // Max GptId per trade (numeric, 0 if not parseable)
        foreach (var row in repSensi)
        {
            if (int.TryParse(row.GptId, out var gpt))
            {
                _maxGpt[row.TranNum] = Math.Max(_maxGpt.GetValueOrDefault(row.TranNum, 0), gpt);
            }
        }
    }

    private void CreateSubPortfolios()
    {
        var groups = new Dictionary<(string Ccy, string PricingModel, string Way), List<TradeInfo>>();
        foreach (var trade in _internalTrades)
        {
            var key = (trade.Ccy, trade.PricingModel, trade.Way);
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<TradeInfo>();
                groups[key] = list;
            }
            list.Add(trade);
        }

        foreach (var ((ccy, pm, way), trades) in groups)
        {
            var total = new Sensitivities();
            foreach (var trade in trades)
            {
                if (_sensiByTrade.TryGetValue(trade.TranNum, out var sensi))
                {
                    foreach (var (bucket, delta) in sensi.Items)
                    {
                        total.Add(bucket, delta);
                    }
                }
            }
            var sub = new SubPortfolioState(ccy, pm, way, trades, total, total.Clone());
            _subPortfolios[sub.Key] = sub;
        }
    }

    private IEnumerable<TradeInfo> Candidates(SubPortfolioState sub)
    {
        var pool = _config.MatchCcyPm
            ? _externalsByGroup.GetValueOrDefault((sub.Ccy, sub.PricingModel), [])
            : _externalsSorted;

        foreach (var trade in pool)
        {
            if (trade.Way == sub.Way)
            {
                continue;
            }
            if (_externalUsage[trade.TranNum] >= 1.0 - _config.MinUsage)
            {
                continue;
            }
            yield return trade;
        }
    }

    private sealed record Candidate(TradeInfo Trade, double Usage, double Gain, Sensitivities Impact);

    private Candidate? Evaluate(SubPortfolioState sub, TradeInfo trade)
    {
        if (!_sensiByTrade.TryGetValue(trade.TranNum, out var e) || e.Count == 0)
        {
            return null;
        }

        if (!_config.FullImpact)
        {
            e = MatchingMath.OffsettingBuckets(sub.Current, e);
            if (e.Count == 0)
            {
                return null;
            }
        }

        var remaining = 1.0 - _externalUsage.GetValueOrDefault(trade.TranNum, 0.0);
        var usage = MatchingMath.OptimalUsage(sub.Current, e, remaining);
        if (usage < _config.MinUsage)
        {
            return null;
        }

        var gain = -MatchingMath.L1Change(sub.Current, e, usage);
        if (gain <= _config.MinGain)
        {
            return null;
        }

        return new Candidate(trade, usage, gain, e);
    }

    private Candidate? FindBestMatch(SubPortfolioState sub)
    {
        Candidate? best = null;
        // Candidates are sorted by decreasing GptId: a candidate only replaces
        // the best one if it is strictly better (> gainTol).
        foreach (var trade in Candidates(sub))
        {
            var cand = Evaluate(sub, trade);
            if (cand is not null && (best is null || cand.Gain > best.Gain + _config.GainTol))
            {
                best = cand;
            }
        }
        return best;
    }

    private void MatchSubPortfolio(SubPortfolioState sub, Action<string>? log = null)
    {
        for (var iteration = 1; iteration <= _config.MaxIterations; iteration++)
        {
            var best = FindBestMatch(sub);
            if (best is null)
            {
                return;
            }

            var tran = best.Trade.TranNum;
            _externalUsage[tran] = Math.Min(1.0, _externalUsage.GetValueOrDefault(tran, 0.0) + best.Usage);
            sub.Current.Apply(best.Impact, best.Usage, _config.ZeroTol);
            _matches.Add(new Match(sub.Name, tran, best.Usage, best.Gain, iteration));
            log?.Invoke($"{sub.Name} it.{iteration}: trade {tran} usage={best.Usage:F4} gain={best.Gain:F4} L1={sub.L1:F4}");
        }

        _capReached[sub.Key] = true;
        log?.Invoke($"{sub.Name}: iteration cap of {_config.MaxIterations} reached, matching possibly incomplete");
    }

    /// <summary>
    /// Run the full matching (state is reset) and return the result tables.
    /// </summary>
    public MatchResults MatchPortfolios(Action<string>? log = null)
    {
        _matches.Clear();
        _capReached.Clear();
        _externalUsage.Clear();
        foreach (var t in _externalTrades)
        {
            _externalUsage[t.TranNum] = 0.0;
        }
        _subPortfolios.Clear();
        CreateSubPortfolios();

        var subs = _subPortfolios.Values.ToList();
        if (_config.LargestFirst)
        {
            subs.Sort((a, b) => b.InitialL1.CompareTo(a.InitialL1));
        }

        foreach (var sub in subs)
        {
            log?.Invoke($"Matching {sub.Name} (initial L1 = {sub.InitialL1:F2})");
            MatchSubPortfolio(sub, log);
            log?.Invoke($"  -> final L1 = {sub.L1:F2}");
        }

        log?.Invoke($"Matching done: {_matches.Count} matches");
        return GetResults();
    }

    private MatchResults GetResults()
    {
        var initialRows = new List<(string, string, string, double)>();
        var remainingRows = new List<(string, string, string, double)>();
        var summaryRows = new List<(string, double, double, int, bool)>();
        var nbMatches = new Dictionary<string, int>();
        foreach (var m in _matches)
        {
            nbMatches[m.SubPortfolio] = nbMatches.GetValueOrDefault(m.SubPortfolio, 0) + 1;
        }

        foreach (var sub in _subPortfolios.Values)
        {
            foreach (var ((index, tenor), delta) in sub.Initial.Items)
            {
                initialRows.Add((sub.Name, index, tenor, delta));
            }

            var allKeys = new HashSet<(string, string)>(sub.Initial.Keys);
            foreach (var key in sub.Current.Keys)
            {
                allKeys.Add(key);
            }
            foreach (var (index, tenor) in allKeys)
            {
                remainingRows.Add((sub.Name, index, tenor, sub.Current.Get((index, tenor))));
            }

            summaryRows.Add((sub.Name, sub.InitialL1, sub.L1,
                nbMatches.GetValueOrDefault(sub.Name, 0),
                _capReached.GetValueOrDefault(sub.Key, false)));
        }

        var externalUsage = _externalTrades
            .Select(t => new ExternalUsageRow(
                t.TranNum, t.InsNum, t.ExternalParty, t.Ccy, t.PricingModel, t.Way,
                _externalUsage.GetValueOrDefault(t.TranNum, 0.0)))
            .ToList();

        return new MatchResults
        {
            InternalSubPortfolios = initialRows,
            MatchedTrades = _matches.ToList(),
            ExternalUsage = externalUsage,
            UnmatchedSensitivities = remainingRows,
            SubPortfolioSummary = summaryRows,
        };
    }
}
