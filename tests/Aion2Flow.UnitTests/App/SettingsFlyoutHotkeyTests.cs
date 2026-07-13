using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Hotkeys;
using Cloris.Aion2Flow.Services.Settings;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class SettingsFlyoutHotkeyTests
{
    [Fact]
    public void SettingsService_PersistsBothGlobalHotkeys()
    {
        using var settingsFile = new TemporarySettingsFile();
        var settings = new SettingsService(settingsFile.Path);

        Assert.Null(settings.Current.OverlayInteractionHotkeyModifiers);
        Assert.Null(settings.Current.OverlayInteractionHotkeyVirtualKey);

        settings.Update(static value =>
        {
            value.BattleResetHotkeyModifiers = (uint)(HotkeyModifiers.Control | HotkeyModifiers.Shift);
            value.BattleResetHotkeyVirtualKey = 0x52;
            value.OverlayInteractionHotkeyModifiers = (uint)(HotkeyModifiers.Alt | HotkeyModifiers.Win);
            value.OverlayInteractionHotkeyVirtualKey = 0x49;
        });

        var loaded = new SettingsService(settingsFile.Path).Current;

        Assert.Equal((uint)(HotkeyModifiers.Control | HotkeyModifiers.Shift), loaded.BattleResetHotkeyModifiers);
        Assert.Equal(0x52u, loaded.BattleResetHotkeyVirtualKey);
        Assert.Equal((uint)(HotkeyModifiers.Alt | HotkeyModifiers.Win), loaded.OverlayInteractionHotkeyModifiers);
        Assert.Equal(0x49u, loaded.OverlayInteractionHotkeyVirtualKey);
    }

    [Fact]
    public void PersistedHotkeys_MustSatisfyTheInteractiveValidationContract()
    {
        using var fixture = new SettingsViewModelFixture(settings =>
        {
            settings.BattleResetHotkeyModifiers = (uint)HotkeyModifiers.None;
            settings.BattleResetHotkeyVirtualKey = 0x52;
            settings.OverlayInteractionHotkeyModifiers = (uint)HotkeyModifiers.Control;
            settings.OverlayInteractionHotkeyVirtualKey = 0x01;
        });

        Assert.Null(fixture.ViewModel.BattleResetHotkey);
        Assert.Null(fixture.ViewModel.OverlayInteractionHotkey);
        Assert.True(fixture.HotkeyService.AttachWindow(0x1234));
        Assert.Empty(fixture.HotkeyPlatform.ActiveRegistrations);
    }

    [Fact]
    public void BeginCaptureHotkey_SwitchesTarget_AndRejectsAStaleTarget()
    {
        using var fixture = new SettingsViewModelFixture();
        var viewModel = fixture.ViewModel;
        var definition = new HotkeyDefinition(HotkeyModifiers.Control, 0x49);

        viewModel.BeginCaptureHotkey(GlobalHotkeyAction.BattleReset);

        Assert.Equal(GlobalHotkeyAction.BattleReset, viewModel.CapturingHotkeyAction);
        Assert.True(viewModel.IsCapturingResetHotkey);

        viewModel.BeginCaptureHotkey(GlobalHotkeyAction.CycleOverlayInteraction);

        Assert.Equal(GlobalHotkeyAction.CycleOverlayInteraction, viewModel.CapturingHotkeyAction);
        Assert.False(viewModel.IsCapturingResetHotkey);
        Assert.True(viewModel.IsCapturingOverlayInteractionHotkey);
        Assert.False(viewModel.ApplyCapturedHotkey(GlobalHotkeyAction.BattleReset, definition));
        Assert.Equal(GlobalHotkeyAction.CycleOverlayInteraction, viewModel.CapturingHotkeyAction);
        Assert.Null(viewModel.BattleResetHotkey);

        Assert.True(viewModel.ApplyCapturedHotkey(GlobalHotkeyAction.CycleOverlayInteraction, definition));
        Assert.Null(viewModel.CapturingHotkeyAction);
        Assert.Equal(definition, viewModel.OverlayInteractionHotkey);
    }

    [Theory]
    [InlineData(GlobalHotkeyAction.BattleReset, GlobalHotkeyAction.CycleOverlayInteraction)]
    [InlineData(GlobalHotkeyAction.CycleOverlayInteraction, GlobalHotkeyAction.BattleReset)]
    public void ApplyCapturedHotkey_WhenCombinationConflicts_LatestActionWins(GlobalHotkeyAction firstAction, GlobalHotkeyAction latestAction)
    {
        using var fixture = new SettingsViewModelFixture();
        var viewModel = fixture.ViewModel;
        var definition = new HotkeyDefinition(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x48);

        viewModel.BeginCaptureHotkey(firstAction);
        Assert.True(viewModel.ApplyCapturedHotkey(firstAction, definition));
        viewModel.BeginCaptureHotkey(latestAction);
        Assert.True(viewModel.ApplyCapturedHotkey(latestAction, definition));

        Assert.Null(GetHotkey(viewModel, firstAction));
        Assert.Equal(definition, GetHotkey(viewModel, latestAction));

        var persisted = new SettingsService(fixture.SettingsPath).Current;
        Assert.Null(GetPersistedHotkey(persisted, firstAction));
        Assert.Equal(definition, GetPersistedHotkey(persisted, latestAction));
    }

    [Fact]
    public void ApplyCapturedHotkey_WhenConflictingNativeRegistrationFails_DoesNotPublishOrPersistRejectedBindings()
    {
        using var fixture = new SettingsViewModelFixture();
        var viewModel = fixture.ViewModel;
        var reset = new HotkeyDefinition(HotkeyModifiers.Control, 0x52);
        var interaction = new HotkeyDefinition(HotkeyModifiers.Alt, 0x49);
        nint hwnd = 0x1234;
        fixture.HotkeyService.AttachWindow(hwnd);
        viewModel.BeginCaptureHotkey(GlobalHotkeyAction.BattleReset);
        Assert.True(viewModel.ApplyCapturedHotkey(GlobalHotkeyAction.BattleReset, reset));
        viewModel.BeginCaptureHotkey(GlobalHotkeyAction.CycleOverlayInteraction);
        Assert.True(viewModel.ApplyCapturedHotkey(GlobalHotkeyAction.CycleOverlayInteraction, interaction));
        fixture.HotkeyPlatform.FailNextRegistration = true;

        viewModel.BeginCaptureHotkey(GlobalHotkeyAction.CycleOverlayInteraction);
        Assert.False(viewModel.ApplyCapturedHotkey(GlobalHotkeyAction.CycleOverlayInteraction, reset));

        Assert.Null(viewModel.CapturingHotkeyAction);
        Assert.Equal(reset, viewModel.BattleResetHotkey);
        Assert.Equal(interaction, viewModel.OverlayInteractionHotkey);
        Assert.True(viewModel.HasHotkeyRegistrationError);
        Assert.Equal(reset.Display, viewModel.ResetHotkeyDisplay);
        Assert.Equal(interaction.Display, viewModel.OverlayInteractionHotkeyDisplay);
        Assert.Equal(reset, fixture.HotkeyPlatform.ActiveRegistrations[(hwnd, GlobalHotkeyService.BattleResetHotkeyId)]);
        Assert.Equal(interaction, fixture.HotkeyPlatform.ActiveRegistrations[(hwnd, GlobalHotkeyService.CycleOverlayInteractionHotkeyId)]);

        var persisted = new SettingsService(fixture.SettingsPath).Current;
        Assert.Equal(reset, GetPersistedHotkey(persisted, GlobalHotkeyAction.BattleReset));
        Assert.Equal(interaction, GetPersistedHotkey(persisted, GlobalHotkeyAction.CycleOverlayInteraction));
    }

    [Fact]
    public void RefreshHotkeyRegistrationState_SurfacesStartupRegistrationFailure()
    {
        using var fixture = new SettingsViewModelFixture();
        var viewModel = fixture.ViewModel;
        var definition = new HotkeyDefinition(HotkeyModifiers.Control, 0x52);
        viewModel.BeginCaptureHotkey(GlobalHotkeyAction.BattleReset);
        Assert.True(viewModel.ApplyCapturedHotkey(GlobalHotkeyAction.BattleReset, definition));
        fixture.HotkeyPlatform.FailNextRegistration = true;

        var attached = fixture.HotkeyService.AttachWindow(0x1234);
        viewModel.RefreshHotkeyRegistrationState(fixture.HotkeyService.IsAttachedTo(0x1234));

        Assert.False(attached);
        Assert.True(viewModel.HasHotkeyRegistrationError);
        Assert.Equal(definition, viewModel.BattleResetHotkey);
    }

    [Fact]
    public void ClearHotkey_ClearsOnlyTheSelectedAction()
    {
        using var fixture = new SettingsViewModelFixture();
        var viewModel = fixture.ViewModel;
        var reset = new HotkeyDefinition(HotkeyModifiers.Control, 0x52);
        var interaction = new HotkeyDefinition(HotkeyModifiers.Alt, 0x49);
        viewModel.BeginCaptureHotkey(GlobalHotkeyAction.BattleReset);
        viewModel.ApplyCapturedHotkey(GlobalHotkeyAction.BattleReset, reset);
        viewModel.BeginCaptureHotkey(GlobalHotkeyAction.CycleOverlayInteraction);
        viewModel.ApplyCapturedHotkey(GlobalHotkeyAction.CycleOverlayInteraction, interaction);

        viewModel.ClearHotkey(GlobalHotkeyAction.CycleOverlayInteraction);

        Assert.Equal(reset, viewModel.BattleResetHotkey);
        Assert.Null(viewModel.OverlayInteractionHotkey);
        Assert.True(viewModel.HasResetHotkey);
        Assert.False(viewModel.HasOverlayInteractionHotkey);
    }

    private static HotkeyDefinition? GetHotkey(SettingsFlyoutViewModel viewModel, GlobalHotkeyAction action) => action switch
    {
        GlobalHotkeyAction.BattleReset => viewModel.BattleResetHotkey,
        GlobalHotkeyAction.CycleOverlayInteraction => viewModel.OverlayInteractionHotkey,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    private static HotkeyDefinition? GetPersistedHotkey(AppSettings settings, GlobalHotkeyAction action)
    {
        var (modifiers, virtualKey) = action switch
        {
            GlobalHotkeyAction.BattleReset => (settings.BattleResetHotkeyModifiers, settings.BattleResetHotkeyVirtualKey),
            GlobalHotkeyAction.CycleOverlayInteraction => (settings.OverlayInteractionHotkeyModifiers, settings.OverlayInteractionHotkeyVirtualKey),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        return modifiers is { } mods && virtualKey is { } vk
            ? new HotkeyDefinition((HotkeyModifiers)mods, vk)
            : null;
    }

    private sealed class SettingsViewModelFixture : IDisposable
    {
        private readonly TemporarySettingsFile _settingsFile = new();
        private readonly LocalizationService _localization;
        private readonly PlayerNameDisplayService _playerNameDisplay;
        private readonly UiScaleService _uiScale;
        private readonly ProcessPortDiscoveryService _processPortDiscovery;
        private readonly ProcessForegroundWatcher _processForegroundWatcher;

        public SettingsViewModelFixture(Action<AppSettings>? configureSettings = null)
        {
            var language = new LanguageService();
            _localization = new LocalizationService(language);
            var settings = new SettingsService(_settingsFile.Path);
            if (configureSettings is not null)
            {
                settings.Update(configureSettings);
            }
            _playerNameDisplay = new PlayerNameDisplayService(settings, _localization);
            _uiScale = new UiScaleService(settings);
            _processPortDiscovery = new ProcessPortDiscoveryService();
            _processForegroundWatcher = new ProcessForegroundWatcher(_processPortDiscovery);
            HotkeyPlatform = new TestGlobalHotkeyPlatform();
            HotkeyService = new GlobalHotkeyService(HotkeyPlatform);
            ViewModel = new SettingsFlyoutViewModel(
                _localization,
                language,
                settings,
                _playerNameDisplay,
                _uiScale,
                new AppUpdateService(),
                _processForegroundWatcher,
                HotkeyService);
        }

        public string SettingsPath => _settingsFile.Path;

        public TestGlobalHotkeyPlatform HotkeyPlatform { get; }

        public GlobalHotkeyService HotkeyService { get; }

        public SettingsFlyoutViewModel ViewModel { get; }

        public void Dispose()
        {
            HotkeyService.DetachWindow();
            _processForegroundWatcher.Dispose();
            _processPortDiscovery.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _uiScale.Dispose();
            _playerNameDisplay.Dispose();
            _localization.Dispose();
            _settingsFile.Dispose();
        }
    }

    private sealed class TestGlobalHotkeyPlatform : IGlobalHotkeyPlatform
    {
        public Dictionary<(nint Hwnd, int Id), HotkeyDefinition> ActiveRegistrations { get; } = [];

        public bool FailNextRegistration { get; set; }

        public bool Register(nint hwnd, int id, HotkeyDefinition definition)
        {
            if (FailNextRegistration)
            {
                FailNextRegistration = false;
                return false;
            }

            ActiveRegistrations[(hwnd, id)] = definition;
            return true;
        }

        public bool Unregister(nint hwnd, int id) => ActiveRegistrations.Remove((hwnd, id));
    }

    private sealed class TemporarySettingsFile : IDisposable
    {
        public TemporarySettingsFile()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Aion2Flow.Tests", $"{Guid.NewGuid():N}.json");
        }

        public string Path { get; }

        public void Dispose()
        {
            File.Delete(Path);
            File.Delete(Path + ".tmp");
        }
    }
}
