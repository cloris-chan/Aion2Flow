namespace Cloris.Aion2Flow.Tests;

public sealed class ReplayFixturePolicyTests
{
    [Fact]
    public void RuntimeParsingTests_UseCurrentCompleteReplayFixtures()
    {
        var root = FindSolutionRoot();
        var testsRoot = Path.Combine(root, "tests");
        var replayFixturesRoot = Path.Combine(root, "tests", "Aion2Flow.ReplayTests", "Fixtures");
        var forbiddenTerms = new[]
        {
            ".he" + "x",
            "Hex" + "Helper",
            "Fixture" + "Catalog",
            "From" + "Fixture",
            "Convert." + "FromHexString"
        };

        var sourceViolations = Directory
            .EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !HasBuildOutputSegment(path))
            .Where(static path => !string.Equals(Path.GetFileName(path), "ReplayFixturePolicyTests.cs", StringComparison.Ordinal))
            .SelectMany(path => FindViolations(path, forbiddenTerms))
            .ToArray();
        var fixtureViolations = Directory
            .EnumerateFiles(replayFixturesRoot, "*", SearchOption.AllDirectories)
            .Where(static path => !HasBuildOutputSegment(path))
            .Where(path => !IsCurrentStreamLogFixture(replayFixturesRoot, path))
            .Select(path => $"{Path.GetRelativePath(root, path)} is not a 20260701+ complete stream log fixture.")
            .ToArray();
        var violations = sourceViolations.Concat(fixtureViolations).ToArray();

        Assert.True(
            violations.Length == 0,
            "Runtime parsing tests must use 20260701+ complete stream logs as protocol truth." +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> FindViolations(string path, IReadOnlyList<string> forbiddenTerms)
    {
        var text = File.ReadAllText(path);
        foreach (var term in forbiddenTerms)
        {
            if (text.Contains(term, StringComparison.Ordinal))
            {
                yield return $"{Path.GetRelativePath(FindSolutionRoot(), path)} contains forbidden term '{term}'.";
            }
        }
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aion2Flow.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Aion2Flow.slnx.");
    }

    private static bool IsCurrentStreamLogFixture(string fixtureRoot, string path)
    {
        var relative = Path.GetRelativePath(fixtureRoot, path);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Length != 2 || !string.Equals(segments[0], "logs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        const string Prefix = "aion2flow.stream.";
        const string Suffix = ".log";
        var fileName = segments[1];
        if (!fileName.StartsWith(Prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(Suffix, StringComparison.Ordinal) ||
            fileName.Length != Prefix.Length + 14 + Suffix.Length)
        {
            return false;
        }

        var timestamp = fileName.Substring(Prefix.Length, 14);
        return timestamp.All(static c => c >= '0' && c <= '9') &&
               string.CompareOrdinal(timestamp, "20260701000000") >= 0;
    }

    private static bool HasBuildOutputSegment(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(static segment =>
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }
}
