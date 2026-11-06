namespace Cloris.Aion2Flow.Clients;

public enum ClientVersion
{
    Purple = 1,
    Steam = 2
}

public enum ProtocolRevision
{
    Purple20260701 = 1,
    Steam20261029 = 2
}

public sealed record ClientProfile
{
    internal ClientProfile(ClientVersion version, ProtocolRevision protocolRevision, string resourceSetId)
    {
        Version = version;
        ProtocolRevision = protocolRevision;
        ResourceSetId = resourceSetId;
    }

    public ClientVersion Version { get; }
    public ProtocolRevision ProtocolRevision { get; }
    public string ResourceSetId { get; }
}

public static class ClientProfiles
{
    public static ClientProfile Purple { get; } = new(ClientVersion.Purple, ProtocolRevision.Purple20260701, "purple");
    public static ClientProfile Steam { get; } = new(ClientVersion.Steam, ProtocolRevision.Steam20261029, "steam");

    public static IReadOnlyList<ClientProfile> Supported { get; } = Array.AsReadOnly([Purple, Steam]);

    public static ClientProfile Get(ClientVersion version) => version switch
    {
        ClientVersion.Purple => Purple,
        ClientVersion.Steam => Steam,
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported game client version.")
    };
}
