using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Runtime;

internal sealed class MapRuntimeObservationContext
{
    private readonly HashSet<uint> _mapEventRegistrations = [];
    private MapEntityIngressState _entities = new();
    private uint _currentMapId;
    private uint _currentMapInstanceId;
    private uint _candidateMapId;
    private bool _mapEventDepartureObserved;

    public MapEntityIngressState Entities => _entities;

    public bool TryConfirmCurrentMap(uint mapId, out uint boundaryMapId)
    {
        boundaryMapId = 0;
        if (mapId == 0)
            return false;

        if (_currentMapId == mapId)
        {
            if (_mapEventDepartureObserved)
            {
                boundaryMapId = mapId;
                return true;
            }

            if (_candidateMapId == mapId)
                _candidateMapId = 0;

            return false;
        }

        boundaryMapId = mapId;
        return true;
    }

    public void StageMapCandidate(uint mapId)
    {
        if (mapId == 0)
            return;

        _candidateMapId = mapId;
    }

    public void RegisterMapEvent(uint instanceId)
    {
        if (instanceId == 0)
            return;

        _mapEventRegistrations.Add(instanceId);
        _currentMapInstanceId = instanceId;
    }

    public bool UnregisterMapEvent(uint instanceId)
    {
        if (instanceId == 0 || !_mapEventRegistrations.Remove(instanceId))
            return false;

        if (_currentMapInstanceId == instanceId)
            _currentMapInstanceId = 0;

        _mapEventDepartureObserved = true;
        return true;
    }

    public bool TryCommitArrival(out uint mapId)
    {
        mapId = _candidateMapId != 0 ? _candidateMapId : _currentMapId;
        var startsMapContext =
            (_candidateMapId != 0 && _candidateMapId != _currentMapId) ||
            _mapEventDepartureObserved;
        if (!startsMapContext)
            _candidateMapId = 0;

        return startsMapContext;
    }

    public void StartMapContext(uint mapId)
    {
        _entities = new MapEntityIngressState();
        _mapEventRegistrations.Clear();
        _currentMapId = mapId;
        _currentMapInstanceId = 0;
        _candidateMapId = 0;
        _mapEventDepartureObserved = false;
    }
}

internal sealed class MapEntityIngressState
{
    public LifecycleRemapService Lifecycle { get; } = new();

    public Dictionary<int, RuntimeNpcState> NpcStates { get; } = [];

    public HashSet<int> KnownEntities { get; } = [];

    public Dictionary<int, int> SummonOwnerByInstance { get; } = [];
}

internal sealed class RuntimeNpcState
{
    public int? NpcCode { get; set; }
    public long? Hp { get; set; }
    public long? MaxHp { get; set; }
    public RawPacketReference? NpcCodeRaw { get; set; }
    public RawPacketReference? KindRaw { get; set; }
    public RawPacketReference? HpRaw { get; set; }
    public long? HpObservedAtMilliseconds { get; set; }
    public bool? BattleToggledOn { get; set; }
    public NpcKind? Kind { get; set; }
    public long? Value2136 { get; set; }
    public long? Sequence2136 { get; set; }
    public long? Value0140 { get; set; }
    public long? Value0240 { get; set; }
    public (byte State0, byte State1)? State4636 { get; set; }
    public (int SequenceId, int ResultCode)? Latest2C38 { get; set; }

    public RuntimeNpcStateSnapshot ToSnapshot() =>
        new(
            NpcCode,
            Hp,
            MaxHp,
            HpObservedAtMilliseconds,
            BattleToggledOn,
            Kind,
            Value2136,
            Sequence2136,
            Value0140,
            Value0240,
            State4636,
            Latest2C38);
}
