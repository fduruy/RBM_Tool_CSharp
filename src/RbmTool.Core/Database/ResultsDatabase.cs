using System.Globalization;
using Microsoft.Data.Sqlite;
using RbmTool.Core.Models;
using RbmTool.Core.Matching;

namespace RbmTool.Core.Database;

/// <summary>
/// SQLite storage of inputs and results (port of database.py).
///
/// Tables (replaced at each run):
///   Inputs : gt_valo, rep_sensi, offsetting
///   Results: internal_sub_portfolios, matched_trades, external_usage,
///            unmatched_sensitivities, sub_portfolio_summary
///
/// The .db file can be linked to Excel via an ODBC connection.
/// </summary>
public sealed class ResultsDatabase
{
    private readonly string _connectionString;

    public ResultsDatabase(string dbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        // Required for bulk writes through the same connection
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    private static void ReplaceTable(SqliteConnection conn, string table, string columns, IEnumerable<object?[]> rows)
    {
        using (var drop = conn.CreateCommand())
        {
            drop.CommandText = $"DROP TABLE IF EXISTS {table};";
            drop.ExecuteNonQuery();
        }

        var placeholders = string.Join(", ", Enumerable.Range(0, columns.Split(',').Length).Select(_ => "?"));
        using (var create = conn.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE {table} ({columns});";
            create.ExecuteNonQuery();
        }

        using var insert = conn.CreateCommand();
        insert.CommandText = $"INSERT INTO {table} ({columns}) VALUES ({placeholders});";
        foreach (var row in rows)
        {
            insert.Parameters.Clear();
            foreach (var value in row)
            {
                insert.Parameters.AddWithValue(parameterName: $"@p{insert.Parameters.Count}", value ?? DBNull.Value);
            }
            insert.ExecuteNonQuery();
        }
    }

    /// <summary>Save the input tables (replaced each run).</summary>
    public void SaveInputs(InputData data)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        ReplaceTable(conn, "gt_valo", "TranNum, InsNum, ExternalParty, PricingModel, MTM, CCY, Way",
            data.Trades.Select(t => new object?[]
            {
                t.TranNum, t.InsNum, t.ExternalParty, t.PricingModel,
                t.MtM.ToString("R", CultureInfo.InvariantCulture), t.Ccy, t.Way
            }));

        ReplaceTable(conn, "rep_sensi", "TranNum, InsNum, Index, Tenor, Gpt_Id, Delta",
            data.Sensitivities.Select(s => new object?[]
            {
                s.TranNum, s.InsNum, s.Index, s.Tenor, s.GptId,
                s.Delta.ToString("R", CultureInfo.InvariantCulture)
            }));

        ReplaceTable(conn, "offsetting", "TranNum, InsNum, Daily_PL, YTD_PNL, CCY",
            data.Offsetting.Select(o => new object?[]
            {
                o.TranNum, o.InsNum,
                o.DailyPl.ToString("R", CultureInfo.InvariantCulture),
                o.YtdPnl.ToString("R", CultureInfo.InvariantCulture),
                o.Ccy
            }));

        tx.Commit();
    }

    /// <summary>
    /// Load the input tables from the database. Throws if a table is missing.
    /// </summary>
    public InputData LoadInputs()
    {
        using var conn = Open();

        var required = new[] { "gt_valo", "rep_sensi", "offsetting" };
        var existing = TableNames(conn).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = required.Where(t => !existing.Contains(t)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidDataException(
                $"Input tables missing in database: [{string.Join(", ", missing)}]. " +
                "Run with --init-db once to load the input files into the database.");
        }

        var trades = ReadRows(conn, "gt_valo").Select(r => new TradeInfo
        {
            TranNum = Convert.ToInt32(r[0], CultureInfo.InvariantCulture),
            InsNum = Convert.ToInt32(r[1], CultureInfo.InvariantCulture),
            ExternalParty = Convert.ToString(r[2], CultureInfo.InvariantCulture) ?? string.Empty,
            PricingModel = Convert.ToString(r[3], CultureInfo.InvariantCulture) ?? string.Empty,
            MtM = NumericParser.Parse(Convert.ToString(r[4], CultureInfo.InvariantCulture)),
            Ccy = Convert.ToString(r[5], CultureInfo.InvariantCulture) ?? string.Empty,
            Way = Convert.ToString(r[6], CultureInfo.InvariantCulture) ?? string.Empty,
            IsInternal = (Convert.ToString(r[2], CultureInfo.InvariantCulture) ?? string.Empty)
                .StartsWith("CLF", StringComparison.Ordinal)
                || (Convert.ToString(r[2], CultureInfo.InvariantCulture) ?? string.Empty)
                    .StartsWith("FSA", StringComparison.Ordinal),
        }).ToList();

        var sensi = ReadRows(conn, "rep_sensi").Select(r => new PortfolioMatcher.SensitivityRow(
            Convert.ToInt32(r[0], CultureInfo.InvariantCulture),
            Convert.ToInt32(r[1], CultureInfo.InvariantCulture),
            Convert.ToString(r[2], CultureInfo.InvariantCulture) ?? string.Empty,
            Convert.ToString(r[3], CultureInfo.InvariantCulture) ?? string.Empty,
            Convert.ToString(r[4], CultureInfo.InvariantCulture) ?? string.Empty,
            NumericParser.Parse(Convert.ToString(r[5], CultureInfo.InvariantCulture)))).ToList();

        var offsetting = ReadRows(conn, "offsetting").Select(r => new PnlRow(
            Convert.ToInt32(r[0], CultureInfo.InvariantCulture),
            Convert.ToInt32(r[1], CultureInfo.InvariantCulture),
            NumericParser.Parse(Convert.ToString(r[2], CultureInfo.InvariantCulture)),
            NumericParser.Parse(Convert.ToString(r[3], CultureInfo.InvariantCulture)),
            Convert.ToString(r[4], CultureInfo.InvariantCulture) ?? string.Empty)).ToList();

        return new InputData { Trades = trades, Sensitivities = sensi, Offsetting = offsetting };
    }

    private static IEnumerable<string> TableNames(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            yield return reader.GetString(0);
        }
    }

    private static List<object?[]> ReadRows(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {table}";
        var rows = new List<object?[]>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];
            reader.GetValues(row);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Save the result tables (replaced each run).</summary>
    public void SaveResults(MatchResults results)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        ReplaceTable(conn, "internal_sub_portfolios", "SubPortfolio, Index, Tenor, Delta",
            results.InternalSubPortfolios.Select(r => new object?[]
            {
                r.SubPortfolio, r.Index, r.Tenor,
                r.Delta.ToString("R", CultureInfo.InvariantCulture)
            }));

        ReplaceTable(conn, "matched_trades", "SubPortfolio, External_TranNum, Usage, Reduction, Iteration",
            results.MatchedTrades.Select(m => new object?[]
            {
                m.SubPortfolio, m.ExternalTranNum,
                m.Usage.ToString("R", CultureInfo.InvariantCulture),
                m.Reduction.ToString("R", CultureInfo.InvariantCulture),
                m.Iteration
            }));

        ReplaceTable(conn, "external_usage", "TranNum, InsNum, ExternalParty, CCY, PricingModel, Way, Usage",
            results.ExternalUsage.Select(e => new object?[]
            {
                e.TranNum, e.InsNum, e.ExternalParty, e.Ccy, e.PricingModel, e.Way,
                e.Usage.ToString("R", CultureInfo.InvariantCulture)
            }));

        ReplaceTable(conn, "unmatched_sensitivities", "SubPortfolio, Index, Tenor, Delta",
            results.UnmatchedSensitivities.Select(r => new object?[]
            {
                r.SubPortfolio, r.Index, r.Tenor,
                r.Delta.ToString("R", CultureInfo.InvariantCulture)
            }));

        ReplaceTable(conn, "sub_portfolio_summary", "SubPortfolio, Initial_L1, Final_L1, Nb_Matches, Iteration_Cap_Reached",
            results.SubPortfolioSummary.Select(s => new object?[]
            {
                s.SubPortfolio,
                s.InitialL1.ToString("R", CultureInfo.InvariantCulture),
                s.FinalL1.ToString("R", CultureInfo.InvariantCulture),
                s.NbMatches,
                s.CapReached ? 1 : 0
            }));

        tx.Commit();
    }
}
