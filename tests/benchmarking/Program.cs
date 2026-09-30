// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Reflection;
using System.Text;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace AterraEngine.Core.DependencyInjection.Benchmarks;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
internal static class Program {

    private static readonly string[] RequiredColumns = ["Method", "Categories", "Mean", "Allocated", "Alloc Ratio"];
    private static readonly string[] ReportColumns = ["Method", "Categories", "Mean", "Error", "StdDev", "Ratio", "RatioSD", "Allocated", "Alloc Ratio"];
    public static void Main(string[] args) {
        Assembly assembly = typeof(Program).Assembly;
        // Keep invocation count and unroll factor unset so DefaultJob retains automatic batching.
        Job job = Job.Default.WithLaunchCount(3).WithWarmupCount(10).WithIterationCount(20);
        ManualConfig config = ManualConfig.Create(DefaultConfig.Instance).AddJob(job).AddColumn(StatisticColumn.Median);
        string[] runArguments = args.Any(static argument => argument.Equals("--filter", StringComparison.Ordinal) || argument.StartsWith("--filter=", StringComparison.Ordinal))
            ? args
            : [.. args, "--filter", "*"];
        IEnumerable<Summary> summaries = BenchmarkSwitcher.FromAssembly(assembly).Run(runArguments, config);
        WriteAggregateReport(summaries);
    }

    private static void WriteAggregateReport(IEnumerable<Summary> summaries) {
        Summary[] summaryArray = summaries.ToArray();
        if (summaryArray.Length == 0) return;

        string resultsDirectory = Path.GetFullPath(summaryArray[0].ResultsDirectoryPath);
        if (!Directory.Exists(resultsDirectory))
            throw new DirectoryNotFoundException($"BenchmarkDotNet results directory does not exist: {resultsDirectory}");

        List<Dictionary<string, string>> rows = [];
        HashSet<string> columns = new(StringComparer.Ordinal);
        foreach (Summary summary in summaryArray) {
            string summaryResultsDirectory = Path.GetFullPath(summary.ResultsDirectoryPath);
            if (!string.Equals(summaryResultsDirectory, resultsDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Benchmark summaries were written to more than one results directory: '{resultsDirectory}' and '{summaryResultsDirectory}'.");

            string reportPath = Path.Combine(resultsDirectory, $"{summary.Title}-report.csv");
            if (!File.Exists(reportPath)) throw new FileNotFoundException($"BenchmarkDotNet did not emit the expected CSV for {summary.Title}.", reportPath);

            List<string> lines = File.ReadAllLines(reportPath).Where(static line => !string.IsNullOrWhiteSpace(line)).ToList();
            if (lines.Count < 2) throw new InvalidOperationException($"BenchmarkDotNet emitted no result rows for {summary.Title}: {reportPath}");

            List<string> headers = ParseCsvLine(lines[0]);
            if (headers.Count != headers.Distinct(StringComparer.Ordinal).Count())
                throw new InvalidOperationException($"BenchmarkDotNet emitted duplicate CSV columns for {summary.Title}: {reportPath}");

            foreach (string requiredColumn in RequiredColumns) {
                if (!headers.Contains(requiredColumn, StringComparer.Ordinal))
                    throw new InvalidOperationException($"BenchmarkDotNet CSV is missing '{requiredColumn}' for {summary.Title}: {reportPath}");
            }

            HashSet<string> summaryMethods = summary.Reports
                .Select(static report => report.BenchmarkCase.Descriptor.WorkloadMethod.Name)
                .ToHashSet(StringComparer.Ordinal);
            HashSet<string> csvMethods = new(StringComparer.Ordinal);
            foreach (string line in lines.Skip(1)) {
                List<string> values = ParseCsvLine(line);
                if (values.Count != headers.Count) throw new InvalidOperationException($"Malformed BenchmarkDotNet CSV row in {reportPath}");

                Dictionary<string, string> row = headers.Zip(values, resultSelector: static (header, value) => (header, value)).ToDictionary(keySelector: static pair => pair.header, elementSelector: static pair => pair.value, StringComparer.Ordinal);
                string method = row["Method"];
                if (!csvMethods.Add(method) || !summaryMethods.Contains(method))
                    throw new InvalidOperationException($"BenchmarkDotNet CSV row '{method}' does not match exactly one benchmark in {summary.Title}: {reportPath}");

                if (row.TryGetValue("Median", out string? median) && string.IsNullOrWhiteSpace(median))
                    row["Median"] = "N/A";
                rows.Add(row);
                columns.UnionWith(headers);
            }

            if (!summaryMethods.SetEquals(csvMethods))
                throw new InvalidOperationException($"BenchmarkDotNet CSV rows do not cover every benchmark in {summary.Title}: {reportPath}");
        }

        string outputPath = Path.Combine(resultsDirectory, "aggregate-report.md");
        List<string> outputColumns = ReportColumns.Where(columns.Contains).ToList();
        if (columns.Contains("Median")) {
            int medianIndex = outputColumns.IndexOf("Ratio");
            outputColumns.Insert(medianIndex < 0 ? outputColumns.Count : medianIndex, "Median");
        }
        outputColumns.AddRange(columns.Where(static column => column.StartsWith("Gen", StringComparison.Ordinal))
            .OrderBy(static column => column, StringComparer.Ordinal));
        StringBuilder markdown = new();
        markdown.AppendLine("# Benchmark Aggregate Report");
        markdown.AppendLine();
        markdown.AppendLine($"| {string.Join(" | ", outputColumns)} |");
        markdown.AppendLine($"| {string.Join(" | ", outputColumns.Select(static _ => "---"))} |");
        foreach (Dictionary<string, string> row in rows) {
            markdown.AppendLine($"| {string.Join(" | ", outputColumns.Select(column => Cell(row, column)))} |");
        }

        File.WriteAllText(outputPath, markdown.ToString());
        Console.WriteLine($"Aggregate benchmark report: {Path.GetFullPath(outputPath)}");
    }

    private static string Cell(Dictionary<string, string> row, string column) => Escape(row.GetValueOrDefault(column, "N/A"));

    private static string Escape(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static List<string> ParseCsvLine(string line) {
        List<string> values = [];
        StringBuilder value = new();
        bool quoted = false;
        for (int index = 0; index < line.Length; index++) {
            char character = line[index];
            switch (character) {
                case '"' when quoted && index + 1 < line.Length && line[index + 1] == '"':
                    value.Append('"');
                    index++;
                    break;
                case '"':
                    quoted = !quoted;
                    break;
                case ',' when !quoted:
                    values.Add(value.ToString());
                    value.Clear();
                    break;
                default:
                    value.Append(character);
                    break;
            }
        }

        if (quoted) throw new InvalidOperationException("Malformed quoted CSV value.");

        values.Add(value.ToString());
        return values;
    }
}
