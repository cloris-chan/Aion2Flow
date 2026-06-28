using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Cloris.Aion2Flow.Resources;

public static class ResourceDatabase
{
    public static SkillCollection LoadSkills(string lang = "en-US")
    {
        using var connection = CreateConnection();
        connection.Open();

        using var cmd = connection.CreateCommand();
        var nameColumn = GetLocalizedColumn("Name", lang);
        cmd.CommandText = $"""
            SELECT SkillId, {nameColumn}, Category, SourceType, SourceKey, TriggeredSkillIdsCsv
            FROM Skills
            WHERE {nameColumn} IS NOT NULL
            """;

        return ReadSkills(cmd);
    }

    public static SkillCollection LoadCombatSkills()
    {
        using var connection = CreateConnection();
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT SkillId,
                   COALESCE(NameEnUs, NameZhTw, NameKoKr, CAST(SkillId AS TEXT)),
                   Category,
                   SourceType,
                   SourceKey,
                   TriggeredSkillIdsCsv
            FROM Skills
            WHERE SkillId IS NOT NULL
            """;

        return ReadSkills(cmd);
    }

    public static IReadOnlyDictionary<int, SkillPresentation> LoadSkillPresentations()
    {
        using var connection = CreateConnection();
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT SkillCode,
                   PresentationSkillId,
                   DisplaySkillId,
                   BaseSkillId,
                   SpecializationMask,
                   VariantState,
                   IsChargeSkill
            FROM SkillPresentations
            """;

        var presentationsBySkillCode = new Dictionary<int, SkillPresentation>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var presentation = new SkillPresentation(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetByte(6) != 0);
            presentationsBySkillCode[presentation.SkillCode] = presentation;
        }

        return presentationsBySkillCode;
    }

    public static IReadOnlyDictionary<int, SkillAnalysis> LoadSkillAnalysis()
    {
        using var connection = CreateConnection();
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT SkillId,
                   ActionType,
                   SubType,
                   OverlapType,
                   DispositionType,
                   DamageType,
                   TargetProcessType,
                   AttributeType,
                   ClientCategoryType
            FROM SkillAnalysis
            """;

        var analysisBySkillId = new Dictionary<int, SkillAnalysis>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var analysis = new SkillAnalysis(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8));
            analysisBySkillId[analysis.SkillId] = analysis;
        }

        return analysisBySkillId;
    }

    public static IReadOnlyList<SkillEffectRelation> LoadSkillEffectRelations()
    {
        using var connection = CreateConnection();
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT SkillId, Slot, EffectId, EffectDataId, AuxEffectId
            FROM SkillEffectRelations
            ORDER BY SkillId, Slot
            """;

        var relations = new List<SkillEffectRelation>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            relations.Add(new SkillEffectRelation(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4)));
        }

        return relations;
    }

    private static SkillCollection ReadSkills(SqliteCommand cmd)
    {
        var skills = new SkillCollection();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
            {
                continue;
            }

            var name = reader.GetString(1);

            skills.Add(new Skill(
                reader.GetInt32(0),
                name,
                (SkillCategory)reader.GetByte(2),
                (SkillSourceType)reader.GetByte(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return skills;
    }

    public static IReadOnlyDictionary<string, NpcName> LoadNpcNames(string lang = "en-US", string? resourceKeyPrefix = null)
    {
        using var connection = CreateConnection();
        connection.Open();

        using var cmd = connection.CreateCommand();
        var nameColumn = GetLocalizedColumn("Name", lang);
        if (string.IsNullOrWhiteSpace(resourceKeyPrefix))
        {
            cmd.CommandText = $"SELECT ResourceKey, {nameColumn}, KeyPrefix, SourceKey FROM NpcNames WHERE {nameColumn} IS NOT NULL";
        }
        else
        {
            cmd.CommandText = $"SELECT ResourceKey, {nameColumn}, KeyPrefix, SourceKey FROM NpcNames WHERE ResourceKey LIKE $prefix AND {nameColumn} IS NOT NULL";
            cmd.Parameters.AddWithValue("$prefix", resourceKeyPrefix + "%");
        }

        var npcs = new Dictionary<string, NpcName>(StringComparer.Ordinal);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2))
            {
                continue;
            }

            var npc = new NpcName(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3));
            npcs[npc.ResourceKey] = npc;
        }

        return npcs;
    }

    public static IReadOnlyDictionary<int, NpcCatalogEntry> LoadNpcCatalog(string lang = "en-US")
    {
        using var connection = CreateConnection();
        connection.Open();

        using var cmd = connection.CreateCommand();
        var nameColumn = GetLocalizedColumn("Name", lang);
        cmd.CommandText = $"SELECT Code, {nameColumn}, Kind FROM NpcCatalog WHERE {nameColumn} IS NOT NULL";

        var npcs = new Dictionary<int, NpcCatalogEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2))
            {
                continue;
            }

            var entry = new NpcCatalogEntry(
                reader.GetInt32(0),
                reader.GetString(1),
                (NpcCatalogKind)reader.GetInt64(2));
            npcs[entry.Code] = entry;
        }

        return npcs;
    }

    public static IReadOnlyDictionary<uint, string> LoadMaps(string lang = "en-US")
    {
        using var connection = CreateConnection();
        connection.Open();

        using var cmd = connection.CreateCommand();
        var nameColumn = GetLocalizedColumn("Name", lang);
        cmd.CommandText = $"SELECT MapId, {nameColumn} FROM Maps WHERE {nameColumn} IS NOT NULL";

        var maps = new Dictionary<uint, string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
            {
                continue;
            }

            maps[(uint)reader.GetInt64(0)] = reader.GetString(1);
        }

        return maps;
    }

    public static IReadOnlyDictionary<int, ServerNameCatalogEntry> LoadServerNames(string lang = "en-US")
    {
        using var connection = CreateConnection();
        connection.Open();

        using var cmd = connection.CreateCommand();
        var serverNameColumn = GetLocalizedColumn("ServerName", lang);
        var shortServerNameColumn = GetLocalizedColumn("ShortServerName", lang);
        cmd.CommandText = $"""
            SELECT Code, {serverNameColumn}, {shortServerNameColumn}
            FROM ServerNames
            WHERE {serverNameColumn} IS NOT NULL
            """;

        var servers = new Dictionary<int, ServerNameCatalogEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
            {
                continue;
            }

            var serverName = reader.GetString(1);
            var entry = new ServerNameCatalogEntry(
                reader.GetInt32(0),
                serverName,
                reader.IsDBNull(2) ? serverName : reader.GetString(2));
            servers[entry.Code] = entry;
        }

        return servers;
    }

    public static string ResolveMapName(uint mapId, IReadOnlyDictionary<uint, string> mapNames)
    {
        if (mapId == 0)
        {
            return string.Empty;
        }

        return mapNames.TryGetValue(mapId, out var name) ? name : mapId.ToString(CultureInfo.InvariantCulture);
    }

    public static string ResolveServerName(int code, IReadOnlyDictionary<int, ServerNameCatalogEntry> serverNames)
    {
        if (code <= 0)
        {
            return string.Empty;
        }

        return serverNames.TryGetValue(code, out var entry) && !string.IsNullOrWhiteSpace(entry.ServerName)
            ? entry.ServerName
            : code.ToString(CultureInfo.InvariantCulture);
    }

    public static string ResolveShortServerName(int code, IReadOnlyDictionary<int, ServerNameCatalogEntry> serverNames)
    {
        if (code <= 0)
        {
            return string.Empty;
        }

        return serverNames.TryGetValue(code, out var entry) && !string.IsNullOrWhiteSpace(entry.ShortServerName)
            ? entry.ShortServerName
            : ResolveServerName(code, serverNames);
    }

    private static string GetLocalizedColumn(string baseName, string lang) => lang switch
    {
        "en-US" => $"{baseName}EnUs",
        "ko-KR" => $"{baseName}KoKr",
        "zh-TW" => $"{baseName}ZhTw",
        _ => throw new ArgumentOutOfRangeException(nameof(lang), lang, "Unsupported resource language.")
    };

    private static SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = ResolveDatabasePath(),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        };

        return new SqliteConnection(builder.ConnectionString);
    }

    private static string ResolveDatabasePath()
    {
        const string fileName = "resources.db";

        foreach (var root in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var current in EnumerateParents(new DirectoryInfo(root)))
            {
                var repoCandidate = Path.Combine(current.FullName, "Aion2Flow.Resources", fileName);
                if (File.Exists(repoCandidate))
                {
                    return repoCandidate;
                }

                var directCandidate = Path.Combine(current.FullName, fileName);
                if (File.Exists(directCandidate))
                {
                    return directCandidate;
                }
            }
        }

        return fileName;
    }

    private static IEnumerable<DirectoryInfo> EnumerateParents(DirectoryInfo? start)
    {
        for (var current = start; current is not null; current = current.Parent)
        {
            yield return current;
        }
    }
}
