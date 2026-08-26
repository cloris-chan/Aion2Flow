namespace Cloris.Aion2Flow.Services.Settings;

internal static class SkillMonitorSelection
{
    public static bool IncludesBuff(AppSettings settings, int rowBaseSkillId)
        => settings.SkillMonitorEnabled &&
           rowBaseSkillId > 0 &&
           (settings.SkillMonitorBuffSelectAll || settings.SkillMonitorBuffSkillIds.Contains(rowBaseSkillId));

    public static bool IncludesCooldown(AppSettings settings, int rowBaseSkillId)
        => settings.SkillMonitorEnabled &&
           rowBaseSkillId > 0 &&
           (settings.SkillMonitorCooldownSelectAll || settings.SkillMonitorCooldownSkillIds.Contains(rowBaseSkillId));
}
