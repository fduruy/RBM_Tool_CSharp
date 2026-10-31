using RbmTool.Core.Matching;

namespace RbmTool.Core.Models;

/// <summary>All input data loaded from files or database.</summary>
public sealed class InputData
{
    /// <summary>Trades from GT_Valo.</summary>
    public IReadOnlyList<TradeInfo> Trades { get; init; } = [];

    /// <summary>Sensitivities from Rep_Sensi (raw rows).</summary>
    public IReadOnlyList<PortfolioMatcher.SensitivityRow> Sensitivities { get; init; } = [];

    /// <summary>PnL rows from Offsetting.</summary>
    public IReadOnlyList<PnlRow> Offsetting { get; init; } = [];
}

/// <summary>One PnL row from Offsetting.</summary>
public sealed record PnlRow(int TranNum, int InsNum, double DailyPl, double YtdPnl, string Ccy);
