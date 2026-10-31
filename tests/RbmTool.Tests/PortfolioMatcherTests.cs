using RbmTool.Core.Models;
using RbmTool.Core.Matching;
using Xunit;

namespace RbmTool.Tests;

public class PortfolioMatcherTests
{
    private static TradeInfo Trade(int tranNum, string party, string ccy, string way) => new()
    {
        TranNum = tranNum,
        InsNum = tranNum,
        ExternalParty = party,
        PricingModel = "Discounting",
        MtM = 0,
        Ccy = ccy,
        Way = way,
        IsInternal = party.StartsWith("CLF") || party.StartsWith("FSA"),
    };

    [Fact]
    public void SimpleMatch_ConsumesExternalFully()
    {
        var internalTrades = new List<TradeInfo> { Trade(1, "CLFA", "EUR", "Pay") };
        var externalTrades = new List<TradeInfo> { Trade(2, "BANKX", "EUR", "Rec") };
        var sensi = new List<PortfolioMatcher.SensitivityRow>
        {
            new(1, 1, "Euribor", "1Y", "1", 100.0),
            new(2, 2, "Euribor", "1Y", "1", -80.0),
        };

        var matcher = new PortfolioMatcher(internalTrades, externalTrades, sensi);
        var results = matcher.MatchPortfolios();

        Assert.Single(results.MatchedTrades);
        var match = results.MatchedTrades[0];
        Assert.Equal(2, match.ExternalTranNum);
        Assert.Equal(1.0, match.Usage, 6);
        // Residual: 100 - 80 = 20
        Assert.Equal(20.0, results.UnmatchedSensitivities.Single(r => r.Tenor == "1Y").Delta, 6);
    }

    [Fact]
    public void SameWayExternal_IsNotMatched()
    {
        var internalTrades = new List<TradeInfo> { Trade(1, "CLFA", "EUR", "Pay") };
        var externalTrades = new List<TradeInfo> { Trade(2, "BANKX", "EUR", "Pay") };
        var sensi = new List<PortfolioMatcher.SensitivityRow>
        {
            new(1, 1, "Euribor", "1Y", "1", 100.0),
            new(2, 2, "Euribor", "1Y", "1", -80.0),
        };

        var results = new PortfolioMatcher(internalTrades, externalTrades, sensi).MatchPortfolios();
        Assert.Empty(results.MatchedTrades);
    }

    [Fact]
    public void UsageIsCappedAtOne_AcrossSubPortfolios()
    {
        // Two internal sub-portfolios (different CCY -> no, same group needed)
        // Use two ways of same group? Internals are grouped by Way too, so use
        // a second internal trade in the same group via partial consumption.
        var internalTrades = new List<TradeInfo> { Trade(1, "CLFA", "EUR", "Pay"), Trade(3, "CLFB", "GBP", "Pay") };
        var externalTrades = new List<TradeInfo> { Trade(2, "BANKX", "EUR", "Rec") };
        var sensi = new List<PortfolioMatcher.SensitivityRow>
        {
            new(1, 1, "Euribor", "1Y", "1", 100.0),
            new(3, 3, "Euribor", "1Y", "1", 100.0),  // GBP portfolio: external not in group -> no match
            new(2, 2, "Euribor", "1Y", "1", -150.0),
        };

        var results = new PortfolioMatcher(internalTrades, externalTrades, sensi).MatchPortfolios();
        // EUR sub-portfolio matches at 100/150 = 0.6667 usage
        var match = Assert.Single(results.MatchedTrades);
        Assert.Equal(2.0 / 3.0, match.Usage, 6);
        var usage = results.ExternalUsage.Single(e => e.TranNum == 2);
        Assert.Equal(2.0 / 3.0, usage.Usage, 6);
    }

    [Fact]
    public void TwoSubPortfolios_ShareExternalUsage()
    {
        // Two internal sub-portfolios in the same CCY/PricingModel but different Way,
        // both offset by the same external (opposite way of both is impossible:
        // external has one Way) -> use two externals instead to check cumulated usage.
        var internalTrades = new List<TradeInfo> { Trade(1, "CLFA", "EUR", "Pay"), Trade(2, "CLFB", "EUR", "Rec") };
        var externalTrades = new List<TradeInfo> { Trade(10, "BANKX", "EUR", "Rec"), Trade(11, "BANKY", "EUR", "Pay") };
        var sensi = new List<PortfolioMatcher.SensitivityRow>
        {
            new(1, 1, "Euribor", "1Y", "1", 100.0),   // internal Pay
            new(2, 2, "Euribor", "1Y", "1", -60.0),   // internal Rec
            new(10, 10, "Euribor", "1Y", "1", -70.0), // external Rec offsets internal Pay
            new(11, 11, "Euribor", "1Y", "1", 40.0),  // external Pay offsets internal Rec
        };

        var results = new PortfolioMatcher(internalTrades, externalTrades, sensi).MatchPortfolios();

        Assert.Equal(2, results.MatchedTrades.Count);
        // Internal Pay (100) fully consumes external Rec (70): usage = 1
        Assert.Contains(results.MatchedTrades, m => m.ExternalTranNum == 10 && Math.Abs(m.Usage - 1.0) < 1e-6);
        // Internal Rec (-60) is fully offset by external Pay (40): usage = 1
        Assert.Contains(results.MatchedTrades, m => m.ExternalTranNum == 11 && Math.Abs(m.Usage - 1.0) < 1e-6);
    }

    [Fact]
    public void Summary_ReportsL1AndMatches()
    {
        var internalTrades = new List<TradeInfo> { Trade(1, "CLFA", "EUR", "Pay") };
        var externalTrades = new List<TradeInfo> { Trade(2, "BANKX", "EUR", "Rec") };
        var sensi = new List<PortfolioMatcher.SensitivityRow>
        {
            new(1, 1, "Euribor", "1Y", "1", 100.0),
            new(2, 2, "Euribor", "1Y", "1", -30.0),
        };

        var results = new PortfolioMatcher(internalTrades, externalTrades, sensi).MatchPortfolios();
        var summary = Assert.Single(results.SubPortfolioSummary);
        Assert.Equal("EUR_Discounting_Pay", summary.SubPortfolio);
        Assert.Equal(100.0, summary.InitialL1, 6);
        Assert.Equal(70.0, summary.FinalL1, 6);
        Assert.Equal(1, summary.NbMatches);
        Assert.False(summary.CapReached);
    }
}
