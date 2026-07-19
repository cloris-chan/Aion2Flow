using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Cloris.Aion2Flow.Controls;

public sealed class AnimatedItemsView : Panel
{
    private const int OverscanRows = 1;
    private const int FractionalViewportExtraRows = 1;

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty = AvaloniaProperty.Register<AnimatedItemsView, IDataTemplate?>(nameof(ItemTemplate));
    public static readonly StyledProperty<IDataTemplate?> EmptyTemplateProperty = AvaloniaProperty.Register<AnimatedItemsView, IDataTemplate?>(nameof(EmptyTemplate));
    public static readonly StyledProperty<double> ItemHeightProperty = AvaloniaProperty.Register<AnimatedItemsView, double>(nameof(ItemHeight), 36, validate: static value => double.IsFinite(value) && value > 0);
    public static readonly StyledProperty<double> ItemSpacingProperty = AvaloniaProperty.Register<AnimatedItemsView, double>(nameof(ItemSpacing), 0, validate: static value => double.IsFinite(value) && value >= 0);
    public static readonly StyledProperty<int> MaxVisibleItemsProperty = AvaloniaProperty.Register<AnimatedItemsView, int>(nameof(MaxVisibleItems), 5, validate: static value => value > 0);
    public static readonly StyledProperty<TimeSpan> MoveDurationProperty = AvaloniaProperty.Register<AnimatedItemsView, TimeSpan>(nameof(MoveDuration), TimeSpan.FromMilliseconds(220), validate: static value => value >= TimeSpan.Zero);
    public static readonly StyledProperty<TimeSpan> AddRemoveDurationProperty = AvaloniaProperty.Register<AnimatedItemsView, TimeSpan>(nameof(AddRemoveDuration), TimeSpan.FromMilliseconds(180), validate: static value => value >= TimeSpan.Zero);
    public static readonly StyledProperty<double> AddRemoveOffsetProperty = AvaloniaProperty.Register<AnimatedItemsView, double>(nameof(AddRemoveOffset), 28, validate: double.IsFinite);
    public static readonly DirectProperty<AnimatedItemsView, IEnumerable?> ItemsSourceProperty = AvaloniaProperty.RegisterDirect<AnimatedItemsView, IEnumerable?>(nameof(ItemsSource), static view => view.ItemsSource, static (view, value) => view.ItemsSource = value);
    public static readonly DirectProperty<AnimatedItemsView, object?> SelectedItemProperty = AvaloniaProperty.RegisterDirect<AnimatedItemsView, object?>(nameof(SelectedItem), static view => view.SelectedItem, static (view, value) => view.SelectedItem = value, defaultBindingMode: BindingMode.TwoWay);

    private readonly List<object> _items = [];
    private readonly Dictionary<object, AnimatedItemsViewItem> _realizedContainers = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<AnimatedItemsViewItem> _recyclePool = [];
    private readonly HashSet<object> _desiredItems = new(ReferenceEqualityComparer.Instance);
    private readonly List<object> _itemsToRecycle = [];
    private readonly HashSet<object> _pendingEntranceItems = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, double> _previousPositions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AnimatedItemsViewItem, int> _pendingAnimations = [];
    private readonly Dictionary<AnimatedItemsViewItem, DepartureAnimationState> _departingContainers = [];
    private readonly List<AnimatedItemsViewItem> _departuresToRecycle = [];
    private readonly Action _completeAnimations;

    private INotifyCollectionChanged? _trackedCollection;
    private IEnumerable? _itemsSource;
    private object? _selectedItem;
    private Control? _emptyStateControl;
    private bool _animateMovesOnNextArrange;
    private bool _animationCompletionScheduled;
    private bool _departureFrameScheduled;
    private bool _hasArrangedOnce;
    private long _departureFrameGeneration;
    private int _realizedStart;
    private int _realizedEndExclusive;
    private double _verticalOffset;
    private double _viewportHeight;

    static AnimatedItemsView()
    {
        AffectsMeasure<AnimatedItemsView>(ItemHeightProperty, ItemSpacingProperty, MaxVisibleItemsProperty);
    }

    public AnimatedItemsView()
    {
        ClipToBounds = true;
        _completeAnimations = CompleteAnimations;
    }

    public IDataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }
    public IDataTemplate? EmptyTemplate { get => GetValue(EmptyTemplateProperty); set => SetValue(EmptyTemplateProperty, value); }
    public double ItemHeight { get => GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }
    public double ItemSpacing { get => GetValue(ItemSpacingProperty); set => SetValue(ItemSpacingProperty, value); }
    public int MaxVisibleItems { get => GetValue(MaxVisibleItemsProperty); set => SetValue(MaxVisibleItemsProperty, value); }
    public TimeSpan MoveDuration { get => GetValue(MoveDurationProperty); set => SetValue(MoveDurationProperty, value); }
    public TimeSpan AddRemoveDuration { get => GetValue(AddRemoveDurationProperty); set => SetValue(AddRemoveDurationProperty, value); }
    public double AddRemoveOffset { get => GetValue(AddRemoveOffsetProperty); set => SetValue(AddRemoveOffsetProperty, value); }

    public IEnumerable? ItemsSource
    {
        get => _itemsSource;
        set
        {
            if (ReferenceEquals(_itemsSource, value))
                return;

            DetachCollectionChanged();
            SetAndRaise(ItemsSourceProperty, ref _itemsSource, value);
            AttachCollectionChanged(value);
            ReloadItems(resetOffset: true);
        }
    }

    public object? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(_selectedItem, value))
                return;

            SetAndRaise(SelectedItemProperty, ref _selectedItem, value);
            ApplySelection();
        }
    }

    public void ScrollIntoView(object? item)
    {
        if (item is null || _items.Count == 0)
            return;

        var index = IndexOfReference(_items, item);
        if (index < 0)
            return;

        var viewportHeight = GetEffectiveViewportHeight();
        var itemTop = index * RowExtent;
        var itemBottom = itemTop + ItemHeight;
        var nextOffset = _verticalOffset;

        if (itemTop < _verticalOffset)
            nextOffset = itemTop;
        else if (itemBottom > _verticalOffset + viewportHeight)
            nextOffset = itemBottom - viewportHeight;

        SetVerticalOffset(nextOffset, viewportHeight);
    }

    internal bool ScrollByRows(double rowDelta)
    {
        if (!double.IsFinite(rowDelta))
            return false;

        var viewportHeight = GetEffectiveViewportHeight();
        return SetVerticalOffset(_verticalOffset + (rowDelta * RowExtent), viewportHeight);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemTemplateProperty)
        {
            CompleteDepartures();
            RecycleAllRealized(keepForReuse: false);
            _recyclePool.Clear();
            _hasArrangedOnce = false;
            EnsureRealizedRange(GetEffectiveViewportHeight());
            InvalidateMeasure();
            InvalidateArrange();
        }
        else if (change.Property == EmptyTemplateProperty || change.Property == DataContextProperty)
        {
            UpdateEmptyStateControl(rebuild: true);
            InvalidateMeasure();
            InvalidateArrange();
        }
        else if (change.Property == ItemHeightProperty || change.Property == ItemSpacingProperty || change.Property == MaxVisibleItemsProperty)
        {
            CompleteDepartures();
            _verticalOffset = CoerceVerticalOffset(_verticalOffset, CalculateDesiredViewportHeight());
            UpdateRealizedContainerMetrics();
            TrimRecyclePool();
            EnsureRealizedRange(GetEffectiveViewportHeight());
            InvalidateMeasure();
            InvalidateArrange();
        }
        else if (change.Property == MoveDurationProperty || change.Property == AddRemoveDurationProperty)
        {
            RefreshRealizedTransitions();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CompleteDepartures(updateLayout: false);
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_items.Count == 0)
            return MeasureEmptyState(availableSize);

        UpdateEmptyStateControl();
        var desiredViewportHeight = Math.Max(CalculateDesiredViewportHeight(), CalculateDepartureViewportHeight());
        var viewportHeight = double.IsInfinity(availableSize.Height)
            ? desiredViewportHeight
            : Math.Min(desiredViewportHeight, availableSize.Height);
        _viewportHeight = viewportHeight;
        _verticalOffset = CoerceVerticalOffset(_verticalOffset, viewportHeight);
        EnsureRealizedRange(viewportHeight);

        var maxWidth = 0d;
        var measureSize = new Size(availableSize.Width, ItemHeight);
        foreach (var container in _realizedContainers.Values)
        {
            container.Measure(measureSize);
            maxWidth = Math.Max(maxWidth, container.DesiredSize.Width);
        }
        foreach (var container in _departingContainers.Keys)
        {
            container.Measure(measureSize);
            maxWidth = Math.Max(maxWidth, container.DesiredSize.Width);
        }

        return new Size(maxWidth, viewportHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_items.Count == 0)
        {
            ArrangeDepartures(finalSize);
            if (_departingContainers.Count == 0)
                _emptyStateControl?.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
            _hasArrangedOnce = true;
            return finalSize;
        }

        _viewportHeight = finalSize.Height;
        _verticalOffset = CoerceVerticalOffset(_verticalOffset, finalSize.Height);
        EnsureRealizedRange(finalSize.Height);

        var animateMoves = _hasArrangedOnce && _animateMovesOnNextArrange;
        for (var index = _realizedStart; index < _realizedEndExclusive; index++)
        {
            var item = _items[index];
            if (!_realizedContainers.TryGetValue(item, out var container))
                continue;

            var top = (index * RowExtent) - _verticalOffset;
            container.Arrange(new Rect(0, top, finalSize.Width, ItemHeight));
            container.VirtualTop = top;
            container.IsViewportVisible = top + ItemHeight > 0 && top < finalSize.Height;

            if (animateMoves && _previousPositions.TryGetValue(item, out var previousTop))
                BeginMoveAnimation(container, previousTop - top);

            if (_hasArrangedOnce && _pendingEntranceItems.Remove(item))
                BeginEntranceAnimation(container);
        }

        ArrangeDepartures(finalSize);

        _animateMovesOnNextArrange = false;
        _previousPositions.Clear();
        _pendingEntranceItems.Clear();
        _hasArrangedOnce = true;
        return finalSize;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (ScrollByRows(-e.Delta.Y))
            e.Handled = true;

        base.OnPointerWheelChanged(e);
    }

    private Size MeasureEmptyState(Size availableSize)
    {
        _verticalOffset = 0;
        _viewportHeight = 0;
        RecycleAllRealized();
        UpdateEmptyStateControl();

        if (_departingContainers.Count != 0)
        {
            var maxWidth = 0d;
            var measureSize = new Size(availableSize.Width, ItemHeight);
            foreach (var container in _departingContainers.Keys)
            {
                container.Measure(measureSize);
                maxWidth = Math.Max(maxWidth, container.DesiredSize.Width);
            }

            var desiredHeight = CalculateDepartureViewportHeight();
            _viewportHeight = double.IsInfinity(availableSize.Height)
                ? desiredHeight
                : Math.Min(desiredHeight, availableSize.Height);
            return new Size(maxWidth, _viewportHeight);
        }

        if (_emptyStateControl is null)
            return default;

        _emptyStateControl.Measure(availableSize);
        var desired = _emptyStateControl.DesiredSize;
        _viewportHeight = double.IsInfinity(availableSize.Height)
            ? desired.Height
            : Math.Min(desired.Height, availableSize.Height);
        return new Size(desired.Width, _viewportHeight);
    }

    private void AttachCollectionChanged(IEnumerable? source)
    {
        if (source is not INotifyCollectionChanged collection)
            return;

        _trackedCollection = collection;
        collection.CollectionChanged += OnItemsSourceCollectionChanged;
    }

    private void DetachCollectionChanged()
    {
        if (_trackedCollection is not null)
            _trackedCollection.CollectionChanged -= OnItemsSourceCollectionChanged;
        _trackedCollection = null;
    }

    private void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var scrollAnchor = CaptureScrollAnchor();
        CaptureMovePositions();

        var handled = e.Action switch
        {
            NotifyCollectionChangedAction.Add => ApplyAdd(e),
            NotifyCollectionChangedAction.Remove => ApplyRemove(e),
            NotifyCollectionChangedAction.Move => ApplyMove(e),
            NotifyCollectionChangedAction.Replace => ApplyReplace(e),
            NotifyCollectionChangedAction.Reset => false,
            _ => false
        };

        if (!handled)
        {
            ReloadItemSnapshot();
            BeginDeparturesMissingFromItems();
        }

        RestoreScrollAnchor(scrollAnchor);
        CompleteCollectionMutation();
    }

    private bool ApplyAdd(NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null || e.NewStartingIndex < 0 || e.NewStartingIndex > _items.Count)
            return false;

        var insertIndex = e.NewStartingIndex;
        foreach (var newItem in e.NewItems)
        {
            if (newItem is null)
                return false;

            _items.Insert(insertIndex++, newItem);
            _pendingEntranceItems.Add(newItem);
        }

        return true;
    }

    private bool ApplyRemove(NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is null || e.OldStartingIndex < 0 || e.OldStartingIndex + e.OldItems.Count > _items.Count)
            return false;

        for (var index = 0; index < e.OldItems.Count; index++)
        {
            if (!ReferenceEquals(_items[e.OldStartingIndex + index], e.OldItems[index]))
                return false;
        }

        BeginDepartures(e.OldItems);
        _items.RemoveRange(e.OldStartingIndex, e.OldItems.Count);
        return true;
    }

    private bool ApplyMove(NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not { Count: 1 } || e.OldStartingIndex < 0 || e.OldStartingIndex >= _items.Count || e.NewStartingIndex < 0 || e.NewStartingIndex >= _items.Count)
            return false;

        var item = _items[e.OldStartingIndex];
        if (!ReferenceEquals(item, e.OldItems[0]))
            return false;

        _items.RemoveAt(e.OldStartingIndex);
        _items.Insert(e.NewStartingIndex, item);
        return true;
    }

    private bool ApplyReplace(NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is null || e.NewItems is null || e.OldStartingIndex < 0 || e.OldStartingIndex + e.OldItems.Count > _items.Count)
            return false;

        for (var index = 0; index < e.OldItems.Count; index++)
        {
            if (!ReferenceEquals(_items[e.OldStartingIndex + index], e.OldItems[index]))
                return false;
        }

        var replacements = new object[e.NewItems.Count];
        for (var index = 0; index < e.NewItems.Count; index++)
        {
            if (e.NewItems[index] is not { } replacement)
                return false;
            replacements[index] = replacement;
        }

        BeginDepartures(e.OldItems);
        _items.RemoveRange(e.OldStartingIndex, e.OldItems.Count);
        _items.InsertRange(e.OldStartingIndex, replacements);
        foreach (var replacement in replacements)
            _pendingEntranceItems.Add(replacement);
        return true;
    }

    private void CompleteCollectionMutation()
    {
        if (_selectedItem is not null && IndexOfReference(_items, _selectedItem) < 0)
            SelectedItem = null;

        if (_items.Count == 0)
        {
            _previousPositions.Clear();
            _pendingEntranceItems.Clear();
            _animateMovesOnNextArrange = false;
        }

        _verticalOffset = CoerceVerticalOffset(_verticalOffset, GetEffectiveViewportHeight());
        UpdateEmptyStateControl();
        EnsureRealizedRange(GetEffectiveViewportHeight());
        TrimPendingEntranceItems();
        ApplySelection();
        InvalidateMeasure();
        InvalidateArrange();
    }

    private void ReloadItems(bool resetOffset)
    {
        CompleteDepartures(updateLayout: false);
        CaptureMovePositions();
        ReloadItemSnapshot();
        if (resetOffset)
            _verticalOffset = 0;
        CompleteCollectionMutation();
    }

    private void ReloadItemSnapshot()
    {
        _items.Clear();
        if (_itemsSource is null)
            return;

        foreach (var item in _itemsSource)
        {
            if (item is not null)
                _items.Add(item);
        }
    }

    private void BeginDepartures(IList items)
    {
        foreach (var item in items)
        {
            if (item is not null)
                BeginDeparture(item);
        }
    }

    private void BeginDeparturesMissingFromItems()
    {
        _itemsToRecycle.Clear();
        foreach (var item in _realizedContainers.Keys)
        {
            if (IndexOfReference(_items, item) < 0)
                _itemsToRecycle.Add(item);
        }

        foreach (var item in _itemsToRecycle)
            BeginDeparture(item);
    }

    private void BeginDeparture(object item)
    {
        if (!_realizedContainers.TryGetValue(item, out var container))
            return;

        var top = container.VirtualTop;
        if (!_hasArrangedOnce || !container.IsViewportVisible || !double.IsFinite(top))
        {
            RecycleContainer(item);
            return;
        }

        while (_departingContainers.Count >= MaxDepartureCount)
            RecycleOldestDeparture();

        _realizedContainers.Remove(item);
        _pendingAnimations.Remove(container);
        _pendingEntranceItems.Remove(item);
        ResetAnimationState(container);
        container.IsHitTestVisible = false;
        var transform = EnsureTranslateTransform(container);
        var departure = new DepartureAnimationState(top, AddRemoveDuration, AddRemoveOffset, container.Transitions, transform.Transitions);
        container.Transitions = null;
        transform.Transitions = null;
        _departingContainers.Add(container, departure);

        if (departure.Duration <= TimeSpan.Zero)
        {
            CompleteDepartures();
            return;
        }

        ScheduleDepartureFrame();
    }

    private ScrollAnchor CaptureScrollAnchor()
    {
        if (_verticalOffset <= 0.1 || _items.Count == 0)
            return default;

        var index = Math.Clamp((int)Math.Floor(_verticalOffset / RowExtent), 0, _items.Count - 1);
        return new ScrollAnchor(_items[index], _verticalOffset - (index * RowExtent));
    }

    private void RestoreScrollAnchor(ScrollAnchor anchor)
    {
        if (anchor.Item is null)
            return;

        var index = IndexOfReference(_items, anchor.Item);
        if (index >= 0)
            _verticalOffset = (index * RowExtent) + anchor.IntraRowOffset;
    }

    private void CaptureMovePositions()
    {
        if (!_hasArrangedOnce || _animateMovesOnNextArrange)
            return;

        _previousPositions.Clear();
        foreach (var (item, container) in _realizedContainers)
            _previousPositions[item] = container.Bounds.Y + EnsureTranslateTransform(container).Y;
        _animateMovesOnNextArrange = true;
    }

    private void EnsureRealizedRange(double viewportHeight)
    {
        if (_items.Count == 0 || viewportHeight <= 0)
        {
            _realizedStart = 0;
            _realizedEndExclusive = 0;
            RecycleAllRealized();
            return;
        }

        var firstVisible = Math.Clamp((int)Math.Floor(_verticalOffset / RowExtent), 0, _items.Count - 1);
        var lastVisible = Math.Clamp((int)Math.Ceiling((_verticalOffset + viewportHeight) / RowExtent) - 1, firstVisible, _items.Count - 1);
        var start = Math.Max(0, firstVisible - OverscanRows);
        var endExclusive = Math.Min(_items.Count, lastVisible + OverscanRows + 1);

        _desiredItems.Clear();
        for (var index = start; index < endExclusive; index++)
            _desiredItems.Add(_items[index]);

        _itemsToRecycle.Clear();
        foreach (var item in _realizedContainers.Keys)
        {
            if (!_desiredItems.Contains(item))
                _itemsToRecycle.Add(item);
        }

        foreach (var item in _itemsToRecycle)
            RecycleContainer(item);

        for (var index = start; index < endExclusive; index++)
        {
            var item = _items[index];
            if (!_realizedContainers.ContainsKey(item))
                _realizedContainers.Add(item, RentContainer(item));
        }

        for (var index = start; index < endExclusive; index++)
        {
            var container = _realizedContainers[_items[index]];
            var targetChildIndex = index - start;
            var currentChildIndex = Children.IndexOf(container);
            if (currentChildIndex == targetChildIndex)
                continue;

            if (currentChildIndex >= 0)
                Children.RemoveAt(currentChildIndex);
            Children.Insert(targetChildIndex, container);
        }

        _realizedStart = start;
        _realizedEndExclusive = endExclusive;
        UpdateRealizedGeometry(viewportHeight);
    }

    private void UpdateRealizedGeometry(double viewportHeight)
    {
        for (var index = _realizedStart; index < _realizedEndExclusive; index++)
        {
            var container = _realizedContainers[_items[index]];
            var top = (index * RowExtent) - _verticalOffset;
            container.VirtualTop = top;
            container.IsViewportVisible = top + ItemHeight > 0 && top < viewportHeight;
        }
    }

    private AnimatedItemsViewItem RentContainer(object item)
    {
        var container = _recyclePool.Count > 0 ? _recyclePool.Pop() : new AnimatedItemsViewItem();
        container.Generation++;
        container.Owner = this;
        container.Content = item;
        container.ContentTemplate = ItemTemplate;
        container.Height = ItemHeight;
        container.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        container.IsHitTestVisible = true;
        container.VirtualTop = 0;
        container.IsViewportVisible = false;
        container.IsSelected = ReferenceEquals(item, _selectedItem);
        ResetAnimationState(container);
        EnsureTransitions(container);
        Children.Add(container);
        return container;
    }

    private void RecycleContainer(object item, bool keepForReuse = true)
    {
        if (!_realizedContainers.Remove(item, out var container))
            return;

        ReturnContainer(container, keepForReuse);
    }

    private void ReturnContainer(AnimatedItemsViewItem container, bool keepForReuse = true)
    {
        _pendingAnimations.Remove(container);
        Children.Remove(container);
        ResetAnimationState(container);
        container.Generation++;
        container.VirtualTop = 0;
        container.IsViewportVisible = false;
        container.IsSelected = false;
        container.IsHitTestVisible = true;
        container.Owner = null;
        container.Content = null;

        if (keepForReuse && _recyclePool.Count < MaxPoolSize)
            _recyclePool.Push(container);
    }

    private void RecycleAllRealized(bool keepForReuse = true)
    {
        _itemsToRecycle.Clear();
        foreach (var item in _realizedContainers.Keys)
            _itemsToRecycle.Add(item);
        foreach (var item in _itemsToRecycle)
            RecycleContainer(item, keepForReuse);
    }

    private void TrimRecyclePool()
    {
        while (_recyclePool.Count > MaxPoolSize)
            _recyclePool.Pop();
    }

    private void TrimPendingEntranceItems()
    {
        _itemsToRecycle.Clear();
        foreach (var item in _pendingEntranceItems)
        {
            if (!_realizedContainers.ContainsKey(item))
                _itemsToRecycle.Add(item);
        }
        foreach (var item in _itemsToRecycle)
            _pendingEntranceItems.Remove(item);
    }

    private void UpdateRealizedContainerMetrics()
    {
        foreach (var container in _realizedContainers.Values)
            container.Height = ItemHeight;
    }

    private void RefreshRealizedTransitions()
    {
        foreach (var container in _realizedContainers.Values)
            EnsureTransitions(container);
    }

    private void ApplySelection()
    {
        foreach (var (item, container) in _realizedContainers)
            container.IsSelected = ReferenceEquals(item, _selectedItem);
    }

    private bool SetVerticalOffset(double value, double viewportHeight)
    {
        var nextOffset = CoerceVerticalOffset(value, viewportHeight);
        if (Math.Abs(nextOffset - _verticalOffset) <= 0.1)
            return false;

        CaptureMovePositions();
        _pendingAnimations.Clear();
        _pendingEntranceItems.Clear();
        var scrollDelta = nextOffset - _verticalOffset;
        foreach (var departure in _departingContainers.Values)
            departure.Top -= scrollDelta;
        _verticalOffset = nextOffset;
        var previousStart = _realizedStart;
        var previousEndExclusive = _realizedEndExclusive;
        EnsureRealizedRange(viewportHeight);
        if (_realizedStart != previousStart || _realizedEndExclusive != previousEndExclusive)
            InvalidateMeasure();
        InvalidateArrange();
        return true;
    }

    private void ArrangeDepartures(Size finalSize)
    {
        foreach (var (container, departure) in _departingContainers)
            container.Arrange(new Rect(0, departure.Top, finalSize.Width, ItemHeight));
    }

    private void ScheduleDepartureFrame()
    {
        if (_departureFrameScheduled || _departingContainers.Count == 0)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            CompleteDepartures();
            return;
        }

        _departureFrameScheduled = true;
        var generation = _departureFrameGeneration;
        topLevel.RequestAnimationFrame(timestamp => AdvanceDepartures(timestamp, generation));
    }

    private void AdvanceDepartures(TimeSpan timestamp, long generation)
    {
        if (generation != _departureFrameGeneration)
            return;

        _departureFrameScheduled = false;
        if (_departingContainers.Count == 0)
            return;

        _departuresToRecycle.Clear();
        foreach (var (container, departure) in _departingContainers)
        {
            if (departure.StartTimestamp is not { } startTimestamp)
            {
                departure.StartTimestamp = timestamp;
                continue;
            }

            var progress = Math.Clamp((timestamp - startTimestamp).TotalMilliseconds / departure.Duration.TotalMilliseconds, 0, 1);
            if (progress >= 1 && !departure.HasIntermediateFrame)
                progress = 0.5;
            if (progress is > 0 and < 1)
                departure.HasIntermediateFrame = true;

            var transform = EnsureTranslateTransform(container);
            container.Opacity = 1 - progress;
            transform.X = departure.Offset * progress;
            transform.Y = 0;

            if (progress >= 1)
                _departuresToRecycle.Add(container);
        }

        foreach (var container in _departuresToRecycle)
            RecycleDeparture(container);

        if (_departuresToRecycle.Count != 0)
        {
            UpdateEmptyStateControl();
            InvalidateMeasure();
            InvalidateArrange();
        }

        ScheduleDepartureFrame();
    }

    private void CompleteDepartures(bool updateLayout = true)
    {
        _departureFrameGeneration++;
        _departureFrameScheduled = false;
        if (_departingContainers.Count == 0)
            return;

        _departuresToRecycle.Clear();
        foreach (var container in _departingContainers.Keys)
            _departuresToRecycle.Add(container);
        foreach (var container in _departuresToRecycle)
            RecycleDeparture(container);

        if (!updateLayout)
            return;

        UpdateEmptyStateControl();
        InvalidateMeasure();
        InvalidateArrange();
    }

    private void RecycleOldestDeparture()
    {
        AnimatedItemsViewItem? oldest = null;
        foreach (var container in _departingContainers.Keys)
        {
            oldest = container;
            break;
        }

        if (oldest is null)
            return;

        RecycleDeparture(oldest);
    }

    private void RecycleDeparture(AnimatedItemsViewItem container)
    {
        if (!_departingContainers.Remove(container, out var departure))
            return;

        var transform = EnsureTranslateTransform(container);
        container.Transitions = departure.OpacityTransitions;
        transform.Transitions = departure.TransformTransitions;
        ReturnContainer(container);
    }

    private void BeginMoveAnimation(AnimatedItemsViewItem container, double offset)
    {
        if (Math.Abs(offset) <= 0.1 || !ReferenceEquals(container.Owner, this))
            return;

        EnsureTransitions(container);
        var transform = EnsureTranslateTransform(container);
        var transitions = transform.Transitions;
        transform.Transitions = null;
        transform.Y = offset;
        transform.Transitions = transitions;
        ScheduleAnimationCompletion(container);
    }

    private void BeginEntranceAnimation(AnimatedItemsViewItem container)
    {
        EnsureTransitions(container);
        var opacityTransitions = container.Transitions;
        var transform = EnsureTranslateTransform(container);
        var transformTransitions = transform.Transitions;

        container.Transitions = null;
        transform.Transitions = null;
        container.Opacity = 0;
        transform.X = -AddRemoveOffset;
        transform.Y = 0;
        container.Transitions = opacityTransitions;
        transform.Transitions = transformTransitions;
        ScheduleAnimationCompletion(container);
    }

    private void ScheduleAnimationCompletion(AnimatedItemsViewItem container)
    {
        _pendingAnimations[container] = container.Generation;
        if (_animationCompletionScheduled)
            return;

        _animationCompletionScheduled = true;
        Dispatcher.UIThread.Post(_completeAnimations, DispatcherPriority.Render);
    }

    private void CompleteAnimations()
    {
        _animationCompletionScheduled = false;
        foreach (var (container, generation) in _pendingAnimations)
        {
            if (container.Generation != generation || !ReferenceEquals(container.Owner, this))
                continue;

            container.Opacity = 1;
            var transform = EnsureTranslateTransform(container);
            transform.X = 0;
            transform.Y = 0;
        }
        _pendingAnimations.Clear();
    }

    private void EnsureTransitions(AnimatedItemsViewItem container)
    {
        if (container.ConfiguredAddRemoveDuration == AddRemoveDuration && container.ConfiguredMoveDuration == MoveDuration)
            return;

        container.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = AddRemoveDuration }
        ];

        var transform = EnsureTranslateTransform(container);
        transform.Transitions =
        [
            new DoubleTransition { Property = TranslateTransform.XProperty, Duration = AddRemoveDuration },
            new DoubleTransition { Property = TranslateTransform.YProperty, Duration = MoveDuration }
        ];
        container.ConfiguredAddRemoveDuration = AddRemoveDuration;
        container.ConfiguredMoveDuration = MoveDuration;
    }

    private static void ResetAnimationState(AnimatedItemsViewItem container)
    {
        var opacityTransitions = container.Transitions;
        var transform = EnsureTranslateTransform(container);
        var transformTransitions = transform.Transitions;

        container.Transitions = null;
        transform.Transitions = null;
        container.Opacity = 1;
        transform.X = 0;
        transform.Y = 0;
        container.Transitions = opacityTransitions;
        transform.Transitions = transformTransitions;
    }

    private static TranslateTransform EnsureTranslateTransform(Visual visual)
    {
        if (visual.RenderTransform is TranslateTransform translate)
            return translate;

        translate = new TranslateTransform();
        visual.RenderTransform = translate;
        return translate;
    }

    private double CoerceVerticalOffset(double offset, double viewportHeight)
    {
        var maxOffset = Math.Max(0, CalculateContentHeight() - Math.Max(0, viewportHeight));
        return maxOffset <= 0 ? 0 : Math.Clamp(offset, 0, maxOffset);
    }

    private double GetEffectiveViewportHeight()
    {
        var desiredHeight = CalculateDesiredViewportHeight();
        if (_viewportHeight > 0)
            return Math.Min(_viewportHeight, desiredHeight);
        if (Bounds.Height > 0)
            return Math.Min(Bounds.Height, desiredHeight);
        return desiredHeight;
    }

    private double CalculateContentHeight()
        => _items.Count == 0 ? 0 : (_items.Count * RowExtent) - ItemSpacing;

    private double CalculateDepartureViewportHeight()
    {
        var height = 0d;
        foreach (var departure in _departingContainers.Values)
            height = Math.Max(height, departure.Top + ItemHeight);
        return Math.Max(0, height);
    }

    private double CalculateDesiredViewportHeight()
    {
        var visibleCount = Math.Min(_items.Count, MaxVisibleItems);
        return visibleCount == 0 ? 0 : (visibleCount * RowExtent) - ItemSpacing;
    }

    private double RowExtent => ItemHeight + ItemSpacing;
    private int MaxIntersectingContainerCount => MaxVisibleItems + FractionalViewportExtraRows;
    private int MaxRealizedContainerCount => MaxIntersectingContainerCount + (OverscanRows * 2);
    private int MaxPoolSize => MaxRealizedContainerCount;
    private int MaxDepartureCount => MaxIntersectingContainerCount;

    private void UpdateEmptyStateControl(bool rebuild = false)
    {
        if (_items.Count != 0 || EmptyTemplate is null || _departingContainers.Count != 0)
        {
            RemoveEmptyStateControl();
            return;
        }

        if (rebuild)
            RemoveEmptyStateControl();

        _emptyStateControl ??= EmptyTemplate.Build(DataContext);
        if (_emptyStateControl is null || Children.Contains(_emptyStateControl))
            return;

        _emptyStateControl.IsHitTestVisible = false;
        _emptyStateControl.HorizontalAlignment = HorizontalAlignment.Stretch;
        _emptyStateControl.VerticalAlignment = VerticalAlignment.Stretch;
        Children.Add(_emptyStateControl);
    }

    private void RemoveEmptyStateControl()
    {
        if (_emptyStateControl is not null)
            Children.Remove(_emptyStateControl);
        _emptyStateControl = null;
    }

    private static int IndexOfReference(IReadOnlyList<object> items, object expected)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], expected))
                return index;
        }
        return -1;
    }

    private sealed class DepartureAnimationState(
        double top,
        TimeSpan duration,
        double offset,
        Transitions? opacityTransitions,
        Transitions? transformTransitions)
    {
        public double Top { get; set; } = top;
        public TimeSpan Duration { get; } = duration;
        public double Offset { get; } = offset;
        public Transitions? OpacityTransitions { get; } = opacityTransitions;
        public Transitions? TransformTransitions { get; } = transformTransitions;
        public TimeSpan? StartTimestamp { get; set; }
        public bool HasIntermediateFrame { get; set; }
    }

    private readonly record struct ScrollAnchor(object? Item, double IntraRowOffset);
}
