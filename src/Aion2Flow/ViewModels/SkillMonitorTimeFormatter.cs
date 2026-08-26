using System.Globalization;
using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.ViewModels;

internal static class SkillMonitorTimeFormatter
{
    public static string Format(long milliseconds, EncounterTimeDisplayFormat displayFormat)
    {
        var normalizedMilliseconds = Math.Max(0, milliseconds);
        return displayFormat switch
        {
            EncounterTimeDisplayFormat.DecimalSeconds => string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.0}s",
                normalizedMilliseconds / 1_000d),
            EncounterTimeDisplayFormat.MinutesSeconds => FormatMinutesSeconds(normalizedMilliseconds),
            _ => throw new ArgumentOutOfRangeException(nameof(displayFormat), displayFormat, null)
        };
    }

    private static string FormatMinutesSeconds(long milliseconds)
    {
        var totalSeconds = milliseconds / 1_000;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:00}:{1:00}",
            totalSeconds / 60,
            totalSeconds % 60);
    }
}
