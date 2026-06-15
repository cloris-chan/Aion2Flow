using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public static class BossModeFocusTargets
{
    public static bool IsFocusTarget(NpcKind kind) => kind is NpcKind.Boss or NpcKind.TrainingDummy;
}
