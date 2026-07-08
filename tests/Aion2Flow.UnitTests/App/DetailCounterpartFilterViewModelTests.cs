using System.Collections.Specialized;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class DetailCounterpartFilterViewModelTests
{
    [Fact]
    public void ReplaceCounterparts_StableOrder_UpdatesInPlaceWithoutCollectionNotification()
    {
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var filter = new DetailCounterpartFilterViewModel(localization, "Direction_Targets");

        filter.ReplaceCounterparts(
        [
            CreateOption(1001, 100),
            CreateOption(1002, 200)
        ]);

        var first = filter.Counterparts[0];
        var second = filter.Counterparts[1];
        second.IsSelected = false;
        var collectionChangeCount = 0;
        filter.Counterparts.CollectionChanged += (_, _) => collectionChangeCount++;

        filter.ReplaceCounterparts(
        [
            CreateOption(1001, 300),
            CreateOption(1002, 400)
        ]);

        Assert.Equal(0, collectionChangeCount);
        Assert.Same(first, filter.Counterparts[0]);
        Assert.Same(second, filter.Counterparts[1]);
        Assert.Equal(300, filter.Counterparts[0].DamageAmount);
        Assert.Equal(400, filter.Counterparts[1].DamageAmount);
        Assert.False(filter.Counterparts[1].IsSelected);
    }

    [Fact]
    public void ReplaceCounterparts_LargeStructuralChanges_ResetOnce()
    {
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var filter = new DetailCounterpartFilterViewModel(localization, "Direction_Targets");
        filter.Counterparts.ResetThreshold = 4;

        filter.ReplaceCounterparts(CreateOptions(1001, 6));

        var actions = new List<NotifyCollectionChangedAction>();
        filter.Counterparts.CollectionChanged += (_, e) => actions.Add(e.Action);

        filter.ReplaceCounterparts(CreateOptions(2001, 6));

        Assert.Equal([NotifyCollectionChangedAction.Reset], actions);
        Assert.Equal(6, filter.Counterparts.Count);
        Assert.Equal(2001, filter.Counterparts[0].CombatantId);
    }

    private static List<DetailCounterpartOption> CreateOptions(int firstCombatantId, int count)
    {
        var options = new List<DetailCounterpartOption>(count);
        for (var i = 0; i < count; i++)
        {
            options.Add(CreateOption(firstCombatantId + i, 100 + i));
        }

        return options;
    }

    private static DetailCounterpartOption CreateOption(int combatantId, long damage)
        => new(combatantId, damage, damage / 1000d, damage / 2, damage / 2000d, damage / 4, damage / 4000d);
}
