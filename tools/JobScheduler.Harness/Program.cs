using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;

return await Harness.RunAsync(args);

internal static class Harness
{
    private const int DefaultCoverageThreshold = 70;

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = HarnessOptions.Parse(args);
            var root = FindRepositoryRoot(Environment.CurrentDirectory);

            if (options.Mode == "coverage")
            {
                VerifyCoverage(
                    Path.GetFullPath(options.ResultsRoot!, root),
                    options.ExpectedReports,
                    options.CoverageThreshold);
                Console.WriteLine("coverage harness passed.");
                return 0;
            }

            var solution = Path.Combine(root, "JobScheduler.slnx");
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            if (!options.NoRestore)
            {
                await RunDotNetAsync(dotnet, root, "restore", solution);
            }

            await RunDotNetAsync(dotnet, root, "format", solution, "--no-restore", "--verify-no-changes");
            await RunDotNetAsync(
                dotnet,
                root,
                "build",
                solution,
                "--configuration",
                "Release",
                "--no-restore");

            var testRoot = Path.Combine(root, "tests");
            var testProjects = Directory
                .EnumerateFiles(testRoot, "*.csproj", SearchOption.AllDirectories)
                .Where(path => Path.GetFileNameWithoutExtension(path).EndsWith("Tests", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (testProjects.Length == 0)
            {
                throw new InvalidOperationException("No test projects matching *Tests.csproj were found.");
            }

            var resultsRoot = Path.Combine(root, "artifacts", "harness", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(resultsRoot);

            foreach (var project in testProjects)
            {
                var projectName = Path.GetFileNameWithoutExtension(project);
                var kind = projectName.EndsWith("IntegrationTests", StringComparison.Ordinal)
                    ? "integration"
                    : "unit";
                Console.WriteLine($"Running {kind} tests: {Path.GetFileName(project)}");

                await RunDotNetAsync(
                    dotnet,
                    root,
                    "test",
                    project,
                    "--configuration",
                    "Release",
                    "--no-build",
                    "--collect:XPlat Code Coverage",
                    "--results-directory",
                    Path.Combine(resultsRoot, projectName));
            }

            VerifyCoverage(resultsRoot, testProjects.Length, options.CoverageThreshold);

            await RunDotNetAsync(
                dotnet,
                root,
                "package",
                "list",
                "--project",
                solution,
                "--vulnerable",
                "--include-transitive",
                "--no-restore");

            Console.WriteLine($"{options.Mode} harness passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Harness failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task RunDotNetAsync(
        string dotnet,
        string workingDirectory,
        params string[] arguments)
    {
        Console.WriteLine($"dotnet {string.Join(' ', arguments.Select(QuoteForDisplay))}");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = dotnet,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet command failed with exit code {process.ExitCode}.");
        }
    }

    private static void VerifyCoverage(string resultsRoot, int expectedReports, int threshold)
    {
        var reports = Directory
            .EnumerateFiles(resultsRoot, "coverage.cobertura.xml", SearchOption.AllDirectories)
            .ToArray();

        if (reports.Length != expectedReports)
        {
            throw new InvalidOperationException(
                $"Expected {expectedReports} coverage reports, found {reports.Length}.");
        }

        var lines = new Dictionary<(string File, int Number), bool>();
        foreach (var report in reports)
        {
            var coverage = XDocument.Load(report).Root
                ?? throw new InvalidOperationException($"Coverage report is empty: {report}");
            foreach (var @class in coverage.Descendants("class"))
            {
                var file = @class.Attribute("filename")?.Value
                    ?? throw new InvalidOperationException($"Coverage class has no filename: {report}");
                foreach (var line in @class.Element("lines")?.Elements("line") ?? [])
                {
                    var number = ParseAttribute(line, "number", report);
                    var covered = ParseAttribute(line, "hits", report) > 0;
                    var key = (file, number);
                    lines[key] = covered || lines.GetValueOrDefault(key);
                }
            }
        }

        var linesValid = lines.Count;
        if (linesValid == 0)
        {
            throw new InvalidOperationException("Coverage reports contain no executable lines.");
        }

        var linesCovered = lines.Count(line => line.Value);
        var percentage = 100d * linesCovered / linesValid;
        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Line coverage: {percentage:F2}% ({linesCovered}/{linesValid})"));

        if (percentage < threshold)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Line coverage {percentage:F2}% is below the {threshold}% gate."));
        }
    }

    private static int ParseAttribute(XElement element, string name, string report)
    {
        var value = element.Attribute(name)?.Value;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new InvalidOperationException($"Invalid {name} value in {report}.");
    }

    private static string FindRepositoryRoot(string startPath)
    {
        for (var directory = new DirectoryInfo(startPath); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "JobScheduler.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the Job Scheduler repository root.");
    }

    private static string QuoteForDisplay(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private sealed record HarnessOptions(
        string Mode,
        int CoverageThreshold,
        bool NoRestore,
        string? ResultsRoot,
        int ExpectedReports)
    {
        public static HarnessOptions Parse(string[] arguments)
        {
            var mode = arguments.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal))
                ?? "implement";
            if (mode is not ("implement" or "review" or "coverage"))
            {
                throw new ArgumentException("Mode must be 'implement', 'review', or 'coverage'.");
            }

            var threshold = DefaultCoverageThreshold;
            var thresholdIndex = Array.IndexOf(arguments, "--coverage-threshold");
            if (thresholdIndex >= 0)
            {
                if (thresholdIndex + 1 >= arguments.Length ||
                    !int.TryParse(arguments[thresholdIndex + 1], out threshold) ||
                    threshold is < 0 or > 100)
                {
                    throw new ArgumentException("Coverage threshold must be an integer from 0 to 100.");
                }
            }

            var resultsRoot = ReadOption(arguments, "--results-root");
            var expectedReportsText = ReadOption(arguments, "--expected-reports");
            var expectedReports = 0;
            if (mode == "coverage" &&
                (string.IsNullOrWhiteSpace(resultsRoot) ||
                 !int.TryParse(expectedReportsText, out expectedReports) ||
                 expectedReports < 1))
            {
                throw new ArgumentException(
                    "Coverage mode requires --results-root and a positive --expected-reports value.");
            }

            return new HarnessOptions(
                mode,
                threshold,
                arguments.Contains("--no-restore", StringComparer.Ordinal),
                resultsRoot,
                expectedReports);
        }

        private static string? ReadOption(string[] arguments, string name)
        {
            var index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
        }
    }
}
