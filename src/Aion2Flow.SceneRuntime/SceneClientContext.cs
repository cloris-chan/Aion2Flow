using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.SceneRuntime;

public sealed class SceneClientContext
{
    public static SceneClientContext Purple { get; } = Create(ClientProfiles.Purple);

    public SceneClientContext(ClientProfile profile, ResourceCatalogSnapshot resources)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(resources);
        if (!string.Equals(profile.ResourceSetId, resources.ResourceSet.Id, StringComparison.Ordinal))
            throw new ArgumentException("The resource set does not belong to the selected client profile.", nameof(resources));

        Profile = profile;
        Resources = resources;
        CombatResources = new CombatResourceRegistry(resources);
    }

    public ClientProfile Profile { get; }
    public ResourceCatalogSnapshot Resources { get; }
    public CombatResourceRegistry CombatResources { get; }

    public static SceneClientContext Create(ClientProfile profile) => new(profile, ResourceCatalog.Load(profile));
}
