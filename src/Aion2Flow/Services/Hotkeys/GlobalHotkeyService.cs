using Avalonia.Threading;
using Cloris.Aion2Flow.Services.Logging;

namespace Cloris.Aion2Flow.Services.Hotkeys;

public sealed class GlobalHotkeyService
{
    public const uint WmHotkey = 0x0312;
    internal const int BattleResetHotkeyId = 0xA101;
    internal const int CycleOverlayInteractionHotkeyId = 0xA102;

    private readonly Lock _gate = new();
    private readonly IGlobalHotkeyPlatform _platform;
    private nint _hwnd;
    private HotkeyDefinition? _battleResetHotkey;
    private HotkeyDefinition? _overlayInteractionHotkey;
    private ulong _nextRegistrationToken;
    private ulong _battleResetRegistrationToken;
    private ulong _overlayInteractionRegistrationToken;

    public GlobalHotkeyService() : this(Win32GlobalHotkeyPlatform.Instance)
    {
    }

    internal GlobalHotkeyService(IGlobalHotkeyPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        _platform = platform;
    }

    public event Action<GlobalHotkeyAction>? Triggered;

    public bool AttachWindow(nint hwnd)
    {
        if (hwnd == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hwnd), "A valid window handle is required.");
        }

        lock (_gate)
        {
            if (_hwnd != 0 && !TryReleaseAllRegistrationsLocked())
            {
                return false;
            }

            _hwnd = hwnd;
            var resetRegistered = RegisterConfiguredHotkeyLocked(GlobalHotkeyAction.BattleReset);
            var overlayRegistered = RegisterConfiguredHotkeyLocked(GlobalHotkeyAction.CycleOverlayInteraction);
            return resetRegistered && overlayRegistered;
        }
    }

    public bool DetachWindow()
    {
        lock (_gate)
        {
            if (!TryReleaseAllRegistrationsLocked())
            {
                return false;
            }

            _hwnd = 0;
            return true;
        }
    }

    public bool IsRegistered(GlobalHotkeyAction action)
    {
        lock (_gate)
        {
            _ = OtherAction(action);
            return IsRegisteredLocked(action);
        }
    }

    public bool IsAttachedTo(nint hwnd)
    {
        lock (_gate)
        {
            return hwnd != 0 && _hwnd == hwnd;
        }
    }

    public bool TrySetHotkey(GlobalHotkeyAction action, HotkeyDefinition? definition)
    {
        lock (_gate)
        {
            var otherAction = OtherAction(action);
            if (definition is { IsValid: false })
            {
                return false;
            }

            var previousDefinition = GetDefinitionLocked(action);
            var displacedAction = definition is not null && definition == GetDefinitionLocked(otherAction)
                ? otherAction
                : (GlobalHotkeyAction?)null;

            if (definition == previousDefinition && displacedAction is null &&
                (_hwnd == 0 || definition is null || IsRegisteredLocked(action)))
            {
                return true;
            }

            if (_hwnd == 0)
            {
                CommitDefinitionsLocked(action, definition, displacedAction);
                return true;
            }

            return TryReplaceRegistrationLocked(action, definition, previousDefinition, displacedAction);
        }
    }

    public void HandleWindowMessage(uint msg, nint wParam)
    {
        if (msg != WmHotkey || !TryGetAction((int)wParam, out var action))
        {
            return;
        }

        nint hwnd;
        ulong registrationToken;
        lock (_gate)
        {
            registrationToken = GetRegistrationTokenLocked(action);
            if (_hwnd == 0 || registrationToken == 0)
            {
                return;
            }

            hwnd = _hwnd;
        }

        Dispatcher.UIThread.Post(() => DispatchIfCurrent(hwnd, action, registrationToken));
    }

    private bool TryReplaceRegistrationLocked(GlobalHotkeyAction action, HotkeyDefinition? definition, HotkeyDefinition? previousDefinition, GlobalHotkeyAction? displacedAction)
    {
        var actionWasRegistered = IsRegisteredLocked(action);
        var displacedDefinition = displacedAction is { } displaced ? GetDefinitionLocked(displaced) : null;
        var displacedWasRegistered = displacedAction is { } registeredDisplaced && IsRegisteredLocked(registeredDisplaced);

        if (!TryReleaseRegistrationLocked(action))
        {
            return false;
        }

        if (displacedAction is { } actionToDisplace && !TryReleaseRegistrationLocked(actionToDisplace))
        {
            if (!RestoreRegistrationLocked(action, previousDefinition, actionWasRegistered))
            {
                AppLog.Write(AppLogLevel.Error, $"Failed to restore global hotkey {action} after another binding could not be unregistered.");
            }
            return false;
        }

        if (definition is not null && !TryRegisterLocked(action, definition))
        {
            var restored = RestorePreviousRegistrationsLocked(action, previousDefinition, actionWasRegistered, displacedAction, displacedDefinition, displacedWasRegistered);
            var message = restored
                ? $"Failed to register global hotkey {action}: {definition.Display}. Previous bindings were restored."
                : $"Failed to register global hotkey {action}: {definition.Display}. One or more previous native registrations could not be restored.";
            AppLog.Write(restored ? AppLogLevel.Warning : AppLogLevel.Error, message);
            return false;
        }

        CommitDefinitionsLocked(action, definition, displacedAction);
        return true;
    }

    private bool RestorePreviousRegistrationsLocked(GlobalHotkeyAction action, HotkeyDefinition? previousDefinition, bool actionWasRegistered, GlobalHotkeyAction? displacedAction, HotkeyDefinition? displacedDefinition, bool displacedWasRegistered)
    {
        var actionRestored = RestoreRegistrationLocked(action, previousDefinition, actionWasRegistered);
        var displacedRestored = displacedAction is not { } displaced || RestoreRegistrationLocked(displaced, displacedDefinition, displacedWasRegistered);
        return actionRestored && displacedRestored;
    }

    private bool RestoreRegistrationLocked(GlobalHotkeyAction action, HotkeyDefinition? definition, bool wasRegistered)
    {
        if (!wasRegistered)
        {
            return true;
        }

        if (definition is null)
        {
            throw new InvalidOperationException($"Registered global hotkey {action} has no definition.");
        }

        return TryRegisterLocked(action, definition);
    }

    private bool RegisterConfiguredHotkeyLocked(GlobalHotkeyAction action)
    {
        var definition = GetDefinitionLocked(action);
        if (definition is null)
        {
            return true;
        }

        if (!TryRegisterLocked(action, definition))
        {
            AppLog.Write(AppLogLevel.Warning, $"Failed to register global hotkey {action}: {definition.Display}");
            return false;
        }

        return true;
    }

    private bool TryRegisterLocked(GlobalHotkeyAction action, HotkeyDefinition definition)
    {
        if (!_platform.Register(_hwnd, GetRegistrationId(action), definition))
        {
            return false;
        }

        SetRegistrationTokenLocked(action, NextRegistrationTokenLocked());
        return true;
    }

    private bool TryReleaseRegistrationLocked(GlobalHotkeyAction action)
    {
        if (!IsRegisteredLocked(action))
        {
            return true;
        }

        if (!_platform.Unregister(_hwnd, GetRegistrationId(action)))
        {
            AppLog.Write(AppLogLevel.Warning, $"Failed to unregister global hotkey {action}.");
            return false;
        }

        SetRegistrationTokenLocked(action, 0);
        return true;
    }

    private bool TryReleaseAllRegistrationsLocked()
    {
        var resetReleased = TryReleaseRegistrationLocked(GlobalHotkeyAction.BattleReset);
        var overlayReleased = TryReleaseRegistrationLocked(GlobalHotkeyAction.CycleOverlayInteraction);
        return resetReleased && overlayReleased;
    }

    private void DispatchIfCurrent(nint hwnd, GlobalHotkeyAction action, ulong registrationToken)
    {
        Action<GlobalHotkeyAction>? handler;
        lock (_gate)
        {
            if (_hwnd != hwnd || registrationToken == 0 || GetRegistrationTokenLocked(action) != registrationToken || !IsRegisteredLocked(action))
            {
                return;
            }

            handler = Triggered;
        }

        handler?.Invoke(action);
    }

    private void CommitDefinitionsLocked(GlobalHotkeyAction action, HotkeyDefinition? definition, GlobalHotkeyAction? displacedAction)
    {
        SetDefinitionLocked(action, definition);
        if (displacedAction is { } displaced)
        {
            SetDefinitionLocked(displaced, null);
        }
    }

    private HotkeyDefinition? GetDefinitionLocked(GlobalHotkeyAction action) => action switch
    {
        GlobalHotkeyAction.BattleReset => _battleResetHotkey,
        GlobalHotkeyAction.CycleOverlayInteraction => _overlayInteractionHotkey,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported global hotkey action.")
    };

    private void SetDefinitionLocked(GlobalHotkeyAction action, HotkeyDefinition? definition)
    {
        switch (action)
        {
            case GlobalHotkeyAction.BattleReset:
                _battleResetHotkey = definition;
                break;
            case GlobalHotkeyAction.CycleOverlayInteraction:
                _overlayInteractionHotkey = definition;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported global hotkey action.");
        }
    }

    private bool IsRegisteredLocked(GlobalHotkeyAction action) => GetRegistrationTokenLocked(action) != 0;

    private ulong GetRegistrationTokenLocked(GlobalHotkeyAction action) => action switch
    {
        GlobalHotkeyAction.BattleReset => _battleResetRegistrationToken,
        GlobalHotkeyAction.CycleOverlayInteraction => _overlayInteractionRegistrationToken,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported global hotkey action.")
    };

    private void SetRegistrationTokenLocked(GlobalHotkeyAction action, ulong registrationToken)
    {
        switch (action)
        {
            case GlobalHotkeyAction.BattleReset:
                _battleResetRegistrationToken = registrationToken;
                break;
            case GlobalHotkeyAction.CycleOverlayInteraction:
                _overlayInteractionRegistrationToken = registrationToken;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported global hotkey action.");
        }
    }

    private ulong NextRegistrationTokenLocked()
    {
        do
        {
            _nextRegistrationToken = unchecked(_nextRegistrationToken + 1);
        }
        while (_nextRegistrationToken == 0);

        return _nextRegistrationToken;
    }

    private static int GetRegistrationId(GlobalHotkeyAction action) => action switch
    {
        GlobalHotkeyAction.BattleReset => BattleResetHotkeyId,
        GlobalHotkeyAction.CycleOverlayInteraction => CycleOverlayInteractionHotkeyId,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported global hotkey action.")
    };

    private static GlobalHotkeyAction OtherAction(GlobalHotkeyAction action) => action switch
    {
        GlobalHotkeyAction.BattleReset => GlobalHotkeyAction.CycleOverlayInteraction,
        GlobalHotkeyAction.CycleOverlayInteraction => GlobalHotkeyAction.BattleReset,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported global hotkey action.")
    };

    private static bool TryGetAction(int registrationId, out GlobalHotkeyAction action)
    {
        switch (registrationId)
        {
            case BattleResetHotkeyId:
                action = GlobalHotkeyAction.BattleReset;
                return true;
            case CycleOverlayInteractionHotkeyId:
                action = GlobalHotkeyAction.CycleOverlayInteraction;
                return true;
            default:
                action = default;
                return false;
        }
    }
}
