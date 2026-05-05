using Cloris.Aion2Flow.Scene.Journal;

namespace Cloris.Aion2Flow.Scene.Model;

public sealed class SceneSession
{
    public Guid SceneSessionId { get; init; }

    public DateTimeOffset Started { get; init; }

    public int MapId { get; set; }

    public int MapInstanceId { get; set; }

    public long StartOrdinal { get; init; }

    public long EndOrdinal { get; set; }

    public Revision Revision { get; set; }

    public ObservedEventJournal Journal { get; } = new();

    public DateTimeOffset ToDisplayTime(long offsetTicks) => Started.AddTicks(offsetTicks);
}
