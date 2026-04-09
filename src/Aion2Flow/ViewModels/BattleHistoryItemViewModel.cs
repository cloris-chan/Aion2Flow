using Cloris.Aion2Flow.Battle.Archive;

namespace Cloris.Aion2Flow.ViewModels;

public sealed record BattleHistoryItemViewModel(ArchivedBattleRecord Record, string DisplayName);
