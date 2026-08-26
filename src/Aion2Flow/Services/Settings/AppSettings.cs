using System.Drawing;
using Cloris.Aion2Flow.Presentation;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Services.Settings;

public sealed class AppSettings
{
    public TopmostMode TopmostMode { get; set; } = TopmostMode.GameForeground;

    public int MaxVisibleCombatantRows { get; set => field = Math.Clamp(value, 5, 10); } = 5;

    public CombatantSortMetric CombatantSortMetric { get; set; } = CombatantSortMetric.DamagePerSecond;

    public CombatantStatisticsScope CombatantStatisticsScope { get; set; } = CombatantStatisticsScope.All;

    public SceneKind SceneKind { get; set; } = SceneKind.Standard;

    public bool UseCompactMainMetrics { get; set; } = true;

    public bool ShowDamagePerSecondColumn { get; set; } = true;

    public bool ShowDamageColumn { get; set; } = true;

    public bool ShowTotalDamagePerSecond { get; set; } = true;

    public EncounterTimeDisplayFormat EncounterTimeDisplayFormat
    {
        get;
        set => field = Enum.IsDefined(value)
            ? value
            : EncounterTimeDisplayFormat.DecimalSeconds;
    } = EncounterTimeDisplayFormat.DecimalSeconds;

    public bool ShowFocusStatusBar { get; set; } = true;

    public bool HideHeaderWhenClickThrough { get; set; }

    public bool ShowPlayerNames { get; set; } = true;

    public PlayerSelfMarkerDisplayMode PlayerSelfMarkerDisplayMode { get; set; } = PlayerSelfMarkerDisplayMode.WhenNamesHidden;

    public bool ShowPlayerShortServerName { get; set; }

    public bool ShowPlayerLegionName { get; set; }

    public bool TintPlayerNamesByFaction { get; set; } = true;

    public bool SkillMonitorEnabled { get; set; } = true;

    public bool SkillMonitorBuffSelectAll { get; set; } = true;

    public List<int> SkillMonitorBuffSkillIds { get; set => field = value ?? []; } = [];

    public bool SkillMonitorCooldownSelectAll { get; set; } = true;

    public List<int> SkillMonitorCooldownSkillIds { get; set => field = value ?? []; } = [];

    public int SkillMonitorScalePercent { get; set => field = Math.Clamp(value, 50, 200); } = 100;

    public Point? SkillMonitorPosition { get; set; }

    public int SkillMonitorWidth { get; set => field = Math.Clamp(value, 100, 4_096); } = 680;

    public int UiScalePercent { get; set => field = Math.Clamp(value, 50, 200); } = 100;

    public string? Language { get; set; }

    public uint? BattleResetHotkeyModifiers { get; set; }

    public uint? BattleResetHotkeyVirtualKey { get; set; }

    public uint? OverlayInteractionHotkeyModifiers { get; set; }

    public uint? OverlayInteractionHotkeyVirtualKey { get; set; }

    public Point? MainWindowPosition { get; set; }
}
