using RbmTool.Core.Models;
using RbmTool.Core.Matching;

namespace RbmTool.Core.Calculations;

/// <summary>
/// Entry point of the computation (port of calculations.py + trade_processing.py).
/// Identifies internal/external trades and runs the matching.
/// </summary>
public static class TradeProcessor
{
    /// <summary>Internal trades: ExternalParty starts with CLF or FSA.</summary>
    public static List<TradeInfo> IdentifyInternal(IReadOnlyList<TradeInfo> trades) =>
        trades.Where(t => t.IsInternal).ToList();

    /// <summary>External trades: everything else.</summary>
    public static List<TradeInfo> IdentifyExternal(IReadOnlyList<TradeInfo> trades) =>
        trades.Where(t => !t.IsInternal).ToList();

    /// <summary>
    /// Run the full matching on the input data and return the result tables.
    /// </summary>
    public static MatchResults ProcessAll(InputData data, MatcherConfig? config = null, Action<string>? log = null)
    {
        var internalTrades = IdentifyInternal(data.Trades);
        var externalTrades = IdentifyExternal(data.Trades);

        log?.Invoke($"Internal trades: {internalTrades.Count}");
        log?.Invoke($"External trades: {externalTrades.Count}");

        var matcher = new PortfolioMatcher(internalTrades, externalTrades, data.Sensitivities, config);
        return matcher.MatchPortfolios(log);
    }
}
