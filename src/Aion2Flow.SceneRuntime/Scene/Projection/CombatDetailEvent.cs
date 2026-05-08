using Cloris.Aion2Flow.Combat.Metrics;

namespace Cloris.Aion2Flow.Scene.Projection;

public readonly record struct CombatDetailEvent(ParsedCombatPacket Packet, int SourceId, int TargetId, long Revision = 0);
