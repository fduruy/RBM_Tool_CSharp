using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using RbmTool.Core.Models;
using RbmTool.Core.Matching;

namespace RbmTool.Core.Inputs;

/// <summary>
/// Reads and validates the input CSV files (port of inputs.py).
/// Files use ';' as separator and ';' decimal values in English format (dot).
/// </summary>
public static class InputReader
{
    private sealed class GtValoRow
    {
        public string TranNum { get; set; } = string.Empty;
        public string InsNum { get; set; } = string.Empty;
        public string ExternalParty { get; set; } = string.Empty;
        public string PricingModel { get; set; } = string.Empty;
        public string MTM { get; set; } = string.Empty;
        public string CCY { get; set; } = string.Empty;
        public string Way { get; set; } = string.Empty;
    }

    private sealed class RepSensiRow
    {
        public string TranNum { get; set; } = string.Empty;
        public string InsNum { get; set; } = string.Empty;
        public string Index { get; set; } = string.Empty;
        public string Tenor { get; set; } = string.Empty;
        public string Gpt_Id { get; set; } = string.Empty;
        public string Delta { get; set; } = string.Empty;
    }

    private sealed class OffsettingRow
    {
        public string TranNum { get; set; } = string.Empty;
        public string InsNum { get; set; } = string.Empty;
        public string Daily_PL { get; set; } = string.Empty;
        public string YTD_PNL { get; set; } = string.Empty;
        public string CCY { get; set; } = string.Empty;
    }

    private sealed class SemicolonClassMap<T> : ClassMap<T>
    {
    }

    private static CsvConfiguration CsvConfig() => new(CultureInfo.InvariantCulture)
    {
        Delimiter = ";",
        MissingFieldFound = null,
        BadDataFound = null,
        TrimOptions = TrimOptions.Trim,
    };

    private static List<T> ReadCsv<T>(string path, ClassMap<T>? map = null)
    {
        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, CsvConfig());
        if (map is not null)
        {
            csv.Context.RegisterClassMap(map);
        }
        return csv.GetRecords<T>().ToList();
    }

    private static List<T> ReadCsvWithHeaderCheck<T>(string path, string[] requiredColumns)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Input file not found: {path}");
        }

        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, CsvConfig());
        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecords ?? [];
        var missing = requiredColumns.Where(c => !headers.Contains(c, StringComparer.Ordinal)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} missing required columns: [{string.Join(", ", missing)}]");
        }

        var records = new List<T>();
        while (csv.Read())
        {
            var record = csv.GetRecord<T>();
            records.Add(record);
        }
        return records;
    }

    /// <summary>
    /// Read and validate all input files from a directory (GT_Valo.csv, Rep_Sensi.csv, Offsetting.csv).
    /// Throws on any validation or consistency error.
    /// </summary>
    public static InputData ReadAndValidate(string inputDir)
    {
        var gtValoPath = Path.Combine(inputDir, "GT_Valo.csv");
        var repSensiPath = Path.Combine(inputDir, "Rep_Sensi.csv");
        var offsettingPath = Path.Combine(inputDir, "Offsetting.csv");

        var gtRows = ReadCsvWithHeaderCheck<GtValoRow>(gtValoPath,
            ["TranNum", "InsNum", "ExternalParty", "PricingModel", "MTM", "CCY", "Way"]);
        var repRows = ReadCsvWithHeaderCheck<RepSensiRow>(repSensiPath,
            ["TranNum", "InsNum", "Index", "Tenor", "Gpt_Id", "Delta"]);
        var offRows = ReadCsvWithHeaderCheck<OffsettingRow>(offsettingPath,
            ["TranNum", "InsNum", "Daily_PL", "YTD_PNL", "CCY"]);

        var trades = gtRows.Select(r => new TradeInfo
        {
            TranNum = int.Parse(r.TranNum, CultureInfo.InvariantCulture),
            InsNum = int.Parse(r.InsNum, CultureInfo.InvariantCulture),
            ExternalParty = r.ExternalParty,
            PricingModel = r.PricingModel,
            MtM = NumericParser.Parse(r.MTM),
            Ccy = r.CCY,
            Way = r.Way,
            IsInternal = r.ExternalParty.StartsWith("CLF", StringComparison.Ordinal)
                      || r.ExternalParty.StartsWith("FSA", StringComparison.Ordinal),
        }).ToList();

        var sensi = repRows.Select(r => new PortfolioMatcher.SensitivityRow(
            int.Parse(r.TranNum, CultureInfo.InvariantCulture),
            int.Parse(r.InsNum, CultureInfo.InvariantCulture),
            r.Index, r.Tenor, r.Gpt_Id,
            NumericParser.Parse(r.Delta))).ToList();

        var offsetting = offRows.Select(r => new PnlRow(
            int.Parse(r.TranNum, CultureInfo.InvariantCulture),
            int.Parse(r.InsNum, CultureInfo.InvariantCulture),
            NumericParser.Parse(r.Daily_PL),
            NumericParser.Parse(r.YTD_PNL),
            r.CCY)).ToList();

        Validate(trades, sensi, offsetting);
        return new InputData { Trades = trades, Sensitivities = sensi, Offsetting = offsetting };
    }

    private static void Validate(
        IReadOnlyList<TradeInfo> trades,
        IReadOnlyList<PortfolioMatcher.SensitivityRow> sensi,
        IReadOnlyList<PnlRow> offsetting)
    {
        if (trades.Select(t => t.TranNum).GroupBy(t => t).Any(g => g.Count() > 1))
        {
            throw new InvalidDataException("GT_Valo has duplicate TranNum values");
        }
        if (offsetting.Select(o => o.TranNum).GroupBy(t => t).Any(g => g.Count() > 1))
        {
            throw new InvalidDataException("Offsetting has duplicate TranNum values");
        }

        // Consistency: every trade must have sensitivities and PnL
        var tradeNums = trades.Select(t => t.TranNum).ToHashSet();
        var sensiNums = sensi.Select(s => s.TranNum).ToHashSet();
        var offNums = offsetting.Select(o => o.TranNum).ToHashSet();

        var missingSensi = tradeNums.Except(sensiNums).OrderBy(x => x).ToList();
        if (missingSensi.Count > 0)
        {
            throw new InvalidDataException($"Trades without sensitivities: [{string.Join(", ", missingSensi)}]");
        }

        var missingPnl = tradeNums.Except(offNums).OrderBy(x => x).ToList();
        if (missingPnl.Count > 0)
        {
            throw new InvalidDataException($"Trades without PnL data: [{string.Join(", ", missingPnl)}]");
        }
    }
}
