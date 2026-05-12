using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.Services;

namespace Cloris.Aion2Flow.ViewModels;

public sealed record EncounterHistoryItemViewModel(ArchivedEncounterRecord Record, SceneDisplayContext DisplayContext, uint MapId, string ArchivedAtText);
