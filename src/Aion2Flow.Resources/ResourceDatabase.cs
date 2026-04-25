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
            SELECT Id, {nameColumn}, Category, SourceType, SourceKey, TriggeredSkillIdsCsv
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
            SELECT Id,
                   COALESCE(NameEnUs, NameZhTw, NameKoKr, CAST(Id AS TEXT)),
                   Category,
                   SourceType,
                   SourceKey,
                   TriggeredSkillIdsCsv
            FROM Skills
            WHERE Id IS NOT NULL
            """;

        return ReadSkills(cmd);
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
                (NpcCatalogKind)reader.GetByte(2));
            npcs[entry.Code] = entry;
        }

        return npcs;
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
