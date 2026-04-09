using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Collections;
using System.Collections.Specialized;

namespace Cloris.Aion2Flow.Controls;

public sealed class AnimatedItemsView : Panel
{
    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty = AvaloniaProperty.Register<AnimatedItemsView, IDataTemplate?>(nameof(ItemTemplate));
    public static readonly StyledProperty<double> ItemSpacingProperty = AvaloniaProperty.Register<AnimatedItemsView, double>(nameof(ItemSpacing), 0.0);
    public static readonly DirectProperty<AnimatedItemsView, IEnumerable?> ItemsSourceProperty = AvaloniaProperty.RegisterDirect<AnimatedItemsView, IEnumerable?>(nameof(ItemsSource), view => view.ItemsSource, (v, value) => v.ItemsSource = value);
    public static readonly DirectProperty<AnimatedItemsView, object?> SelectedItemProperty = AvaloniaProperty.RegisterDirect<AnimatedItemsView, object?>(nameof(SelectedItem), view => view.SelectedItem, (v, value) => v.SelectedItem = value, defaultBindingMode: BindingMode.TwoWay);
    public static readonly DirectProperty<AnimatedItemsView, TimeSpan> MoveDurationProperty = AvaloniaProperty.RegisterDirect<AnimatedItemsView, TimeSpan>(nameof(MoveDuration), view => view.MoveDuration, (v, value) => v.MoveDuration = value);
    public static readonly DirectProperty<AnimatedItemsView, TimeSpan> AddRemoveDurationProperty = AvaloniaProperty.RegisterDirect<AnimatedItemsView, TimeSpan>(nameof(AddRemoveDuration), view => view.AddRemoveDuration, (v, value) => v.AddRemoveDuration = value);
    public static readonly DirectProperty<AnimatedItemsView, double> AddRemoveOffsetProperty = AvaloniaProperty.RegisterDirect<AnimatedItemsView, double>(nameof(AddRemoveOffset), view => view.AddRemoveOffset, (v, value) => v.AddRemoveOffset = value);

    private readonly List<object> _items = [];
    private readonly Dictionary<object, AnimatedItemsViewItem> _containers = [];
    private readonly Dictionary<AnimatedItemsViewItem, double> _targetTops = [];

    private INotifyCollectionChanged? _trackedCollection;
    private bool _hasArrangedOnce;
    public IDataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }
    public double ItemSpacing { get => GetValue(ItemSpacingProperty); set => SetValue(ItemSpacingProperty, value); }

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
            _hasArrangedOnce = false;
            FullSync(animate: false);
        }
        else if (change.Property == ItemSpacingProperty)
        {
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? Bounds.Width : availableSize.Width;
        if (double.IsNaN(width) || width <= 0) width = double.PositiveInfinity;
        var measureSize = new Size(width, double.PositiveInfinity);

        double totalHeight = 0;
        double maxWidth = 0;

        foreach (var item in _items)
        {
            if (!_containers.TryGetValue(item, out var container)) continue;
            container.Measure(measureSize);
            maxWidth = Math.Max(maxWidth, container.DesiredSize.Width);
            totalHeight += container.DesiredSize.Height;
        }

        if (_items.Count > 0)
        {
            totalHeight += ItemSpacing * (_items.Count - 1);
        }

        foreach (var container in Children.OfType<AnimatedItemsViewItem>().Where(c => c.IsRemoving))
        {
            container.Measure(measureSize);
            maxWidth = Math.Max(maxWidth, container.DesiredSize.Width);
        }

        return new Size(maxWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double currentY = 0;

        foreach (var item in _items)
        {
            if (!_containers.TryGetValue(item, out var container)) continue;

            container.Arrange(new Rect(0, 0, finalSize.Width, container.DesiredSize.Height));
            var transform = EnsureTranslateTransform(container);

            if (!_hasArrangedOnce)
            {
                transform.Y = currentY;
                _targetTops[container] = currentY;
            }
            else
            {
                if (container.IsAdding)
                {
                    transform.Y = currentY;
                    _targetTops[container] = currentY;

                    EnsureTransitions(container);
                    BeginAddAnimation(container);
                }
                else if (!_targetTops.TryGetValue(container, out double oldY) || Math.Abs(oldY - currentY) > 0.1)
                {
                    _targetTops[container] = currentY;
                    transform.Y = currentY;
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
}
