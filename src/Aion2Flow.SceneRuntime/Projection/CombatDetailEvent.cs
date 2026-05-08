using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public readonly record struct CombatDetailEvent(ParsedCombatPacket Packet, int SourceId, int TargetId, long Revision = 0);
