using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

internal sealed class SceneProjectionState
{
    private SceneCombatSnapshotAdapter? _adapter;
    private CombatantStatisticsScope _combatantStatisticsScope = CombatantStatisticsScope.All;

    public static SceneProjectionState Create(
        Guid encounterId,
        int combatInitialCapacity = 0,
        ICombatOccurrenceObserver? combatOccurrenceObserver = null,
        IAuraLifecycleObserver? auraLifecycleObserver = null)
        => new(
            encounterId,
            new EntityStore(),
            new SceneBoundaryStore(),
            new RuntimeMetadataRegistry(),
            combatInitialCapacity > 0 ? new CombatStore(combatInitialCapacity) : new CombatStore(),
            combatOccurrenceObserver,
            auraLifecycleObserver);

    public SceneProjectionState(
        Guid encounterId,
        EntityStore entities,
        SceneBoundaryStore boundary,
        RuntimeMetadataRegistry metadataRegistry,
        CombatStore combat,
        ICombatOccurrenceObserver? combatOccurrenceObserver = null,
        IAuraLifecycleObserver? auraLifecycleObserver = null)
    {
        EncounterId = encounterId;
        Entities = entities;
        Boundary = boundary;
        MetadataRegistry = metadataRegistry;
        Combat = combat;
        CombatOccurrenceObserver = combatOccurrenceObserver;
        AuraLifecycleObserver = auraLifecycleObserver;
        Applier = CreateApplier(trackBossFocus: true);
    }

    public Guid EncounterId { get; private set; }

    public EntityStore Entities { get; }

    public SceneBoundaryStore Boundary { get; }

    public RuntimeMetadataRegistry MetadataRegistry { get; }

    public CombatStore Combat { get; }

    public DomainEventApplier Applier { get; private set; }

    public CombatantStatisticsScope CombatantStatisticsScope => _combatantStatisticsScope;

    public SceneCombatSnapshotAdapter Adapter =>
        _adapter ??= new(
            Entities,
            Applier.EntityVitals,
            Combat,
            Applier.Mechanics,
            Applier.Resources,
            Boundary,
            Applier.BossFocus,
            EncounterId);

    private ICombatOccurrenceObserver? CombatOccurrenceObserver { get; }

    private IAuraLifecycleObserver? AuraLifecycleObserver { get; }

    public DomainEventMaterialization ApplyEntry(in ObservedEventEntry entry) =>
        Applier.ApplyEntry(entry);

    public void CompleteFlush() =>
        Applier.CompleteFlush();

    public void Reset(Guid encounterId, bool trackBossFocus)
    {
        EncounterId = encounterId;
        Applier = CreateApplier(trackBossFocus);
        _adapter = null;
    }

    public void SetCombatantStatisticsScope(CombatantStatisticsScope scope)
    {
        _combatantStatisticsScope = scope;
        Applier.CombatantStatisticsScope = scope;
    }

    private DomainEventApplier CreateApplier(bool trackBossFocus)
    {
        return new DomainEventApplier(
            Entities,
            Boundary,
            MetadataRegistry,
            Combat,
            CombatOccurrenceObserver,
            AuraLifecycleObserver)
        {
            TrackBossFocus = trackBossFocus,
            CombatantStatisticsScope = _combatantStatisticsScope
        };
    }
}
