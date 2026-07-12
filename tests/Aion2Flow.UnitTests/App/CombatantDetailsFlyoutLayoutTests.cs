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
    public void ConstrainedViewport_ConfiguresVerticalScrollingForShieldSection()
    {
        AvaloniaTestHost.EnsureInitialized();
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

    [Fact]
    public void RecoverySkillTables_DoNotExposeDamageHitCountColumns()
    {
        AvaloniaTestHost.EnsureInitialized();
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

    [Fact]
    public void SupportBanner_ShowsAggregateHealingAndTableShowsBreakdown()
    {
        AvaloniaTestHost.EnsureInitialized();
        var (localization, frameBatch) = CreateViewServices();
        var view = new CombatDirectionDetailView
        {
            DataContext = new CombatDirectionDetailViewModel(localization, frameBatch, "Direction_Targets")
        };

        var supportBannerMetrics = view.FindControl<Grid>("SupportBannerMetrics");
        var bannerLabels = supportBannerMetrics!.Children
            .OfType<MetricTile>()
            .Select(static tile => tile.Label)
            .ToArray();
        var directHealingHeaders = view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Where(textBlock =>
                textBlock.Classes.Contains("DetailTableHeader") &&
                string.Equals(textBlock.Text, localization["Metric_DirectHealing"], StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(4, bannerLabels.Length);
        Assert.Contains(localization["Metric_TotalHealing"], bannerLabels);
        Assert.DoesNotContain(localization["Metric_DirectHealing"], bannerLabels);
        Assert.DoesNotContain(localization["MetricShort_Hot"], bannerLabels);
        Assert.Single(directHealingHeaders);
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
