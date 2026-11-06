using System.Text.Json;
using System.Text.Json.Serialization;
using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.SceneRuntime;

namespace Cloris.Aion2Flow.Capture.Diagnostics;

internal sealed record CaptureClientMetadata(
    ClientVersion ClientVersion,
    ProtocolRevision ProtocolRevision,
    string ResourceSetId,
    string ResourceRevision)
{
    internal const string FileName = "session.json";

    public static CaptureClientMetadata FromContext(SceneClientContext context)
        => new(context.Profile.Version, context.Profile.ProtocolRevision, context.Resources.ResourceSet.Id, context.Resources.ResourceSet.Revision);

    public static void Write(string directory, SceneClientContext context)
    {
        var metadata = FromContext(context);
        File.WriteAllText(
            Path.Combine(directory, FileName),
            JsonSerializer.Serialize(metadata, CaptureClientMetadataJsonContext.Default.CaptureClientMetadata));
    }

    public static SceneClientContext Resolve(string streamPath, SceneClientContext? requestedContext)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(streamPath))!;
        var metadataPath = Path.Combine(directory, FileName);
        if (!File.Exists(metadataPath))
            return requestedContext ?? SceneClientContext.Purple;

        using var stream = File.OpenRead(metadataPath);
        var metadata = JsonSerializer.Deserialize(stream, CaptureClientMetadataJsonContext.Default.CaptureClientMetadata)
            ?? throw new InvalidDataException($"Missing capture client metadata: {metadataPath}");
        var context = requestedContext ?? SceneClientContext.Create(ClientProfiles.Get(metadata.ClientVersion));
        if (metadata.ClientVersion != context.Profile.Version ||
            metadata.ProtocolRevision != context.Profile.ProtocolRevision ||
            !string.Equals(metadata.ResourceSetId, context.Resources.ResourceSet.Id, StringComparison.Ordinal) ||
            !string.Equals(metadata.ResourceRevision, context.Resources.ResourceSet.Revision, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The capture requires a different client protocol or resource revision: {metadataPath}");
        }

        return context;
    }
}

[JsonSerializable(typeof(CaptureClientMetadata))]
internal partial class CaptureClientMetadataJsonContext : JsonSerializerContext;
