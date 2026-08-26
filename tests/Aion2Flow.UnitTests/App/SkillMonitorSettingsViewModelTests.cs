using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Settings;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class SkillMonitorSettingsViewModelTests
{
    [Fact]
    public void DefaultSelectionIncludesEveryRowBaseSkillForBuffsAndCooldowns()
    {
        using var fixture = new SkillMonitorSettingsFixture();

        var skills = fixture.ViewModel.Groups.SelectMany(static group => group.Skills).ToArray();

        Assert.NotEmpty(skills);
        Assert.Equal(skills.Length, skills.Select(static skill => skill.SkillId).Distinct().Count());
        Assert.All(skills, static skill =>
        {
            Assert.True(skill.IsBuffSelected);
            Assert.True(skill.IsCooldownSelected);
        });
        Assert.True(fixture.Settings.Current.SkillMonitorBuffSelectAll);
        Assert.Empty(fixture.Settings.Current.SkillMonitorBuffSkillIds);
        Assert.True(fixture.Settings.Current.SkillMonitorCooldownSelectAll);
        Assert.Empty(fixture.Settings.Current.SkillMonitorCooldownSkillIds);
    }

    [Fact]
    public void IndividualBuffAndCooldownSelectionsRemainIndependent()
    {
        using var fixture = new SkillMonitorSettingsFixture();
        var skills = fixture.ViewModel.Groups.SelectMany(static group => group.Skills).ToArray();
        var excludedBuff = skills[0];
        var excludedCooldown = skills[1];

        excludedBuff.IsBuffSelected = false;

        Assert.False(fixture.Settings.Current.SkillMonitorBuffSelectAll);
        Assert.DoesNotContain(excludedBuff.SkillId, fixture.Settings.Current.SkillMonitorBuffSkillIds);
        Assert.Equal(skills.Length - 1, fixture.Settings.Current.SkillMonitorBuffSkillIds.Count);
        Assert.True(fixture.Settings.Current.SkillMonitorCooldownSelectAll);
        Assert.True(excludedBuff.IsCooldownSelected);
        Assert.False(SkillMonitorSelection.IncludesBuff(fixture.Settings.Current, excludedBuff.SkillId));
        Assert.True(SkillMonitorSelection.IncludesBuff(fixture.Settings.Current, excludedCooldown.SkillId));
        Assert.True(SkillMonitorSelection.IncludesCooldown(fixture.Settings.Current, excludedBuff.SkillId));

        excludedCooldown.IsCooldownSelected = false;

        Assert.False(fixture.Settings.Current.SkillMonitorCooldownSelectAll);
        Assert.DoesNotContain(excludedCooldown.SkillId, fixture.Settings.Current.SkillMonitorCooldownSkillIds);
        Assert.Equal(skills.Length - 1, fixture.Settings.Current.SkillMonitorCooldownSkillIds.Count);
        Assert.True(excludedCooldown.IsBuffSelected);
        Assert.False(SkillMonitorSelection.IncludesCooldown(fixture.Settings.Current, excludedCooldown.SkillId));
        Assert.True(SkillMonitorSelection.IncludesCooldown(fixture.Settings.Current, excludedBuff.SkillId));
    }

    [Fact]
    public void BuffAndCooldownSelectAllTogglesAreIndependent()
    {
        using var fixture = new SkillMonitorSettingsFixture();
        var firstSkill = fixture.ViewModel.Groups.SelectMany(static group => group.Skills).First();
        var changedProperties = new List<string?>();
        firstSkill.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        fixture.ViewModel.ClearAllBuffsCommand.Execute(null);

        Assert.False(fixture.Settings.Current.SkillMonitorBuffSelectAll);
        Assert.Empty(fixture.Settings.Current.SkillMonitorBuffSkillIds);
        Assert.True(fixture.Settings.Current.SkillMonitorCooldownSelectAll);
        Assert.All(
            fixture.ViewModel.Groups.SelectMany(static group => group.Skills),
            static skill =>
            {
                Assert.False(skill.IsBuffSelected);
                Assert.True(skill.IsCooldownSelected);
            });
        Assert.Contains(nameof(SkillMonitorSkillOption.IsBuffSelected), changedProperties);

        fixture.ViewModel.ClearAllCooldownsCommand.Execute(null);

        Assert.False(fixture.Settings.Current.SkillMonitorCooldownSelectAll);
        Assert.Empty(fixture.Settings.Current.SkillMonitorCooldownSkillIds);
        Assert.All(
            fixture.ViewModel.Groups.SelectMany(static group => group.Skills),
            static skill => Assert.False(skill.IsCooldownSelected));
        Assert.Contains(nameof(SkillMonitorSkillOption.IsCooldownSelected), changedProperties);

        fixture.ViewModel.SelectAllBuffsCommand.Execute(null);
        fixture.ViewModel.SelectAllCooldownsCommand.Execute(null);

        Assert.True(fixture.Settings.Current.SkillMonitorBuffSelectAll);
        Assert.True(fixture.Settings.Current.SkillMonitorCooldownSelectAll);
        Assert.All(
            fixture.ViewModel.Groups.SelectMany(static group => group.Skills),
            static skill =>
            {
                Assert.True(skill.IsBuffSelected);
                Assert.True(skill.IsCooldownSelected);
            });
    }

    private sealed class SkillMonitorSettingsFixture : IDisposable
    {
        private readonly string _settingsPath = Path.Combine(
            Path.GetTempPath(),
            "Aion2Flow.Tests",
            $"skill-monitor-{Guid.NewGuid():N}.json");
        private readonly LocalizationService _localization;
        private readonly GameResourceService _resources;

        public SkillMonitorSettingsFixture()
        {
            var language = new LanguageService();
            language.SetLanguage(LanguageService.English);
            _localization = new LocalizationService(language);
            _resources = new GameResourceService(language);
            Settings = new SettingsService(_settingsPath);
            ViewModel = new SkillMonitorSettingsViewModel(_resources, Settings, _localization);
        }

        public SettingsService Settings { get; }

        public SkillMonitorSettingsViewModel ViewModel { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            _resources.Dispose();
            _localization.Dispose();
            File.Delete(_settingsPath);
        }
    }
}
