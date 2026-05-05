namespace Cloris.Aion2Flow.Scene.Model;

public readonly record struct Revision(long Global, long Journal, long Entity, long Combat, long Resource, long Aura, long Spatial, long Action, long State, long Archive);
