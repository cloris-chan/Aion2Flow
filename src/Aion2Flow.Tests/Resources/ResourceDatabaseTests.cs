using Cloris.Aion2Flow.Resources;
using Microsoft.Data.Sqlite;

namespace Cloris.Aion2Flow.Tests.Resources;

public sealed class ResourceDatabaseTests
{
    [Fact]
    public void LoadNpcNames_Contains_Known_Npc_Resource_Key()
    {
        var npcNames = ResourceDatabase.LoadNpcNames("zh-TW", "M_L1_DH_1_MOB_BeritraD_03");

        Assert.True(npcNames.TryGetValue("M_L1_DH_1_MOB_BeritraD_03", out var npc));
        Assert.Equal("崇拜者德基許", npc.Name);
        Assert.Equal("M", npc.KeyPrefix);
        Assert.Equal("String_STR_M_L1_DH_1_MOB_BeritraD_03_body", npc.SourceKey);
    }

    [Fact]
    public void LoadNpcCatalog_Contains_Known_Numeric_Code()
    {
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");

        Assert.True(catalog.TryGetValue(2000002, out var npc));
        Assert.Equal("德拉克紐特弓手", npc.Name);
        Assert.Equal(NpcCatalogKind.Monster, npc.Kind);
    }

    [Fact]
    public void LoadNpcCatalog_Contains_Bridged_Current_Client_Entry()
    {
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");

        Assert.True(catalog.TryGetValue(2405210, out var npc));
        Assert.Equal("盜賊團掠奪者", npc.Name);
        Assert.Equal(NpcCatalogKind.Monster, npc.Kind);
    }

    [Fact]
    public void LoadNpcCatalog_Contains_Summon_Kind_Entry()
    {
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");

        Assert.True(catalog.TryGetValue(2920015, out var npc));
        Assert.Equal("結縛圈套", npc.Name);
        Assert.Equal(NpcCatalogKind.Summon, npc.Kind);
    }

    [Theory]
    [InlineData("en-US", 12240010, "Judgment")]
    [InlineData("zh-TW", 17121450, "痊癒光輝")]
    [InlineData("en-US", 11800008, "Murderous Burst")]
    public void LoadSkills_Exposes_Localized_Skill_Identity_Without_Runtime_Semantics(
        string language,
        int skillId,
        string expectedName)
    {
        var skills = ResourceDatabase.LoadSkills(language);

        Assert.True(skills.TryGetValue(skillId, out var skill));
        Assert.Equal(expectedName, skill.Name);
        Assert.NotEqual(SkillCategory.Unknown, skill.Category);
        Assert.NotEqual(SkillSourceType.Unknown, skill.SourceType);
    }

    [Fact]
    public void LoadCombatSkills_Exposes_Triggered_Sibling_Metadata()
    {
        var skills = ResourceDatabase.LoadCombatSkills();

        Assert.True(skills.TryGetValue(17040250, out var judgmentLightning));
        Assert.Contains(17050250, judgmentLightning.EnumerateTriggeredSkillIds());
    }

    [Theory]
    [InlineData(20u, "渾沌艾雷修藍塔下層")]
    [InlineData(22u, "渾沌艾雷修藍塔中層")]
    [InlineData(50u, "萬神殿")]
    [InlineData(1010u, "斐爾特朗")]
    [InlineData(200003u, "惡夢")]
    [InlineData(503001u, "深淵迴廊")]
    [InlineData(503006u, "深淵迴廊")]
    [InlineData(504006u, "深淵迴廊")]
    [InlineData(600002u, "克勞洞穴")]
    [InlineData(600011u, "烏努庫庫峽谷")]
    [InlineData(600091u, "凶猛的角岩窟")]
    [InlineData(600121u, "無之搖籃")]
    public void LoadMaps_Resolves_Client_Table_Scene_Id_Aliases(uint mapId, string expectedName)
    {
        var maps = ResourceDatabase.LoadMaps("zh-TW");

        Assert.Equal(expectedName, ResourceDatabase.ResolveMapName(mapId, maps));
    }

    [Fact]
    public void Maps_Table_Uses_Numeric_Map_Id_As_Runtime_Key()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ResolveDatabasePath(),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        }.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(Maps)";

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("MapId", columns);
        Assert.DoesNotContain("MapKey", columns);
    }

    [Fact]
    public void Skills_Table_Does_Not_Persist_Runtime_Semantic_Columns()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ResolveDatabasePath(),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        }.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(Skills)";

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("Id", columns);
        Assert.Contains("Category", columns);
        Assert.Contains("SourceType", columns);
        Assert.Contains("SourceKey", columns);
        Assert.DoesNotContain("Kind", columns);
        Assert.DoesNotContain("Semantics", columns);
        Assert.DoesNotContain("SummaryEnUs", columns);
        Assert.DoesNotContain("SummaryKoKr", columns);
        Assert.DoesNotContain("SummaryZhTw", columns);
        Assert.DoesNotContain("EffectEnUs", columns);
        Assert.DoesNotContain("EffectKoKr", columns);
        Assert.DoesNotContain("EffectZhTw", columns);
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
