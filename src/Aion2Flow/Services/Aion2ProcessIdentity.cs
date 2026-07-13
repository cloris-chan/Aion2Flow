namespace Cloris.Aion2Flow.Services;

internal static class Aion2ProcessIdentity
{
    private const string ProcessName = "Aion2";
    private const string ExecutableName = "Aion2.exe";

    public static bool MatchesExecutableName(ReadOnlySpan<char> processName) => processName.Equals(ProcessName, StringComparison.OrdinalIgnoreCase) || processName.Equals(ExecutableName, StringComparison.OrdinalIgnoreCase);
}
