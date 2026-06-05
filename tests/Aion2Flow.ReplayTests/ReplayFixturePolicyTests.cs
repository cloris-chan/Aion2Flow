namespace Cloris.Aion2Flow.Tests;

public sealed class ReplayFixturePolicyTests
{
    [Fact]
    public void ReplayTests_DoNotDependOnFragmentFixtures()
    {
        var root = FindSolutionRoot();
        var replayTestsRoot = Path.Combine(root, "tests", "Aion2Flow.ReplayTests");
        var forbiddenTerms = new[]
        {
            ".he" + "x",
            "Hex" + "Helper",
            "Fixture" + "Catalog",
            "From" + "Fixture"
        };

        var violations = Directory
            .EnumerateFiles(replayTestsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !HasBuildOutputSegment(path))
            .SelectMany(path => FindViolations(path, forbiddenTerms))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Replay tests must use complete stream logs as runtime truth." +
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

    private static bool HasBuildOutputSegment(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(static segment =>
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }
}
