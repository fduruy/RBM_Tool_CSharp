using System.Globalization;
using System.Text;
using RbmTool.Core.Models;
using RbmTool.Core.Matching;

namespace RbmTool.Core.Outputs;

/// <summary>
/// CSV outputs with ';' separator and English decimal format (port of outputs.py).
/// Filenames do NOT contain dates.
/// </summary>
public static class OutputWriter
{
    public static void WriteAll(MatchResults results, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        WriteTable(Path.Combine(outputDir, "Internal_SubPortfolios.csv"),
            new[] { "SubPortfolio", "Index", "Tenor", "Delta" },
            results.InternalSubPortfolios.Select(r => new[] { r.SubPortfolio, r.Index, r.Tenor, Fmt(r.Delta) }));

        WriteTable(Path.Combine(outputDir, "Matched_Trades.csv"),
            new[] { "SubPortfolio", "External_TranNum", "Usage", "Reduction", "Iteration" },
            results.MatchedTrades.Select(m => new[] { m.SubPortfolio, m.ExternalTranNum.ToString(CultureInfo.InvariantCulture), Fmt(m.Usage), Fmt(m.Reduction), m.Iteration.ToString(CultureInfo.InvariantCulture) }));

        WriteTable(Path.Combine(outputDir, "External_Usage.csv"),
            new[] { "TranNum", "InsNum", "ExternalParty", "CCY", "PricingModel", "Way", "Usage" },
            results.ExternalUsage.Select(e => new[] { e.TranNum.ToString(CultureInfo.InvariantCulture), e.InsNum?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, e.ExternalParty, e.Ccy, e.PricingModel, e.Way, Fmt(e.Usage) }));

        WriteTable(Path.Combine(outputDir, "Unmatched_Sensitivities.csv"),
            new[] { "SubPortfolio", "Index", "Tenor", "Delta" },
            results.UnmatchedSensitivities.Select(r => new[] { r.SubPortfolio, r.Index, r.Tenor, Fmt(r.Delta) }));

        WriteTable(Path.Combine(outputDir, "Sub_Portfolio_Summary.csv"),
            new[] { "SubPortfolio", "Initial_L1", "Final_L1", "Nb_Matches", "Iteration_Cap_Reached" },
            results.SubPortfolioSummary.Select(s => new[] { s.SubPortfolio, Fmt(s.InitialL1), Fmt(s.FinalL1), s.NbMatches.ToString(CultureInfo.InvariantCulture), s.CapReached ? "1" : "0" }));
    }

    private static string Fmt(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static void WriteTable(string path, string[] header, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(";", header));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(";", row));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }
}
