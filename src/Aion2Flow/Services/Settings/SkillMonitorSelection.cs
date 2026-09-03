namespace Cloris.Aion2Flow.Services.Settings;

internal static class SkillMonitorSelection
{
    public static bool IncludesBuff(AppSettings settings, GameResourceService resources, int rowBaseSkillId)
        => settings.SkillMonitorEnabled &&
           rowBaseSkillId > 0 &&
           resources.IsPlayerProfessionSkill(rowBaseSkillId) &&
           (settings.SkillMonitorBuffSelectAll || settings.SkillMonitorBuffSkillIds.Contains(rowBaseSkillId));

    public static bool IncludesCooldown(AppSettings settings, GameResourceService resources, int rowBaseSkillId)
        => settings.SkillMonitorEnabled &&
           rowBaseSkillId > 0 &&
           resources.IsPlayerProfessionSkill(rowBaseSkillId) &&
           (settings.SkillMonitorCooldownSelectAll || settings.SkillMonitorCooldownSkillIds.Contains(rowBaseSkillId));
}
