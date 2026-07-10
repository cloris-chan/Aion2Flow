using System.Buffers.Binary;
using System.Collections.Frozen;
using Cloris.Aion2Flow.Resources.Generated;
using K4os.Compression.LZ4;

namespace Cloris.Aion2Flow.Resources.Catalog;

internal static partial class ResourcePackReader
{
    private enum SectionId : ushort
    {
        SkillDefinitions = 1,
        SkillClientMetadata = 2,
        SkillBaseProjections = 3,
        SkillEffectReferences = 4,
        SkillRelatedSkills = 5,
        SkillSemanticStrings = 6,
        SkillEffects = 7,
        SkillEffectFilters = 8,
        SkillEffectFilterLocations = 9,
        SkillEffectLevels = 10,
        SkillProjectiles = 11,
        SkillAbnormals = 12,
        SkillAbnormalEffects = 13,
        SkillAbnormalEffectLevels = 14,
        SkillAbnormalEffectTypes = 15,
        SkillAbnormalOverlapFx = 16,
        SkillAbnormalProperties = 17,
        SkillAbnormalStrings = 18,
        NpcDefinitions = 19,
        NpcNameDefinitions = 20,
        KnownMapIds = 21,
        ServerCodes = 22,
        SkillNames = 101,
        NpcNames = 102,
        NpcCatalogNames = 103,
        MapNames = 104,
        ServerNames = 105
    }

    public static ResourceSharedCatalog LoadShared()
    {
        var payload = LoadPackPayload(ResourcePackManifest.SharedResourceName, ResourcePackDecoder.SharedPackKind, ResourcePackManifest.SharedUncompressedLength, ResourcePackManifest.SharedChecksum);
        var sections = ReadSections(payload);
        var skillSemanticStrings = ReadSkillSemanticStrings(RequireSection(sections, SectionId.SkillSemanticStrings));

        return new ResourceSharedCatalog(
            ReadSkillDefinitions(RequireSection(sections, SectionId.SkillDefinitions)),
            ReadSkillClientMetadata(RequireSection(sections, SectionId.SkillClientMetadata)),
            ReadSkillBaseProjections(RequireSection(sections, SectionId.SkillBaseProjections)),
            ReadSkillEffectReferences(RequireSection(sections, SectionId.SkillEffectReferences)),
            ReadSkillRelatedSkills(RequireSection(sections, SectionId.SkillRelatedSkills)),
            ReadSkillSemanticCatalog(sections, skillSemanticStrings),
            ReadNpcDefinitions(RequireSection(sections, SectionId.NpcDefinitions)),
            ReadNpcNameDefinitions(RequireSection(sections, SectionId.NpcNameDefinitions)),
            ReadKnownMapIds(RequireSection(sections, SectionId.KnownMapIds)),
            ReadServerCodes(RequireSection(sections, SectionId.ServerCodes)));
    }

    public static ResourceLocaleCatalog LoadLocale(string language)
    {
        var entry = ResourcePackManifest.GetLocale(language);
        var payload = LoadPackPayload(entry.ResourceName, ResourcePackDecoder.LocalePackKind, entry.UncompressedLength, entry.Checksum);
        var sections = ReadSections(payload);

        return new ResourceLocaleCatalog(
            language,
            ReadIntStringMap(RequireSection(sections, SectionId.SkillNames)),
            ReadStringStringMap(RequireSection(sections, SectionId.NpcNames)),
            ReadIntStringMap(RequireSection(sections, SectionId.NpcCatalogNames)),
            ReadUIntStringMap(RequireSection(sections, SectionId.MapNames)),
            ReadServerNames(RequireSection(sections, SectionId.ServerNames)));
    }

    private static byte[] LoadPackPayload(string resourceName, byte expectedKind, int expectedUncompressedLength, ulong expectedChecksum)
    {
        using var stream = typeof(ResourceCatalog).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Missing embedded resource pack: {resourceName}");
        using var reader = new BinaryReader(stream);
        var magic = reader.ReadUInt32();
        var version = reader.ReadUInt16();
        var kind = reader.ReadByte();
        _ = reader.ReadByte();
        var uncompressedLength = reader.ReadInt32();
        var checksum = reader.ReadUInt64();
        var compressedLength = reader.ReadInt32();
        if (magic != ResourcePackDecoder.PackMagic || version != ResourcePackDecoder.FormatVersion || kind != expectedKind || uncompressedLength != expectedUncompressedLength || checksum != expectedChecksum || compressedLength < 0)
        {
            throw new InvalidDataException($"Invalid resource pack header: {resourceName}");
        }

        var compressed = reader.ReadBytes(compressedLength);
        if (compressed.Length != compressedLength)
        {
            throw new InvalidDataException($"Truncated resource pack: {resourceName}");
        }

        var payload = new byte[uncompressedLength];
        var decodedLength = LZ4Codec.Decode(compressed, payload);
        if (decodedLength != uncompressedLength || ComputeChecksum(payload) != checksum)
        {
            throw new InvalidDataException($"Invalid resource pack payload: {resourceName}");
        }

        return payload;
    }

    private static Dictionary<SectionId, ResourcePackSection> ReadSections(byte[] payload)
    {
        ReadOnlySpan<byte> cursor = payload;
        var magic = ReadUInt32(ref cursor);
        var version = ReadUInt16(ref cursor);
        if (magic != ResourcePackDecoder.PayloadMagic || version != ResourcePackDecoder.FormatVersion)
        {
            throw new InvalidDataException("Invalid resource pack payload header.");
        }

        var sectionCount = ReadInt32(ref cursor);
        var headers = new SectionHeader[sectionCount];
        for (var i = 0; i < headers.Length; i++)
        {
            headers[i] = new SectionHeader((SectionId)ReadUInt16(ref cursor), ReadInt32(ref cursor), ReadInt32(ref cursor), ReadUInt64(ref cursor));
        }

        var sectionStart = payload.Length - cursor.Length;
        var result = new Dictionary<SectionId, ResourcePackSection>(sectionCount);
        var offset = sectionStart;
        for (var i = 0; i < headers.Length; i++)
        {
            var header = headers[i];
            if (header.Count < 0 || header.Length < 0 || payload.Length - offset < header.Length)
            {
                throw new InvalidDataException($"Invalid resource pack section length: {header.Id}");
            }

            var section = payload.AsMemory(offset, header.Length);
            offset += header.Length;
            if (ComputeChecksum(section.Span) != header.Checksum)
            {
                throw new InvalidDataException($"Invalid resource pack section checksum: {header.Id}");
            }

            result.Add(header.Id, new ResourcePackSection(header.Count, section));
        }

        if (offset != payload.Length)
        {
            throw new InvalidDataException("Resource pack contains trailing payload bytes.");
        }

        return result;
    }

    private static ResourcePackSection RequireSection(IReadOnlyDictionary<SectionId, ResourcePackSection> sections, SectionId sectionId)
        => sections.TryGetValue(sectionId, out var section) ? section : throw new InvalidDataException($"Missing resource pack section: {sectionId}");

    private static SkillDefinitionCatalog ReadSkillDefinitions(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var count = section.Count;
        var result = new SkillDefinitionCatalog(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(new SkillDefinition(
                ReadInt32(ref cursor),
                (SkillCategory)ReadByte(ref cursor),
                (SkillSourceType)ReadByte(ref cursor),
                ReadString(ref cursor)));
        }

        RequireFullyRead(cursor);
        return result;
    }

    private static FrozenDictionary<int, SkillClientMetadata> ReadSkillClientMetadata(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var count = section.Count;
        var result = new Dictionary<int, SkillClientMetadata>(count);
        for (var i = 0; i < count; i++)
        {
            var entry = new SkillClientMetadata(
                ReadInt32(ref cursor),
                ReadString(ref cursor),
                ReadInt32(ref cursor),
                (SkillSourceKeyRelation)ReadByte(ref cursor),
                ReadString(ref cursor),
                ReadInt32(ref cursor),
                ReadByte(ref cursor),
                (SkillClientSkillType)ReadByte(ref cursor),
                (SkillClientSkillSubType)ReadByte(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadInt32(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                (SkillClientOverlapSkillType)ReadByte(ref cursor),
                ReadBool(ref cursor),
                (SkillClientSkillDispositionType)ReadByte(ref cursor),
                (SkillClientSkillDamageType)ReadByte(ref cursor),
                ReadBool(ref cursor),
                (SkillClientNeedMainWeaponType)ReadInt32(ref cursor),
                (SkillClientTargetProcessType)ReadByte(ref cursor),
                ReadInt32(ref cursor),
                ReadSingle(ref cursor),
                ReadSingle(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadSingle(ref cursor),
                ReadSingle(ref cursor),
                ReadBool(ref cursor),
                ReadSingle(ref cursor),
                ReadInt32(ref cursor),
                ReadInt64(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadString(ref cursor),
                ReadString(ref cursor),
                ReadInt32(ref cursor),
                ReadString(ref cursor),
                ReadString(ref cursor),
                ReadString(ref cursor),
                (SkillClientRotateType)ReadByte(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadInt32(ref cursor),
                (SkillClientAutoLoadType)ReadByte(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32Array(ref cursor),
                ReadString(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                (SkillClientProjectileTargetLocationType)ReadByte(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                (SkillClientHideSkillGauge)ReadByte(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                (SkillClientAttributeType)ReadByte(ref cursor),
                (SkillClientSkillCategoryType)ReadInt32(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                (SkillClientElementalSummonGroupType)ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadBool(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                (SkillClientSkillAutoType)ReadByte(ref cursor),
                (SkillClientContextSkillSlotRegisterType)ReadByte(ref cursor),
                ReadInt32(ref cursor));
            result.Add(entry.SkillId, entry);
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary();
    }

    private static FrozenDictionary<int, SkillBaseProjection> ReadSkillBaseProjections(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var count = section.Count;
        var result = new Dictionary<int, SkillBaseProjection>(count);
        for (var i = 0; i < count; i++)
        {
            var projection = new SkillBaseProjection(
                ReadInt32(ref cursor),
                ReadInt32(ref cursor));
            result.Add(projection.SkillCode, projection);
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary();
    }

    private static SkillEffectReference[] ReadSkillEffectReferences(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var count = section.Count;
        var result = new SkillEffectReference[count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new SkillEffectReference(ReadInt32(ref cursor), ReadInt32(ref cursor), (SkillEffectReferenceKind)ReadByte(ref cursor), ReadInt32(ref cursor));
        }

        RequireFullyRead(cursor);
        return result;
    }

    private static SkillRelatedSkill[] ReadSkillRelatedSkills(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var count = section.Count;
        var result = new SkillRelatedSkill[count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new SkillRelatedSkill(ReadInt32(ref cursor), ReadInt32(ref cursor), ReadInt32(ref cursor), (SkillRelationKind)ReadByte(ref cursor), ReadString(ref cursor), ReadString(ref cursor));
        }

        RequireFullyRead(cursor);
        return result;
    }

    private static FrozenDictionary<int, NpcDefinition> ReadNpcDefinitions(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var count = section.Count;
        var result = new Dictionary<int, NpcDefinition>(count);
        for (var i = 0; i < count; i++)
        {
            var code = ReadInt32(ref cursor);
            var definition = new NpcDefinition(code, (NpcCatalogKind)ReadByte(ref cursor), (NpcHpDisplayScale)ReadByte(ref cursor));
            result.Add(definition.Code, definition);
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, NpcNameDefinition> ReadNpcNameDefinitions(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var count = section.Count;
        var result = new Dictionary<string, NpcNameDefinition>(count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var definition = new NpcNameDefinition(ReadString(ref cursor), ReadString(ref cursor), ReadString(ref cursor));
            result.Add(definition.ResourceKey, definition);
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static FrozenSet<uint> ReadKnownMapIds(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var count = section.Count;
        var result = new HashSet<uint>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(ReadUInt32(ref cursor));
        }

        RequireFullyRead(cursor);
        return result.ToFrozenSet();
    }

    private static FrozenSet<int> ReadServerCodes(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var count = section.Count;
        var result = new HashSet<int>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(ReadInt32(ref cursor));
        }

        RequireFullyRead(cursor);
        return result.ToFrozenSet();
    }

    private static FrozenDictionary<int, string> ReadIntStringMap(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var count = section.Count;
        var result = new Dictionary<int, string>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(ReadInt32(ref cursor), ReadString(ref cursor));
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary();
    }

    private static FrozenDictionary<uint, string> ReadUIntStringMap(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var count = section.Count;
        var result = new Dictionary<uint, string>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(ReadUInt32(ref cursor), ReadString(ref cursor));
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, string> ReadStringStringMap(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var count = section.Count;
        var result = new Dictionary<string, string>(count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            result.Add(ReadString(ref cursor), ReadString(ref cursor));
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static FrozenDictionary<int, ServerNameEntry> ReadServerNames(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var count = section.Count;
        var result = new Dictionary<int, ServerNameEntry>(count);
        for (var i = 0; i < count; i++)
        {
            var entry = new ServerNameEntry(ReadInt32(ref cursor), ReadString(ref cursor), ReadString(ref cursor));
            result.Add(entry.Code, entry);
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary();
    }

    private static string ReadString(ref ReadOnlySpan<byte> cursor)
    {
        var length = ReadInt32(ref cursor);
        if (length < 0)
        {
            throw new InvalidDataException("Unexpected null string in resource pack.");
        }

        return ReadStringBody(ref cursor, length);
    }

    private static string ReadStringBody(ref ReadOnlySpan<byte> cursor, int length)
    {
        if (length == 0)
        {
            return string.Empty;
        }

        if (cursor.Length < length)
        {
            throw new InvalidDataException("Truncated string in resource pack.");
        }

        var bytes = cursor[..length].ToArray();
        cursor = cursor[length..];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] ^= ResourcePackDecoder.StringMask;
        }

        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static byte ReadByte(ref ReadOnlySpan<byte> cursor)
    {
        if (cursor.IsEmpty)
        {
            throw new InvalidDataException("Unexpected end of resource pack.");
        }

        var value = cursor[0];
        cursor = cursor[1..];
        return value;
    }

    private static bool ReadBool(ref ReadOnlySpan<byte> cursor)
    {
        var value = ReadByte(ref cursor);
        return value switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException($"Invalid resource pack bool value {value}.")
        };
    }

    private static ushort ReadUInt16(ref ReadOnlySpan<byte> cursor)
    {
        if (cursor.Length < sizeof(ushort))
        {
            throw new InvalidDataException("Unexpected end of resource pack.");
        }

        var value = BinaryPrimitives.ReadUInt16LittleEndian(cursor);
        cursor = cursor[sizeof(ushort)..];
        return value;
    }

    private static int ReadInt32(ref ReadOnlySpan<byte> cursor)
    {
        if (cursor.Length < sizeof(int))
        {
            throw new InvalidDataException("Unexpected end of resource pack.");
        }

        var value = BinaryPrimitives.ReadInt32LittleEndian(cursor);
        cursor = cursor[sizeof(int)..];
        return value;
    }

    private static long ReadInt64(ref ReadOnlySpan<byte> cursor)
    {
        if (cursor.Length < sizeof(long))
        {
            throw new InvalidDataException("Unexpected end of resource pack.");
        }

        var value = BinaryPrimitives.ReadInt64LittleEndian(cursor);
        cursor = cursor[sizeof(long)..];
        return value;
    }

    private static float ReadSingle(ref ReadOnlySpan<byte> cursor)
    {
        if (cursor.Length < sizeof(float))
        {
            throw new InvalidDataException("Unexpected end of resource pack.");
        }

        var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(cursor));
        cursor = cursor[sizeof(float)..];
        return value;
    }

    private static int[] ReadInt32Array(ref ReadOnlySpan<byte> cursor)
    {
        var count = ReadInt32(ref cursor);
        if (count < 0)
        {
            throw new InvalidDataException("Invalid negative Int32 array length in resource pack.");
        }

        var values = new int[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = ReadInt32(ref cursor);
        }

        return values;
    }

    private static uint ReadUInt32(ref ReadOnlySpan<byte> cursor)
    {
        if (cursor.Length < sizeof(uint))
        {
            throw new InvalidDataException("Unexpected end of resource pack.");
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(cursor);
        cursor = cursor[sizeof(uint)..];
        return value;
    }

    private static ulong ReadUInt64(ref ReadOnlySpan<byte> cursor)
    {
        if (cursor.Length < sizeof(ulong))
        {
            throw new InvalidDataException("Unexpected end of resource pack.");
        }

        var value = BinaryPrimitives.ReadUInt64LittleEndian(cursor);
        cursor = cursor[sizeof(ulong)..];
        return value;
    }

    private static void RequireFullyRead(ReadOnlySpan<byte> cursor)
    {
        if (!cursor.IsEmpty)
        {
            throw new InvalidDataException("Resource pack section contains trailing bytes.");
        }
    }

    internal static ulong ComputeChecksum(ReadOnlySpan<byte> bytes)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        for (var i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= prime;
        }

        return hash;
    }

    private readonly record struct ResourcePackSection(int Count, ReadOnlyMemory<byte> Payload);
    private readonly record struct SectionHeader(SectionId Id, int Count, int Length, ulong Checksum);
}
