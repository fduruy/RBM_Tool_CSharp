using RbmTool.Core.Calculations;
using RbmTool.Core.Database;
using RbmTool.Core.Inputs;
using RbmTool.Core.Outputs;

namespace RbmTool.Cli;

/// <summary>
/// Entry point (port of main.py).
///
/// Inputs are read from the SQLite database. Use --init-db once to populate
/// the database from the input files.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var inputDir = "data/input/";
        var outputDir = "data/output/";
        var dbPath = Path.Combine("data", "database", "rbm_results.db");
        var useSamples = false;
        var initDb = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input-dir":
                    inputDir = ArgsValue(args, ++i, args[i]);
                    break;
                case "--output-dir":
                    outputDir = ArgsValue(args, ++i, args[i]);
                    break;
                case "--db-path":
                    dbPath = ArgsValue(args, ++i, args[i]);
                    break;
                case "--samples":
                    useSamples = true;
                    break;
                case "--init-db":
                    initDb = true;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    Console.Error.WriteLine("Usage: RbmTool.Cli [--input-dir DIR] [--output-dir DIR] [--db-path FILE] [--samples] [--init-db]");
                    return 1;
            }
        }

        try
        {
            Log("RBM_TOOL - Starting processing");

            var db = new ResultsDatabase(dbPath);

            // Step 0 (optional): populate the database from input files
            if (initDb)
            {
                var sourceDir = useSamples ? Path.Combine("data", "samples") : inputDir;
                Log($"Initializing database from input files: {sourceDir}");
                var inputData = InputReader.ReadAndValidate(sourceDir);
                db.SaveInputs(inputData);
            }

            // Step 1: read inputs from the database
            Log("Step 1: Reading input tables from SQLite database");
            var data = db.LoadInputs();
            Log($"  - Trades: {data.Trades.Count}");
            Log($"  - Sensitivities: {data.Sensitivities.Count}");
            Log($"  - Offsetting: {data.Offsetting.Count}");

            // Step 2: matching
            Log("Step 2: Processing trades and performing matching...");
            var results = TradeProcessor.ProcessAll(data, log: Log);
            Log($"Processing complete: {results.MatchedTrades.Count} matches, {results.SubPortfolioSummary.Count} sub-portfolios");

            // Step 3: CSV outputs
            Log($"Step 3: Writing output files to {outputDir}");
            OutputWriter.WriteAll(results, outputDir);

            // Step 4: SQLite results (tables replaced each run)
            Log("Step 4: Saving result tables to SQLite database");
            db.SaveResults(results);

            Log("RBM_TOOL - Processing completed successfully!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"RBM_TOOL - Processing failed: {ex.Message}");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static string ArgsValue(string[] args, int index, string name)
    {
        if (index >= args.Length)
        {
            Console.Error.WriteLine($"Missing value for {name}");
            Environment.Exit(1);
        }
        return args[index];
    }

    private static void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
    }
}
