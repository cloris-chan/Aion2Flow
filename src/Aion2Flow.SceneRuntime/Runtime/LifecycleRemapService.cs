namespace Cloris.Aion2Flow.SceneRuntime.Runtime;

public sealed class LifecycleRemapService
{
    private readonly Dictionary<int, int> _remap = [];
    private int _nextSyntheticId = int.MaxValue;
    private int _currentTarget;
    private int _lastObservedNpcSource;

    public int CurrentTarget
    {
        get => _currentTarget;
        set => _currentTarget = Resolve(value);
    }

    public int LastObservedNpcSource => _lastObservedNpcSource;

    public int Resolve(int rawInstanceId) => rawInstanceId > 0 && _remap.TryGetValue(rawInstanceId, out var mapped) ? mapped : rawInstanceId;

    public int Rebind(int rawInstanceId)
    {
        if (rawInstanceId <= 0)
            return rawInstanceId;

        var mapped = Interlocked.Decrement(ref _nextSyntheticId);
        Set(rawInstanceId, mapped);
        return mapped;
    }

    public void Set(int rawInstanceId, int mappedInstanceId)
    {
        if (rawInstanceId <= 0)
            return;

        var previousId = Resolve(rawInstanceId);
        _remap[rawInstanceId] = mappedInstanceId;
        if (_lastObservedNpcSource == previousId || _lastObservedNpcSource == rawInstanceId)
            _lastObservedNpcSource = mappedInstanceId;
        if (_currentTarget == previousId || _currentTarget == rawInstanceId)
            _currentTarget = mappedInstanceId;
    }

    public void RememberNpcObservationSource(int instanceId)
    {
        _lastObservedNpcSource = Resolve(instanceId);
    }
}
