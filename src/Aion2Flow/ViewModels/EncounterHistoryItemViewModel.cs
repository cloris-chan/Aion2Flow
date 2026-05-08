using Cloris.Aion2Flow.SceneRuntime.Archive;

namespace Cloris.Aion2Flow.ViewModels;

public sealed record EncounterHistoryItemViewModel(ArchivedEncounterRecord Record, string DisplayName);
