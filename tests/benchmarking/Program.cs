// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Reflection;
using System.Text;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace AterraEngine.Core.DependencyInjection.Benchmarks;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
internal static class Program {

    private static readonly string[] RequiredColumns = ["Method", "Categories", "Mean", "Error", "StdDev", "Ratio", "RatioSD", "Allocated", "Alloc Ratio"];
    public static void Main(string[] args) {
        Assembly assembly = typeof(Program).Assembly;
        ManualConfig config = ManualConfig.Create(DefaultConfig.Instance).AddColumn(StatisticColumn.Median);
        IEnumerable<Summary> summaries = BenchmarkSwitcher.FromAssembly(assembly).Run(args, config);
        WriteAggregateReport(summaries);
    }

    private static void WriteAggregateReport(IEnumerable<Summary> summaries) {
        Summary[] summaryArray = summaries.ToArray();
        if (summaryArray.Length == 0) return;

        string resultsDirectory = summaryArray[0].ResultsDirectoryPath;
        List<Dictionary<string, string>> rows = [];
        foreach (Summary summary in summaryArray) {
            string reportPath = Path.Combine(resultsDirectory, $"{summary.Title}-report.csv");
            if (!File.Exists(reportPath)) throw new FileNotFoundException($"BenchmarkDotNet did not emit the expected CSV for {summary.Title}.", reportPath);

            List<string> lines = File.ReadAllLines(reportPath).Where(static line => !string.IsNullOrWhiteSpace(line)).ToList();
            if (lines.Count < 2) throw new InvalidOperationException($"BenchmarkDotNet emitted no result rows for {summary.Title}: {reportPath}");

            List<string> headers = ParseCsvLine(lines[0]);
            foreach (string requiredColumn in RequiredColumns) {
                if (!headers.Contains(requiredColumn, StringComparer.Ordinal))
                    throw new InvalidOperationException($"BenchmarkDotNet CSV is missing '{requiredColumn}' for {summary.Title}: {reportPath}");
            }

            foreach (string line in lines.Skip(1)) {
                List<string> values = ParseCsvLine(line);
                if (values.Count != headers.Count) throw new InvalidOperationException($"Malformed BenchmarkDotNet CSV row in {reportPath}");

                Dictionary<string, string> row = headers.Zip(values, resultSelector: static (header, value) => (header, value)).ToDictionary(keySelector: static pair => pair.header, elementSelector: static pair => pair.value, StringComparer.Ordinal);
                BenchmarkReport? report = summary.Reports.FirstOrDefault(candidate => candidate.BenchmarkCase.Descriptor.WorkloadMethod.Name == row["Method"]);
                if (!row.TryGetValue("Median", out string? median) || string.IsNullOrWhiteSpace(median))
                    row["Median"] = report is null || report.ResultStatistics is null ? "N/A" : StatisticColumn.Median.GetValue(summary, report.BenchmarkCase);
                rows.Add(row);
            }
        }

        string outputPath = Path.Combine(resultsDirectory, "aggregate-report.md");
        StringBuilder markdown = new();
        markdown.AppendLine("# Benchmark Aggregate Report");
        markdown.AppendLine();
        markdown.AppendLine("| Method | Categories | Mean | Error | StdDev | Median | Ratio | RatioSD | Allocated | Alloc Ratio |");
        markdown.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (Dictionary<string, string> row in rows) {
            markdown.AppendLine($"| {Cell(row, "Method")} | {Cell(row, "Categories")} | {Cell(row, "Mean")} | {Cell(row, "Error")} | {Cell(row, "StdDev")} | {Cell(row, "Median")} | {Cell(row, "Ratio")} | {Cell(row, "RatioSD")} | {Cell(row, "Allocated")} | {Cell(row, "Alloc Ratio")} |");
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
