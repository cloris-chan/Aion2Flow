using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Collections;
using System.Collections.Specialized;

namespace Cloris.Aion2Flow.Controls;

public sealed class AnimatedItemsView : Panel
{
    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty = AvaloniaProperty.Register<AnimatedItemsView, IDataTemplate?>(nameof(ItemTemplate));
    public static readonly StyledProperty<IDataTemplate?> EmptyTemplateProperty = AvaloniaProperty.Register<AnimatedItemsView, IDataTemplate?>(nameof(EmptyTemplate));
    public static readonly StyledProperty<double> ItemSpacingProperty = AvaloniaProperty.Register<AnimatedItemsView, double>(nameof(ItemSpacing), 0.0);
    public static readonly StyledProperty<int> MaxVisibleItemsProperty = AvaloniaProperty.Register<AnimatedItemsView, int>(nameof(MaxVisibleItems), 0);
    public static readonly DirectProperty<AnimatedItemsView, IEnumerable?> ItemsSourceProperty = AvaloniaProperty.RegisterDirect<AnimatedItemsView, IEnumerable?>(nameof(ItemsSource), view => view.ItemsSource, (v, value) => v.ItemsSource = value);
    public static readonly DirectProperty<AnimatedItemsView, object?> SelectedItemProperty = AvaloniaProperty.RegisterDirect<AnimatedItemsView, object?>(nameof(SelectedItem), view => view.SelectedItem, (v, value) => v.SelectedItem = value, defaultBindingMode: BindingMode.TwoWay);
    public static readonly DirectProperty<AnimatedItemsView, TimeSpan> MoveDurationProperty = AvaloniaProperty.RegisterDirect<AnimatedItemsView, TimeSpan>(nameof(MoveDuration), view => view.MoveDuration, (v, value) => v.MoveDuration = value);
    public static readonly DirectProperty<AnimatedItemsView, TimeSpan> AddRemoveDurationProperty = AvaloniaProperty.RegisterDirect<AnimatedItemsView, TimeSpan>(nameof(AddRemoveDuration), view => view.AddRemoveDuration, (v, value) => v.AddRemoveDuration = value);
    public static readonly DirectProperty<AnimatedItemsView, double> AddRemoveOffsetProperty = AvaloniaProperty.RegisterDirect<AnimatedItemsView, double>(nameof(AddRemoveOffset), view => view.AddRemoveOffset, (v, value) => v.AddRemoveOffset = value);

    private readonly List<object> _items = [];
    private readonly Dictionary<object, AnimatedItemsViewItem> _containers = [];
    private readonly Dictionary<AnimatedItemsViewItem, double> _targetTops = [];

    private INotifyCollectionChanged? _trackedCollection;
    private Control? _emptyStateControl;
    private bool _hasArrangedOnce;
    private double _contentHeight;
    private double _desiredViewportHeight;
    private double _verticalOffset;
    private double _scrollStep = 48;

    public AnimatedItemsView()
    {
        ClipToBounds = true;
    }

    public IDataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }
    public IDataTemplate? EmptyTemplate { get => GetValue(EmptyTemplateProperty); set => SetValue(EmptyTemplateProperty, value); }
    public double ItemSpacing { get => GetValue(ItemSpacingProperty); set => SetValue(ItemSpacingProperty, value); }
    public int MaxVisibleItems { get => GetValue(MaxVisibleItemsProperty); set => SetValue(MaxVisibleItemsProperty, value); }

    public IEnumerable? ItemsSource
    {
        get;
        set
        {
            field = value;
            DetachCollectionChanged();
            AttachCollectionChanged(field);
            FullSync(animate: false);
        }
    }

    public object? SelectedItem { get; set => ApplySelection(field = value); }

    public TimeSpan MoveDuration { get; set; } = TimeSpan.FromMilliseconds(220);

    public TimeSpan AddRemoveDuration { get; set; } = TimeSpan.FromMilliseconds(180);

    public double AddRemoveOffset { get; set; } = 28;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemTemplateProperty)
        {
            Children.Clear();
            _containers.Clear();
            _items.Clear();
            _targetTops.Clear();
            _emptyStateControl = null;
            _hasArrangedOnce = false;
            FullSync(animate: false);
        }
        else if (change.Property == EmptyTemplateProperty)
        {
            UpdateEmptyStateControl(rebuild: true);
            InvalidateMeasure();
            InvalidateArrange();
        }
        else if (change.Property == DataContextProperty)
        {
            UpdateEmptyStateControl(rebuild: true);
            InvalidateMeasure();
            InvalidateArrange();
        }
        else if (change.Property == ItemSpacingProperty || change.Property == MaxVisibleItemsProperty)
        {
            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? Bounds.Width : availableSize.Width;
        if (double.IsNaN(width) || width <= 0) width = double.PositiveInfinity;
        var measureSize = new Size(width, double.PositiveInfinity);

        double totalHeight = 0;
        double maxWidth = 0;
        double viewportHeight = 0;
        double measuredItemHeight = 0;
        int measuredItemCount = 0;
        double removingHeight = 0;
        int removingCount = 0;
        int visibleItemLimit = MaxVisibleItems <= 0 ? int.MaxValue : MaxVisibleItems;
        int visibleItemCount = Math.Min(_items.Count, visibleItemLimit);

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (!_containers.TryGetValue(item, out var container)) continue;

            container.Measure(measureSize);
            maxWidth = Math.Max(maxWidth, container.DesiredSize.Width);

            if (i > 0)
            {
                totalHeight += ItemSpacing;
                if (i < visibleItemCount)
                {
                    viewportHeight += ItemSpacing;
                }
            }

            totalHeight += container.DesiredSize.Height;
            measuredItemHeight += container.DesiredSize.Height;
            measuredItemCount++;

            if (i < visibleItemCount)
            {
                viewportHeight += container.DesiredSize.Height;
            }
        }

        foreach (var container in Children.OfType<AnimatedItemsViewItem>().Where(c => c.IsRemoving))
        {
            container.Measure(measureSize);
            maxWidth = Math.Max(maxWidth, container.DesiredSize.Width);

            if (_items.Count == 0 && removingCount < visibleItemLimit)
            {
                if (removingCount > 0)
                {
                    removingHeight += ItemSpacing;
                }

                removingHeight += container.DesiredSize.Height;
                removingCount++;
            }
        }

        if (visibleItemCount == 0 && _emptyStateControl is not null && Children.Contains(_emptyStateControl))
        {
            _emptyStateControl.Measure(measureSize);
            maxWidth = Math.Max(maxWidth, _emptyStateControl.DesiredSize.Width);
            totalHeight = Math.Max(totalHeight, _emptyStateControl.DesiredSize.Height);
            viewportHeight = Math.Max(viewportHeight, _emptyStateControl.DesiredSize.Height);
        }
        else if (visibleItemCount == 0 && removingCount > 0)
        {
            totalHeight = Math.Max(totalHeight, removingHeight);
            viewportHeight = Math.Max(viewportHeight, removingHeight);
        }

        _contentHeight = totalHeight;
        _desiredViewportHeight = visibleItemCount == 0 ? viewportHeight : (MaxVisibleItems <= 0 ? totalHeight : viewportHeight);
        _scrollStep = measuredItemCount == 0 ? 48 + ItemSpacing : (measuredItemHeight / measuredItemCount) + ItemSpacing;
        _verticalOffset = CoerceVerticalOffset(_verticalOffset);

        var desiredHeight = double.IsInfinity(availableSize.Height)
            ? _desiredViewportHeight
            : Math.Min(_desiredViewportHeight, availableSize.Height);

        return new Size(maxWidth, desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _verticalOffset = CoerceVerticalOffset(_verticalOffset, finalSize.Height);
        double currentY = 0;

        if (_emptyStateControl is not null && Children.Contains(_emptyStateControl))
        {
            _emptyStateControl.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        }

        foreach (var item in _items)
        {
            if (!_containers.TryGetValue(item, out var container)) continue;

            container.Arrange(new Rect(0, 0, finalSize.Width, container.DesiredSize.Height));
            var transform = EnsureTranslateTransform(container);
            var targetY = currentY - _verticalOffset;

            if (!_hasArrangedOnce)
            {
                transform.Y = targetY;
                _targetTops[container] = targetY;
            }
            else
            {
                if (container.IsAdding)
                {
                    transform.Y = targetY;
                    _targetTops[container] = targetY;

                    EnsureTransitions(container);
                    BeginAddAnimation(container);
                }
                else if (!_targetTops.TryGetValue(container, out double oldY) || Math.Abs(oldY - targetY) > 0.1)
                {
                    _targetTops[container] = targetY;
                    transform.Y = targetY;
                }
            }

            currentY += container.DesiredSize.Height + ItemSpacing;
        }

        foreach (var container in Children.OfType<AnimatedItemsViewItem>().Where(c => c.IsRemoving))
        {
            if (_targetTops.TryGetValue(container, out double removeY))
            {
                container.Arrange(new Rect(0, 0, finalSize.Width, container.DesiredSize.Height));
            }
        }

        if (!_hasArrangedOnce)
        {
            _hasArrangedOnce = true;
            foreach (var item in _items)
            {
                if (_containers.TryGetValue(item, out var container))
                {
                    container.Opacity = 1;
                    EnsureTranslateTransform(container).X = 0;
                    EnsureTransitions(container);
                }
            }
        }

        return finalSize;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var nextOffset = CoerceVerticalOffset(_verticalOffset - (e.Delta.Y * _scrollStep));
        if (Math.Abs(nextOffset - _verticalOffset) > 0.1)
        {
            _verticalOffset = nextOffset;
            InvalidateArrange();
            e.Handled = true;
        }

        base.OnPointerWheelChanged(e);
    }

    private void AttachCollectionChanged(IEnumerable? source)
    {
        if (source is INotifyCollectionChanged notify)
        {
            _trackedCollection = notify;
            _trackedCollection.CollectionChanged += OnItemsSourceCollectionChanged;
        }
    }

    private void DetachCollectionChanged()
    {
        _trackedCollection?.CollectionChanged -= OnItemsSourceCollectionChanged;
        _trackedCollection = null;
    }

    private void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems != null)
                {
                    int insertIndex = e.NewStartingIndex;
                    foreach (var newItem in e.NewItems)
                    {
                        _items.Insert(insertIndex, newItem);
                        var container = CreateContainer(newItem, startAsAdding: _hasArrangedOnce);
                        _containers[newItem] = container;
                        Children.Insert(insertIndex, container);
                        insertIndex++;
                    }
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                {
                    foreach (var oldItem in e.OldItems)
                    {
                        if (_containers.TryGetValue(oldItem, out var container))
                        {
                            _items.Remove(oldItem);
                            _containers.Remove(oldItem);
                            BeginRemoveAnimation(container);
                        }
                    }
                }
                break;

            case NotifyCollectionChangedAction.Move:
                if (e.OldItems != null && e.NewItems != null)
                {
                    var itemToMove = e.OldItems[0]!;
                    _items.RemoveAt(e.OldStartingIndex);
                    _items.Insert(e.NewStartingIndex, itemToMove);

                    if (_containers.TryGetValue(itemToMove, out var container))
                    {
                        Children.Remove(container);
                        Children.Insert(e.NewStartingIndex, container);
                    }
                }
                break;

            case NotifyCollectionChangedAction.Replace:
                if (e.OldItems != null && e.NewItems != null)
                {
                    int index = e.NewStartingIndex;
                    var oldItem = e.OldItems[0]!;
                    var newItem = e.NewItems[0]!;

                    if (_containers.TryGetValue(oldItem, out var oldContainer))
                    {
                        _containers.Remove(oldItem);
                        BeginRemoveAnimation(oldContainer);
                    }

                    _items[index] = newItem;
                    var newContainer = CreateContainer(newItem, startAsAdding: _hasArrangedOnce);
                    _containers[newItem] = newContainer;
                    Children.Insert(index, newContainer);
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                FullSync(animate: true);
                break;
        }

        RefreshSelectionState();
        UpdateEmptyStateControl();
        InvalidateMeasure();
    }

    private void FullSync(bool animate)
    {
        var nextItems = ItemsSource?.Cast<object>().ToList() ?? [];
        var nextSet = nextItems.ToHashSet(ReferenceEqualityComparer.Instance);

        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            if (!nextSet.Contains(item))
            {
                var container = _containers[item];
                _items.RemoveAt(i);
                _containers.Remove(item);

                if (animate && _hasArrangedOnce) BeginRemoveAnimation(container);
                else
                {
                    Children.Remove(container);
                    _targetTops.Remove(container);
                }
            }
        }

        for (int i = 0; i < nextItems.Count; i++)
        {
            var item = nextItems[i];
            if (_containers.TryGetValue(item, out var container))
            {
                int currentIndex = _items.IndexOf(item);
                if (currentIndex != i)
                {
                    _items.RemoveAt(currentIndex);
                    _items.Insert(i, item);
                    Children.Remove(container);
                    Children.Insert(i, container);
                }
            }
            else
            {
                _items.Insert(i, item);
                container = CreateContainer(item, startAsAdding: animate && _hasArrangedOnce);
                _containers[item] = container;
                Children.Insert(i, container);
            }
        }

        RefreshSelectionState();
        UpdateEmptyStateControl();
        InvalidateMeasure();
    }

    private AnimatedItemsViewItem CreateContainer(object item, bool startAsAdding = false)
    {
        var container = new AnimatedItemsViewItem
        {
            Owner = this,
            Content = item,
            ContentTemplate = ItemTemplate,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        var transform = EnsureTranslateTransform(container);

        if (startAsAdding)
        {
            container.IsAdding = true;
            container.Opacity = 0;
            transform.X = -AddRemoveOffset;
        }

        return container;
    }

    private static void BeginAddAnimation(AnimatedItemsViewItem container)
    {
        container.IsAdding = false;

        Dispatcher.UIThread.Post(() =>
        {
            if (!container.IsRemoving)
            {
                container.Opacity = 1;
                ((TranslateTransform)container.RenderTransform!).X = 0;
            }
        }, DispatcherPriority.Render);
    }

    private void BeginRemoveAnimation(AnimatedItemsViewItem container)
    {
        container.IsRemoving = true;
        container.IsAdding = false;

        var transform = EnsureTranslateTransform(container);
        _targetTops.Remove(container);

        Dispatcher.UIThread.Post(() =>
        {
            container.Opacity = 0;
            transform.X = AddRemoveOffset;

            DispatcherTimer.RunOnce(() =>
            {
                Children.Remove(container);
                container.IsRemoving = false;
                UpdateEmptyStateControl();
                InvalidateMeasure();
                InvalidateArrange();
            }, AddRemoveDuration);

        }, DispatcherPriority.Render);
    }

    private void EnsureTransitions(AnimatedItemsViewItem container)
    {
        container.Transitions ??=
        [
            new DoubleTransition { Property = OpacityProperty, Duration = AddRemoveDuration }
        ];

        if (container.RenderTransform is TranslateTransform t && t.Transitions == null)
        {
            t.Transitions =
            [
                new DoubleTransition { Property = TranslateTransform.XProperty, Duration = AddRemoveDuration },
                new DoubleTransition { Property = TranslateTransform.YProperty, Duration = MoveDuration }
            ];
        }
    }

    private static TranslateTransform EnsureTranslateTransform(Visual visual)
    {
        if (visual.RenderTransform is TranslateTransform translate) return translate;

        translate = new TranslateTransform();
        visual.RenderTransform = translate;
        return translate;
    }

    private void RefreshSelectionState()
    {
        ApplySelection(SelectedItem);
    }

    private void ApplySelection(object? selectedItem)
    {
        foreach (var (item, container) in _containers)
        {
            container.IsSelected = ReferenceEquals(item, selectedItem);
        }
    }

    private double CoerceVerticalOffset(double offset, double viewportHeight = double.NaN)
    {
        var effectiveViewportHeight = double.IsNaN(viewportHeight) || viewportHeight <= 0
            ? GetEffectiveViewportHeight()
            : viewportHeight;
        var maxOffset = Math.Max(0, _contentHeight - effectiveViewportHeight);
        return maxOffset <= 0 ? 0 : Math.Clamp(offset, 0, maxOffset);
    }

    private double GetEffectiveViewportHeight()
    {
        if (Bounds.Height > 0)
        {
            return Bounds.Height;
        }

        return _desiredViewportHeight;
    }

    private void UpdateEmptyStateControl(bool rebuild = false)
    {
        if (ShouldShowEmptyState())
        {
            if (rebuild)
            {
                RemoveEmptyStateControl();
                _emptyStateControl = null;
            }

            _emptyStateControl ??= CreateEmptyStateControl();

            if (_emptyStateControl is not null && !Children.Contains(_emptyStateControl))
            {
                Children.Add(_emptyStateControl);
            }

            return;
        }

        RemoveEmptyStateControl();
    }

    private bool ShouldShowEmptyState() =>
        EmptyTemplate is not null &&
        _items.Count == 0 &&
        !Children.OfType<AnimatedItemsViewItem>().Any(static c => c.IsRemoving);

    private Control? CreateEmptyStateControl()
    {
        var control = EmptyTemplate?.Build(DataContext);
        if (control is null)
        {
            return null;
        }

        control.IsHitTestVisible = false;
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.VerticalAlignment = VerticalAlignment.Stretch;
        return control;
    }

    private void RemoveEmptyStateControl()
    {
        if (_emptyStateControl is not null && Children.Contains(_emptyStateControl))
        {
            Children.Remove(_emptyStateControl);
        }
    }
}
