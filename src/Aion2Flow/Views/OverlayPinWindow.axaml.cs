using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Overlay;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Cloris.Aion2Flow.Views;

public partial class OverlayPinWindow : Window
{
    private readonly OverlayInteractionController _interactionController;
    private readonly LocalizationService _localization;
    private readonly UiScaleService _uiScale;
    private IPointer? _pinDragPointer;
    private MainWindow? _pinDragOwner;
    private Point _pinDragStart;
    private Size _pinDragThreshold;
    private PixelPoint _pinDragPointerScreenStart;
    private PixelPoint _pinDragOwnerStart;
    private bool _isPinDragging;

    public OverlayPinWindow()
        : this(
            Ioc.Default.GetRequiredService<OverlayInteractionController>(),
            Ioc.Default.GetRequiredService<LocalizationService>(),
            Ioc.Default.GetRequiredService<UiScaleService>())
    {
    }

    public OverlayPinWindow(OverlayInteractionController interactionController, LocalizationService localization, UiScaleService uiScale)
    {
        _interactionController = interactionController;
        _localization = localization;
        _uiScale = uiScale;
        InitializeComponent();
        AddHandler(PointerPressedEvent, PinButtonPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerMovedEvent, PinButtonPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, PinButtonPointerReleasedPreview, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, PinButtonPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        PinButton.AddHandler(PointerCaptureLostEvent, PinButtonPointerCaptureLost, RoutingStrategies.Direct, handledEventsToo: true);
        _interactionController.ModeChanged += OnInteractionModeChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        ScalingChanged += OnScalingChanged;
        RefreshPresentation();
    }

    public event EventHandler? PlacementInvalidated;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _uiScale.RegisterWindow(this);
        Dispatcher.UIThread.Post(InvalidatePlacement, DispatcherPriority.Loaded);
    }

    protected override void OnClosed(EventArgs e)
    {
        ResetPinDragTracking();
        RemoveHandler(PointerPressedEvent, PinButtonPointerPressed);
        RemoveHandler(PointerMovedEvent, PinButtonPointerMoved);
        RemoveHandler(PointerReleasedEvent, PinButtonPointerReleasedPreview);
        RemoveHandler(PointerReleasedEvent, PinButtonPointerReleased);
        PinButton.RemoveHandler(PointerCaptureLostEvent, PinButtonPointerCaptureLost);
        _interactionController.ModeChanged -= OnInteractionModeChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
        ScalingChanged -= OnScalingChanged;
        base.OnClosed(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty)
        {
            InvalidatePlacement();
        }
    }

    private void CycleInteractionMode(object? sender, RoutedEventArgs e) => _interactionController.Cycle();

    private void PinButtonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Pointer.IsPrimary || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Owner is not MainWindow owner)
        {
            return;
        }

        _isPinDragging = false;
        _pinDragPointer = e.Pointer;
        _pinDragOwner = owner;
        _pinDragStart = e.GetPosition(PinButton);
        _pinDragThreshold = this.GetPlatformSettings()?.GetTapSize(e.Pointer.Type) ?? new Size(4, 4);
        _pinDragPointerScreenStart = PinButton.PointToScreen(_pinDragStart);
        _pinDragOwnerStart = owner.Position;
        e.Pointer.Capture(PinButton);
    }

    private void PinButtonPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pinDragPointer != e.Pointer || _pinDragOwner is not { } owner)
        {
            return;
        }

        if (!e.GetCurrentPoint(PinButton).Properties.IsLeftButtonPressed)
        {
            ResetPinDragTracking();
            return;
        }

        var current = e.GetPosition(PinButton);
        if (!_isPinDragging && !HasExceededDragThreshold(_pinDragStart, current, _pinDragThreshold))
        {
            return;
        }

        if (!_isPinDragging)
        {
            _isPinDragging = true;
        }

        var currentScreen = PinButton.PointToScreen(current);
        owner.Position = CalculateOwnerPosition(_pinDragOwnerStart, _pinDragPointerScreenStart, currentScreen);
        e.Handled = true;
    }

    private void PinButtonPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pinDragPointer == e.Pointer)
        {
            var wasDragging = _isPinDragging;
            ResetPinDragTracking();
            if (wasDragging)
            {
                e.Handled = true;
            }
        }
    }

    private void PinButtonPointerReleasedPreview(object? sender, PointerReleasedEventArgs e)
    {
        if (_pinDragPointer == e.Pointer && _isPinDragging)
        {
            ResetPinDragTracking();
            e.Handled = true;
        }
    }

    private void PinButtonPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_pinDragPointer == e.Pointer)
        {
            ResetPinDragTracking();
        }
    }

    private void ResetPinDragTracking()
    {
        var pointer = _pinDragPointer;
        _pinDragPointer = null;
        _pinDragOwner = null;
        _isPinDragging = false;
        if (pointer?.Captured == PinButton)
        {
            pointer.Capture(null);
        }
    }

    internal static bool HasExceededDragThreshold(Point start, Point current, Size threshold) =>
        Math.Abs(current.X - start.X) >= threshold.Width ||
        Math.Abs(current.Y - start.Y) >= threshold.Height;

    internal static PixelPoint CalculateOwnerPosition(PixelPoint ownerStart, PixelPoint pointerStart, PixelPoint pointerCurrent) => new(
        ownerStart.X + pointerCurrent.X - pointerStart.X,
        ownerStart.Y + pointerCurrent.Y - pointerStart.Y);

    private void OnInteractionModeChanged(OverlayInteractionMode mode) => RefreshPresentation();

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshPresentation();

    private void OnScalingChanged(object? sender, EventArgs e) => InvalidatePlacement();

    internal bool ApplyNativeWindowStyle()
    {
        var popupApplied = NativeOverlayWindowStyles.SetPopupStyle(this, true);
        var noActivateApplied = NativeOverlayWindowStyles.SetNoActivate(this, true);
        return popupApplied && noActivateApplied;
    }

    internal bool SetScreenBounds(PixelRect bounds) => NativeOverlayWindowStyles.SetScreenBounds(this, bounds);

    private void RefreshPresentation()
    {
        PinButton.Classes.Set("click-through", _interactionController.Mode == OverlayInteractionMode.ClickThrough);
        PinButton.Classes.Set("hidden", _interactionController.Mode == OverlayInteractionMode.Hidden);
        ToolTip.SetTip(PinButton, _localization[ResolveActionLocalizationKey(_interactionController.Mode)]);
    }

    private void InvalidatePlacement() => PlacementInvalidated?.Invoke(this, EventArgs.Empty);

    private static string ResolveActionLocalizationKey(OverlayInteractionMode mode) => mode switch
    {
        OverlayInteractionMode.Interactive => "Overlay_Action_EnableClickThrough",
        OverlayInteractionMode.ClickThrough => "Overlay_Action_Hide",
        OverlayInteractionMode.Hidden => "Overlay_Action_Restore",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };
}
