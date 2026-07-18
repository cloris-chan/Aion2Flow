using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.ViewModels;
using Cloris.Aion2Flow.Views;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Cloris.Aion2Flow.Tests.App;

[Collection(AvaloniaTestCollection.Name)]
public sealed class CombatantDetailsFlyoutLayoutTests
{
    private static readonly Lock AvaloniaGate = new();
    private static LocalizationService? s_localization;
    private static UiFrameBatchService? s_frameBatch;

    [Fact]
    public void DetailsLayout_UsesIndependentHealingShieldAndResourceCategories()
    {
        AvaloniaTestHost.Run(() =>
        {
            AssertConstrainedViewportConfiguresVerticalScrollingForShieldSection();
            AssertHealingAndShieldSkillTablesDoNotExposeDamageHitCountColumns();
            AssertHealingAndShieldUseIndependentSummaryCards();
            AssertResourceSectionUsesNeutralManaChangeColumn();
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
        var shieldSection = outgoingDetail.FindControl<StackPanel>("ShieldSectionPanel");
        var shieldEmptyState = outgoingDetail.FindControl<TextBlock>("ShieldEmptyState");

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
        var view = new CombatDirectionDetailView
        {
            DataContext = new CombatDirectionDetailViewModel(localization, frameBatch, "Direction_Targets")
        };

        var healingCard = view.FindControl<Border>("HealingSummaryCard");
        var shieldCard = view.FindControl<Border>("ShieldSummaryCard");
        var healingBannerMetrics = view.FindControl<Grid>("HealingBannerMetrics");
        var shieldBannerMetrics = view.FindControl<Grid>("ShieldBannerMetrics");
        var healingBannerLabels = healingBannerMetrics!.Children
            .OfType<MetricTile>()
            .Select(static tile => tile.Label)
            .ToArray();
        var shieldBannerLabels = shieldBannerMetrics!.Children
            .OfType<MetricTile>()
            .Select(static tile => tile.Label)
            .ToArray();
        var bannerTitles = view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Where(static textBlock => textBlock.Classes.Contains("DetailBannerTitle"))
            .Select(static textBlock => textBlock.Text)
            .ToArray();
        var directHealingHeaders = view.GetLogicalDescendants()
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
        var view = new CombatDirectionDetailView
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
    {
        lock (AvaloniaGate)
        {
            if (s_localization is null)
            {
                var language = new LanguageService();
                language.SetLanguage(LanguageService.TraditionalChinese);
                s_frameBatch = new UiFrameBatchService();
                s_localization = new LocalizationService(language);
                Ioc.Default.ConfigureServices(new ServiceCollection()
                    .AddSingleton(s_localization)
                    .BuildServiceProvider());
            }

            return (s_localization, s_frameBatch!);
        }
    }

}
