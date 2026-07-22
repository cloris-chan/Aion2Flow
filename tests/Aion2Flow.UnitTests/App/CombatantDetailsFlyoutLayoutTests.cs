using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.ViewModels;
using Cloris.Aion2Flow.Views;

namespace Cloris.Aion2Flow.Tests.App;

[Collection(AvaloniaTestCollection.Name)]
public sealed class CombatantDetailsFlyoutLayoutTests
{
    [Fact]
    public void DetailsLayout_UsesIndependentHealingShieldAndResourceCategories()
    {
        AvaloniaTestHost.Run(() =>
        {
            AssertConstrainedViewportConfiguresVerticalScrollingForShieldSection();
            AssertHealingAndShieldSkillTablesDoNotExposeDamageHitCountColumns();
            AssertHealingAndShieldUseIndependentSummaryCards();
            AssertResourceSectionUsesNeutralManaChangeColumn();
            AssertSkillListsUseAnimatedVirtualizationAndContextualSelection();
        });
    }

    private static void AssertConstrainedViewportConfiguresVerticalScrollingForShieldSection()
    {
        var (localization, frameBatch) = CreateViewServices();
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, frameBatch)
        {
            SelectedCombatantId = 100
        };
        PopulateRows(viewModel.OutgoingDamage, 6, 11000010);
        PopulateRows(viewModel.OutgoingHealing, 1, 14000010);
        frameBatch.FlushFrame();

        var view = new CombatantDetailsFlyoutView { DataContext = viewModel };
        view.ConfigureViewport(920, 560);
        var rootCard = view.FindControl<Border>("RootCard");
        var contentScroller = view.GetLogicalDescendants()
            .OfType<ScrollViewer>()
            .Single(static scroller => scroller.Classes.Contains("DetailContentScroller"));
        var detailLayout = contentScroller.Parent as Grid;
        var outgoingDetail = view.GetLogicalDescendants().OfType<CombatDirectionDetailView>().First();
        var shieldDetail = outgoingDetail.GetLogicalDescendants().OfType<ShieldDetailView>().Single();
        var shieldSection = shieldDetail.FindControl<StackPanel>("ShieldSectionPanel");
        var shieldEmptyState = shieldDetail.FindControl<TextBlock>("ShieldEmptyState");

        Assert.NotNull(rootCard);
        Assert.NotNull(detailLayout);
        Assert.NotNull(shieldSection);
        Assert.NotNull(shieldEmptyState);
        Assert.False(viewModel.OutgoingShield.HasSkills);
        Assert.True(shieldSection.IsVisible);
        Assert.True(shieldEmptyState.IsVisible);
        Assert.Equal(560d, rootCard.Height, 1);
        Assert.Equal(560d, rootCard.MaxHeight, 1);
        Assert.Equal(0d, contentScroller.MinHeight);
        Assert.Equal(ScrollBarVisibility.Auto, contentScroller.VerticalScrollBarVisibility);
        Assert.True(detailLayout.RowDefinitions[1].Height.IsStar);
        Assert.False(outgoingDetail.EnableSkillSelection);
        Assert.All(outgoingDetail.GetLogicalDescendants().OfType<AnimatedItemsView>(), static list => Assert.False(list.IsSelectionEnabled));
    }

    private static void AssertHealingAndShieldSkillTablesDoNotExposeDamageHitCountColumns()
    {
        var (localization, frameBatch) = CreateViewServices();
        var view = new CombatDirectionDetailView
        {
            DataContext = new CombatDirectionDetailViewModel(localization, frameBatch, "Direction_Targets")
        };

        var hitCountHeaders = view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Where(textBlock =>
                textBlock.Classes.Contains("DetailTableHeader") &&
                string.Equals(textBlock.Text, localization["Metric_HitCount"], StringComparison.Ordinal))
            .ToArray();

        Assert.Single(hitCountHeaders);
    }

    private static void AssertHealingAndShieldUseIndependentSummaryCards()
    {
        var (localization, frameBatch) = CreateViewServices();
        var detail = new CombatDirectionDetailViewModel(localization, frameBatch, "Direction_Targets");
        var healingView = new HealingDetailView { DataContext = detail };
        var shieldView = new ShieldDetailView { DataContext = detail };
        var compositeView = new CombatDirectionDetailView { DataContext = detail };

        var healingCard = healingView.FindControl<Border>("HealingSummaryCard");
        var shieldCard = shieldView.FindControl<Border>("ShieldSummaryCard");
        var healingBannerMetrics = healingView.FindControl<Grid>("HealingBannerMetrics");
        var shieldBannerMetrics = shieldView.FindControl<Grid>("ShieldBannerMetrics");
        var healingBannerLabels = healingBannerMetrics!.Children
            .OfType<MetricTile>()
            .Select(static tile => tile.Label)
            .ToArray();
        var shieldBannerLabels = shieldBannerMetrics!.Children
            .OfType<MetricTile>()
            .Select(static tile => tile.Label)
            .ToArray();
        var bannerTitles = compositeView.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Where(static textBlock => textBlock.Classes.Contains("DetailBannerTitle"))
            .Select(static textBlock => textBlock.Text)
            .ToArray();
        var directHealingHeaders = healingView.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Where(textBlock =>
                textBlock.Classes.Contains("DetailTableHeader") &&
                string.Equals(textBlock.Text, localization["Metric_DirectHealing"], StringComparison.Ordinal))
            .ToArray();

        Assert.NotNull(healingCard);
        Assert.NotNull(shieldCard);
        Assert.NotSame(healingCard, shieldCard);
        Assert.Equal(3, healingBannerLabels.Length);
        Assert.Contains(localization["Metric_TotalHealing"], healingBannerLabels);
        Assert.DoesNotContain(localization["Category_Shield"], healingBannerLabels);
        Assert.Equal(2, shieldBannerLabels.Length);
        Assert.Contains(localization["Metric_Total"], shieldBannerLabels);
        Assert.Equal(4, bannerTitles.Length);
        Assert.Contains(localization["Category_Damage"], bannerTitles);
        Assert.Contains(localization["Category_Healing"], bannerTitles);
        Assert.Contains(localization["Category_Shield"], bannerTitles);
        Assert.Contains(localization["Category_Resource"], bannerTitles);
        Assert.Single(directHealingHeaders);
    }

    private static void AssertResourceSectionUsesNeutralManaChangeColumn()
    {
        var (localization, frameBatch) = CreateViewServices();
        var view = new ResourceDetailView
        {
            DataContext = new CombatDirectionDetailViewModel(localization, frameBatch, "Direction_Targets")
        };

        var resourceCard = view.FindControl<Border>("ResourceSummaryCard")!;
        var resourceBannerMetrics = view.FindControl<Grid>("ResourceBannerMetrics")!;
        var tableHeaders = resourceCard.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Where(static textBlock => textBlock.Classes.Contains("DetailTableHeader"))
            .Select(static textBlock => textBlock.Text ?? string.Empty)
            .ToArray();
        var bannerMetric = Assert.Single(resourceBannerMetrics.Children.OfType<MetricTile>());
        Assert.Equal(localization["Metric_ManaChange"], bannerMetric.Label);
        Assert.Equal(
            [
                localization["Metric_ManaChange"],
                localization["Metric_DirectEvents"],
                localization["Metric_PeriodicEvents"],
                localization["Column_Events"]
            ],
            tableHeaders);
    }

    [Fact]
    public void DetailRowSelectionStyle_PreservesMeasuredHeight()
    {
        AvaloniaTestHost.Run(() =>
        {
            var style = new StyleInclude(new Uri("avares://Aion2Flow/"))
            {
                Source = new Uri("avares://Aion2Flow/Styles/OverlayTheme.axaml")
            };
            Application.Current!.Styles.Add(style);
            var row = new Border { Child = new Border { Height = 22 } };
            row.Classes.Add("DetailTableRow");
            row.Classes.Add("SelectableDetailTableRow");
            var window = new Window
            {
                Width = 400,
                SizeToContent = SizeToContent.Height,
                Content = new StackPanel { Children = { row } }
            };

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var initialHeight = row.Bounds.Height;

                row.Classes.Add("selected");
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(initialHeight, row.Bounds.Height);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
                Application.Current.Styles.Remove(style);
            }
        });
    }

    private static void AssertSkillListsUseAnimatedVirtualizationAndContextualSelection()
    {
        var (localization, frameBatch) = CreateViewServices();
        var view = new CombatDirectionDetailView
        {
            DataContext = new CombatDirectionDetailViewModel(localization, frameBatch, "Direction_Targets")
        };

        var damageView = view.GetLogicalDescendants().OfType<DamageDetailView>().Single();
        var healingView = view.GetLogicalDescendants().OfType<HealingDetailView>().Single();
        var shieldView = view.GetLogicalDescendants().OfType<ShieldDetailView>().Single();
        var skillLists = new[]
        {
            damageView.FindControl<AnimatedItemsView>("DamageRows"),
            healingView.FindControl<AnimatedItemsView>("HealingRows"),
            shieldView.FindControl<AnimatedItemsView>("ShieldRows")
        };

        Assert.All(skillLists, static list =>
        {
            Assert.NotNull(list);
            Assert.True(list.IsSelectionEnabled);
            Assert.Equal(ScrollBarVisibility.Hidden, list.VerticalScrollBarVisibility);
        });
        var allLists = view.GetLogicalDescendants().OfType<AnimatedItemsView>().ToArray();
        Assert.Equal(4, allLists.Length);
        Assert.All(allLists, static list => Assert.Equal(ScrollBarVisibility.Hidden, list.VerticalScrollBarVisibility));
    }

    private static void PopulateRows(SkillDetailSectionViewModel section, int count, int firstSkillCode)
    {
        var rows = new List<SkillDetailRowData>(count);
        for (var i = 0; i < count; i++)
        {
            var skillCode = firstSkillCode + i;
            rows.Add(new SkillDetailRowData
            {
                BaseKey = new SkillBaseKey(new CombatEventKey(skillCode, default, default)),
                SkillCode = skillCode,
                DisplayName = $"Skill {skillCode}",
                EventCount = 1,
                TotalAmount = 100,
                DirectAmount = 100,
                Hits = 1,
                Attempts = 1
            });
        }

        section.ReplaceRows(rows);
        section.HasSkills = true;
        section.SkillCount = count;
        section.Total = count * 100L;
    }

    private static (LocalizationService Localization, UiFrameBatchService FrameBatch) CreateViewServices()
        => ViewTestServices.Get();

}
