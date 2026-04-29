using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Services.Settings;

public sealed class AppSettings
{
    public TopmostMode TopmostMode { get; set; } = TopmostMode.GameForeground;

    public int MaxVisibleCombatantRows { get; set; } = 4;

    public string? Language { get; set; }
}
