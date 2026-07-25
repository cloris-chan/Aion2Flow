using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Runtime;

public sealed class SceneBoundaryService
{
    private uint _sceneMapCandidate;
    private DirectMapTransition _directTransition;

    public uint CurrentMapId { get; private set; }
    public uint CurrentMapInstanceId { get; private set; }
    public long SceneTransitionRevision { get; private set; }
    public bool IsEmpty => CurrentMapId == 0 && CurrentMapInstanceId == 0 && SceneTransitionRevision == 0 && _sceneMapCandidate == 0 && _directTransition.IsEmpty;

    public bool SetCurrentMap(uint mapId)
    {
        if (mapId == 0)
            return false;

        var changed = false;
        if (_directTransition.IsCommitted && _directTransition.MapId == mapId)
        {
            changed |= ClearDirectTransition();
        }
        else if (!_directTransition.IsPending || _directTransition.MapId != mapId || mapId != CurrentMapId)
        {
            changed |= ClearDirectTransition();
            changed |= CommitChangedMap(mapId);
        }

        changed |= ClearSceneMapCandidate();
        return changed;
    }

    public bool AnnounceDestinationMapTransition(uint mapId)
    {
        if (mapId == 0)
            return false;

        if (!_directTransition.IsEmpty && _directTransition.MapId == mapId)
            return false;

        _directTransition = DirectMapTransition.Announced(mapId, CurrentMapInstanceId);
        return true;
    }

    public bool CommitDestinationMapTransition(uint mapId)
    {
        if (mapId == 0)
            return false;

        if (_directTransition.IsCommitted && _directTransition.MapId == mapId)
            return false;

        var changed = CommitMapBoundary(mapId);
        changed |= ClearSceneMapCandidate();
        changed |= SetDirectTransition(DirectMapTransition.Committed(mapId));
        return changed;
    }

    public bool StageSceneMapCandidate(uint mapId)
    {
        if (mapId == 0 || _sceneMapCandidate == mapId)
            return false;

        _sceneMapCandidate = mapId;
        return true;
    }

    public bool ConfirmSceneMap(uint mapId)
    {
        if (mapId == 0)
            return false;

        var changed = false;
        if (_directTransition.MapId == mapId)
        {
            if (_directTransition.IsCommitted)
            {
                changed |= ClearDirectTransition();
            }
            else if (mapId != CurrentMapId)
            {
                changed |= CommitChangedMap(mapId);
                changed |= ClearDirectTransition();
            }
        }
        else
        {
            changed |= CommitChangedMap(mapId);
            changed |= ClearDirectTransition();
        }

        changed |= ClearSceneMapCandidate();
        return changed;
    }

    public bool ConfirmDestinationMapArrival()
    {
        var changed = false;
        if (_sceneMapCandidate != 0)
        {
            if (_sceneMapCandidate != CurrentMapId)
            {
                changed |= CommitChangedMap(_sceneMapCandidate);
                changed |= ClearDirectTransition();
            }

            changed |= ClearSceneMapCandidate();
        }

        if (_directTransition.IsAnnounced)
        {
            _directTransition = _directTransition.WithArrivalConfirmed();
            changed = true;
        }

        return changed;
    }

    public bool StageMapInstance(uint instanceId)
    {
        if (instanceId == 0)
            return false;

        if (CurrentMapInstanceId == instanceId)
            return false;

        CurrentMapInstanceId = instanceId;
        return true;
    }

    public bool ConfirmMapInstance(uint instanceId)
    {
        if (instanceId == 0)
            return false;

        var changed = false;
        if (_directTransition.IsArrivalConfirmed &&
            _directTransition.MapId == CurrentMapId &&
            _directTransition.BaselineMapInstanceId != 0 &&
            _directTransition.BaselineMapInstanceId != instanceId)
        {
            changed |= CommitMapBoundary(CurrentMapId);
            changed |= SetDirectTransition(DirectMapTransition.Committed(CurrentMapId));
            changed |= ClearSceneMapCandidate();
        }
        else if (_directTransition.IsEmpty && _sceneMapCandidate != 0 && _sceneMapCandidate != CurrentMapId)
        {
            changed |= CommitChangedMap(_sceneMapCandidate);
            changed |= ClearSceneMapCandidate();
        }

        changed |= StageMapInstance(instanceId);
        return changed;
    }

    internal SceneTransitionKind ApplySceneObservation(in SceneObservation scene)
    {
        var previousTransitionRevision = SceneTransitionRevision;

        switch (scene.Kind)
        {
            case SceneObservationKind.CurrentMap:
                SetCurrentMap(scene.MapId);
                break;
            case SceneObservationKind.DestinationMapTransitionAnnounced:
                AnnounceDestinationMapTransition(scene.MapId);
                break;
            case SceneObservationKind.DestinationMapTransitionCountdown:
                CommitDestinationMapTransition(scene.MapId);
                break;
            case SceneObservationKind.SceneMapCandidate:
                StageSceneMapCandidate(scene.MapId);
                break;
            case SceneObservationKind.SceneMapConfirmed:
                ConfirmSceneMap(scene.MapId);
                break;
            case SceneObservationKind.DestinationMapArrival:
                ConfirmDestinationMapArrival();
                break;
            case SceneObservationKind.MapInstanceStaged:
                StageMapInstance(scene.MapInstanceId);
                break;
            case SceneObservationKind.MapInstanceConfirmed:
                ConfirmMapInstance(scene.MapInstanceId);
                break;
        }

        return SceneTransitionRevision != previousTransitionRevision
            ? SceneTransitionKind.MapTransition
            : SceneTransitionKind.None;
    }

    internal SceneBoundaryServiceSnapshot CreateSnapshot() => new(CurrentMapId, CurrentMapInstanceId, SceneTransitionRevision, _sceneMapCandidate, _directTransition);

    internal static SceneBoundaryService FromSnapshot(SceneBoundaryServiceSnapshot snapshot) => new()
    {
        CurrentMapId = snapshot.CurrentMapId,
        CurrentMapInstanceId = snapshot.CurrentMapInstanceId,
        SceneTransitionRevision = snapshot.SceneTransitionRevision,
        _sceneMapCandidate = snapshot.SceneMapCandidate,
        _directTransition = snapshot.DirectTransition
    };

    private bool CommitMapBoundary(uint mapId)
    {
        if (mapId == 0)
            return false;

        if (mapId == CurrentMapId)
        {
            SceneTransitionRevision++;
            return true;
        }

        return CommitChangedMap(mapId);
    }

    private bool CommitChangedMap(uint mapId)
    {
        if (mapId == 0 || mapId == CurrentMapId)
            return false;

        CurrentMapId = mapId;
        CurrentMapInstanceId = 0;
        SceneTransitionRevision++;
        return true;
    }

    private bool ClearSceneMapCandidate()
    {
        if (_sceneMapCandidate == 0)
            return false;

        _sceneMapCandidate = 0;
        return true;
    }

    private bool SetDirectTransition(DirectMapTransition transition)
    {
        if (_directTransition == transition)
            return false;

        _directTransition = transition;
        return true;
    }

    private bool ClearDirectTransition()
    {
        if (_directTransition.IsEmpty)
            return false;

        _directTransition = default;
        return true;
    }

    public void Clear()
    {
        _sceneMapCandidate = 0;
        _directTransition = default;
        CurrentMapId = 0;
        CurrentMapInstanceId = 0;
        SceneTransitionRevision = 0;
    }
}

public enum SceneTransitionKind : byte { None, MapTransition }

internal readonly record struct SceneBoundaryServiceSnapshot(
    uint CurrentMapId,
    uint CurrentMapInstanceId,
    long SceneTransitionRevision,
    uint SceneMapCandidate,
    DirectMapTransition DirectTransition);

internal readonly record struct DirectMapTransition(uint MapId, uint BaselineMapInstanceId, DirectMapTransitionPhase Phase)
{
    public bool IsEmpty => Phase == DirectMapTransitionPhase.None;
    public bool IsAnnounced => Phase == DirectMapTransitionPhase.Announced;
    public bool IsArrivalConfirmed => Phase == DirectMapTransitionPhase.ArrivalConfirmed;
    public bool IsPending => IsAnnounced || IsArrivalConfirmed;
    public bool IsCommitted => Phase == DirectMapTransitionPhase.Committed;

    public static DirectMapTransition Announced(uint mapId, uint baselineMapInstanceId) => new(mapId, baselineMapInstanceId, DirectMapTransitionPhase.Announced);
    public static DirectMapTransition Committed(uint mapId) => new(mapId, 0, DirectMapTransitionPhase.Committed);
    public DirectMapTransition WithArrivalConfirmed() => this with { Phase = DirectMapTransitionPhase.ArrivalConfirmed };
}

internal enum DirectMapTransitionPhase : byte
{
    None,
    Announced,
    ArrivalConfirmed,
    Committed
}
