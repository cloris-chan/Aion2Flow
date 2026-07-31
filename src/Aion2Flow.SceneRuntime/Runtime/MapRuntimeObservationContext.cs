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
    private bool _currentMapIsProvisional;
    private bool _provisionalCombatObserved;
    private bool _arrivalConfirmed;

    public MapEntityIngressState Entities => _entities;

    public bool HasMapScope => _currentMapId != 0 || _currentMapIsProvisional;

    public void MarkCombatObserved()
    {
        if (_currentMapIsProvisional)
            _provisionalCombatObserved = true;
    }

    public bool TryConfirmCurrentMap(uint mapId, out uint boundaryMapId)
    {
        boundaryMapId = 0;
        if (mapId == 0)
            return false;

        if (_currentMapIsProvisional)
        {
            if (_provisionalCombatObserved)
            {
                boundaryMapId = mapId;
                return true;
            }

            _currentMapId = mapId;
            _currentMapIsProvisional = false;
            if (_candidateMapId == mapId)
                _candidateMapId = 0;
            return false;
        }

        if (_currentMapId == 0)
        {
            boundaryMapId = mapId;
            return true;
        }

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

    public MapScopeCandidateResult StageMapCandidate(uint mapId)
    {
        if (mapId == 0)
            return MapScopeCandidateResult.Ignored;

        if (_currentMapIsProvisional)
        {
            if (_provisionalCombatObserved)
            {
                _candidateMapId = mapId;
                _arrivalConfirmed = false;
                return MapScopeCandidateResult.ArrivalBoundary;
            }

            _currentMapId = mapId;
            _currentMapIsProvisional = false;
            _candidateMapId = 0;
            _arrivalConfirmed = false;
            return MapScopeCandidateResult.CurrentMapAdopted;
        }

        if (_currentMapId == 0)
        {
            _candidateMapId = 0;
            _arrivalConfirmed = false;
            return MapScopeCandidateResult.ArrivalBoundary;
        }

        if (_currentMapId == mapId)
        {
            _candidateMapId = 0;
            _arrivalConfirmed = false;
            return MapScopeCandidateResult.CurrentMapAdopted;
        }

        _candidateMapId = mapId;
        if (_arrivalConfirmed)
        {
            _arrivalConfirmed = false;
            return MapScopeCandidateResult.ArrivalBoundary;
        }

        return MapScopeCandidateResult.CandidateStaged;
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
        _arrivalConfirmed = true;

        if (_currentMapIsProvisional && _candidateMapId != 0)
        {
            _currentMapId = _candidateMapId;
            _candidateMapId = 0;
            _currentMapIsProvisional = false;
            _arrivalConfirmed = false;
            return false;
        }

        var startsMapContext =
            (_candidateMapId != 0 && _candidateMapId != _currentMapId) ||
            _mapEventDepartureObserved;
        if (!startsMapContext)
            _candidateMapId = 0;

        if (startsMapContext)
            _arrivalConfirmed = false;

        return startsMapContext;
    }

    public void StartMapContext(uint mapId, bool provisional = false, bool preserveEntities = false)
    {
        if (!preserveEntities)
            _entities = new MapEntityIngressState();
        _mapEventRegistrations.Clear();
        _currentMapId = mapId;
        _currentMapInstanceId = 0;
        _candidateMapId = 0;
        _mapEventDepartureObserved = false;
        _currentMapIsProvisional = provisional;
        _provisionalCombatObserved = false;
        _arrivalConfirmed = false;
    }

}

internal enum MapScopeCandidateResult : byte
{
    Ignored,
    CurrentMapAdopted,
    CandidateStaged,
    ArrivalBoundary
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
