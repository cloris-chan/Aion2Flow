using Avalonia.Threading;
using Cloris.Aion2Flow.Services.Hotkeys;

namespace Cloris.Aion2Flow.Tests.Services;

[Collection(AvaloniaTestCollection.Name)]
public sealed class GlobalHotkeyServiceTests
{
    [Fact]
    public void NativeModifiers_AlwaysDisableKeyRepeat()
    {
        const HotkeyModifiers modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift;

        var nativeModifiers = (uint)Win32GlobalHotkeyPlatform.ToNativeModifiers(modifiers);

        Assert.Equal((uint)modifiers | 0x4000u, nativeModifiers);
    }

    [Fact]
    public void NamedHotkeys_RegisterIndependently_AndDetachUnregistersAll()
    {
        var platform = new RecordingGlobalHotkeyPlatform();
        var service = new GlobalHotkeyService(platform);
        var reset = new HotkeyDefinition(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x52);
        var interaction = new HotkeyDefinition(HotkeyModifiers.Alt, 0x49);
        nint hwnd = 0x1234;

        Assert.True(service.TrySetHotkey(GlobalHotkeyAction.BattleReset, reset));
        Assert.True(service.TrySetHotkey(GlobalHotkeyAction.CycleOverlayInteraction, interaction));

        Assert.Empty(platform.Registrations);

        service.AttachWindow(hwnd);

        Assert.Collection(
            platform.Registrations,
            registration => Assert.Equal((hwnd, GlobalHotkeyService.BattleResetHotkeyId, reset), registration),
            registration => Assert.Equal((hwnd, GlobalHotkeyService.CycleOverlayInteractionHotkeyId, interaction), registration));

        platform.Registrations.Clear();
        platform.Unregistrations.Clear();
        var updatedReset = new HotkeyDefinition(HotkeyModifiers.Win, 0x42);

        Assert.True(service.TrySetHotkey(GlobalHotkeyAction.BattleReset, updatedReset));

        Assert.Equal((hwnd, GlobalHotkeyService.BattleResetHotkeyId), Assert.Single(platform.Unregistrations));
        Assert.Equal((hwnd, GlobalHotkeyService.BattleResetHotkeyId, updatedReset), Assert.Single(platform.Registrations));

        platform.Unregistrations.Clear();
        service.DetachWindow();

        Assert.Equal(
            [
                (hwnd, GlobalHotkeyService.BattleResetHotkeyId),
                (hwnd, GlobalHotkeyService.CycleOverlayInteractionHotkeyId)
            ],
            platform.Unregistrations);
    }

    [Theory]
    [InlineData(HotkeyModifiers.None, 0x52u)]
    [InlineData((HotkeyModifiers)0x10, 0x52u)]
    [InlineData(HotkeyModifiers.Control, 0x01u)]
    public void TrySetHotkey_RejectsInvalidCombinations(HotkeyModifiers modifiers, uint virtualKey)
    {
        var platform = new RecordingGlobalHotkeyPlatform();
        var service = new GlobalHotkeyService(platform);

        Assert.False(service.TrySetHotkey(GlobalHotkeyAction.BattleReset, new HotkeyDefinition(modifiers, virtualKey)));
        Assert.True(service.AttachWindow(0x1234));
        Assert.Empty(platform.Registrations);
        Assert.False(service.IsRegistered(GlobalHotkeyAction.BattleReset));
    }

    [Fact]
    public void AttachWindow_ReportsConfiguredRegistrationFailure()
    {
        var platform = new RecordingGlobalHotkeyPlatform();
        var service = new GlobalHotkeyService(platform);
        Assert.True(service.TrySetHotkey(
            GlobalHotkeyAction.BattleReset,
            new HotkeyDefinition(HotkeyModifiers.Control, 0x52)));
        platform.FailNextRegistration = true;

        Assert.False(service.AttachWindow(0x1234));
        Assert.False(service.IsRegistered(GlobalHotkeyAction.BattleReset));
    }

    [Fact]
    public void DetachWindow_WhenUnregisterFails_RetainsOwnershipUntilAReleaseSucceeds()
    {
        var platform = new RecordingGlobalHotkeyPlatform();
        var service = new GlobalHotkeyService(platform);
        var definition = new HotkeyDefinition(HotkeyModifiers.Control, 0x52);
        nint firstHwnd = 0x1234;
        nint secondHwnd = 0x5678;
        Assert.True(service.TrySetHotkey(GlobalHotkeyAction.BattleReset, definition));
        Assert.True(service.AttachWindow(firstHwnd));
        platform.FailNextUnregistration = true;

        Assert.False(service.DetachWindow());
        Assert.True(service.IsRegistered(GlobalHotkeyAction.BattleReset));
        Assert.Equal(definition, platform.ActiveRegistrations[(firstHwnd, GlobalHotkeyService.BattleResetHotkeyId)]);

        Assert.True(service.AttachWindow(secondHwnd));
        Assert.False(platform.ActiveRegistrations.ContainsKey((firstHwnd, GlobalHotkeyService.BattleResetHotkeyId)));
        Assert.Equal(definition, platform.ActiveRegistrations[(secondHwnd, GlobalHotkeyService.BattleResetHotkeyId)]);
    }

    [Fact]
    public void TrySetHotkey_WhenReplacementRegistrationFails_RestoresPreviousDefinitionAndRegistration()
    {
        var platform = new RecordingGlobalHotkeyPlatform();
        var service = new GlobalHotkeyService(platform);
        var previous = new HotkeyDefinition(HotkeyModifiers.Control, 0x52);
        var rejected = new HotkeyDefinition(HotkeyModifiers.Alt, 0x42);
        nint firstHwnd = 0x1234;
        nint secondHwnd = 0x5678;

        Assert.True(service.TrySetHotkey(GlobalHotkeyAction.BattleReset, previous));
        service.AttachWindow(firstHwnd);
        platform.FailNextRegistration = true;

        Assert.False(service.TrySetHotkey(GlobalHotkeyAction.BattleReset, rejected));
        Assert.Equal(previous, platform.ActiveRegistrations[(firstHwnd, GlobalHotkeyService.BattleResetHotkeyId)]);

        service.DetachWindow();
        service.AttachWindow(secondHwnd);

        Assert.Equal(previous, platform.ActiveRegistrations[(secondHwnd, GlobalHotkeyService.BattleResetHotkeyId)]);
        Assert.DoesNotContain(rejected, platform.ActiveRegistrations.Values);
    }

    [Fact]
    public void TrySetHotkey_WhenCombinationConflicts_MovesTheNativeBindingToTheLatestAction()
    {
        var platform = new RecordingGlobalHotkeyPlatform();
        var service = new GlobalHotkeyService(platform);
        var reset = new HotkeyDefinition(HotkeyModifiers.Control, 0x52);
        var interaction = new HotkeyDefinition(HotkeyModifiers.Alt, 0x49);
        nint firstHwnd = 0x1234;
        nint secondHwnd = 0x5678;

        Assert.True(service.TrySetHotkey(GlobalHotkeyAction.BattleReset, reset));
        Assert.True(service.TrySetHotkey(GlobalHotkeyAction.CycleOverlayInteraction, interaction));
        service.AttachWindow(firstHwnd);

        Assert.True(service.TrySetHotkey(GlobalHotkeyAction.CycleOverlayInteraction, reset));
        Assert.False(platform.ActiveRegistrations.ContainsKey((firstHwnd, GlobalHotkeyService.BattleResetHotkeyId)));
        Assert.Equal(reset, platform.ActiveRegistrations[(firstHwnd, GlobalHotkeyService.CycleOverlayInteractionHotkeyId)]);

        service.DetachWindow();
        service.AttachWindow(secondHwnd);

        Assert.False(platform.ActiveRegistrations.ContainsKey((secondHwnd, GlobalHotkeyService.BattleResetHotkeyId)));
        Assert.Equal(reset, platform.ActiveRegistrations[(secondHwnd, GlobalHotkeyService.CycleOverlayInteractionHotkeyId)]);
    }

    [Fact]
    public void TrySetHotkey_WhenConflictingRegistrationFails_RestoresBothActionBindings()
    {
        var platform = new RecordingGlobalHotkeyPlatform();
        var service = new GlobalHotkeyService(platform);
        var reset = new HotkeyDefinition(HotkeyModifiers.Control, 0x52);
        var interaction = new HotkeyDefinition(HotkeyModifiers.Alt, 0x49);
        nint firstHwnd = 0x1234;
        nint secondHwnd = 0x5678;

        Assert.True(service.TrySetHotkey(GlobalHotkeyAction.BattleReset, reset));
        Assert.True(service.TrySetHotkey(GlobalHotkeyAction.CycleOverlayInteraction, interaction));
        service.AttachWindow(firstHwnd);
        platform.FailNextRegistration = true;

        Assert.False(service.TrySetHotkey(GlobalHotkeyAction.CycleOverlayInteraction, reset));
        Assert.Equal(reset, platform.ActiveRegistrations[(firstHwnd, GlobalHotkeyService.BattleResetHotkeyId)]);
        Assert.Equal(interaction, platform.ActiveRegistrations[(firstHwnd, GlobalHotkeyService.CycleOverlayInteractionHotkeyId)]);

        service.DetachWindow();
        service.AttachWindow(secondHwnd);

        Assert.Equal(reset, platform.ActiveRegistrations[(secondHwnd, GlobalHotkeyService.BattleResetHotkeyId)]);
        Assert.Equal(interaction, platform.ActiveRegistrations[(secondHwnd, GlobalHotkeyService.CycleOverlayInteractionHotkeyId)]);
    }

    [Fact]
    public void WindowMessages_DispatchTheRegisteredActionById()
    {
        AvaloniaTestHost.Run(() =>
        {
            var platform = new RecordingGlobalHotkeyPlatform();
            var service = new GlobalHotkeyService(platform);
            var triggered = new List<GlobalHotkeyAction>();
            service.Triggered += triggered.Add;
            Assert.True(service.TrySetHotkey(GlobalHotkeyAction.BattleReset, new HotkeyDefinition(HotkeyModifiers.Control, 0x52)));
            Assert.True(service.TrySetHotkey(GlobalHotkeyAction.CycleOverlayInteraction, new HotkeyDefinition(HotkeyModifiers.Alt, 0x49)));
            service.AttachWindow(0x1234);

            service.HandleWindowMessage(GlobalHotkeyService.WmHotkey, GlobalHotkeyService.CycleOverlayInteractionHotkeyId);
            service.HandleWindowMessage(GlobalHotkeyService.WmHotkey, GlobalHotkeyService.BattleResetHotkeyId);
            service.HandleWindowMessage(GlobalHotkeyService.WmHotkey, 0x7FFF);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(
                [GlobalHotkeyAction.CycleOverlayInteraction, GlobalHotkeyAction.BattleReset],
                triggered);

            service.DetachWindow();
            service.HandleWindowMessage(GlobalHotkeyService.WmHotkey, GlobalHotkeyService.BattleResetHotkeyId);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, triggered.Count);
        });
    }

    [Fact]
    public void WindowMessage_DetachedBeforeUiQueueRuns_DoesNotDispatch()
    {
        AvaloniaTestHost.Run(() =>
        {
            var service = new GlobalHotkeyService(new RecordingGlobalHotkeyPlatform());
            var triggered = new List<GlobalHotkeyAction>();
            service.Triggered += triggered.Add;
            Assert.True(service.TrySetHotkey(GlobalHotkeyAction.BattleReset, new HotkeyDefinition(HotkeyModifiers.Control, 0x52)));
            service.AttachWindow(0x1234);

            service.HandleWindowMessage(GlobalHotkeyService.WmHotkey, GlobalHotkeyService.BattleResetHotkeyId);
            service.DetachWindow();
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(triggered);
        });
    }

    [Fact]
    public void WindowMessage_ReattachedBeforeUiQueueRuns_DropsThePreviousRegistrationCallback()
    {
        AvaloniaTestHost.Run(() =>
        {
            var service = new GlobalHotkeyService(new RecordingGlobalHotkeyPlatform());
            var triggered = new List<GlobalHotkeyAction>();
            service.Triggered += triggered.Add;
            Assert.True(service.TrySetHotkey(GlobalHotkeyAction.BattleReset, new HotkeyDefinition(HotkeyModifiers.Control, 0x52)));
            nint hwnd = 0x1234;
            service.AttachWindow(hwnd);

            service.HandleWindowMessage(GlobalHotkeyService.WmHotkey, GlobalHotkeyService.BattleResetHotkeyId);
            service.DetachWindow();
            service.AttachWindow(hwnd);
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(triggered);

            service.HandleWindowMessage(GlobalHotkeyService.WmHotkey, GlobalHotkeyService.BattleResetHotkeyId);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal([GlobalHotkeyAction.BattleReset], triggered);
        });
    }

    private sealed class RecordingGlobalHotkeyPlatform : IGlobalHotkeyPlatform
    {
        public List<(nint Hwnd, int Id, HotkeyDefinition Definition)> Registrations { get; } = [];

        public List<(nint Hwnd, int Id)> Unregistrations { get; } = [];

        public Dictionary<(nint Hwnd, int Id), HotkeyDefinition> ActiveRegistrations { get; } = [];

        public bool FailNextRegistration { get; set; }

        public bool FailNextUnregistration { get; set; }

        public bool Register(nint hwnd, int id, HotkeyDefinition definition)
        {
            Registrations.Add((hwnd, id, definition));

            if (FailNextRegistration)
            {
                FailNextRegistration = false;
                return false;
            }

            ActiveRegistrations[(hwnd, id)] = definition;
            return true;
        }

        public bool Unregister(nint hwnd, int id)
        {
            Unregistrations.Add((hwnd, id));
            if (FailNextUnregistration)
            {
                FailNextUnregistration = false;
                return false;
            }

            return ActiveRegistrations.Remove((hwnd, id));
        }
    }
}
