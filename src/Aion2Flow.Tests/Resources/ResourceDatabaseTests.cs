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

    [Fact]
    public void LoadSkills_Classifies_Periodic_Healing_From_Resource_Text()
    {
        var skills = ResourceDatabase.LoadSkills("en-US");

        Assert.True(skills.TryGetValue(10001, out var skill));
        Assert.Equal("Rest", skill.Name);
        Assert.Equal(SkillKind.PeriodicHealing, skill.Kind);
    }

    [Fact]
    public void LoadSkills_Backfills_ItemSkill_Metadata_From_SameName_Abnormal()
    {
        var skills = ResourceDatabase.LoadSkills("en-US");

        Assert.True(skills.TryGetValue(2011101, out var skill));
        Assert.Equal("Life Potion", skill.Name);
        Assert.Equal(SkillKind.PeriodicHealing, skill.Kind);
        Assert.True((skill.Semantics & SkillSemantics.PeriodicHealing) != 0);
    }

    [Fact]
    public void LoadSkills_Classifies_Shield_From_Resource_Text()
    {
        var skills = ResourceDatabase.LoadSkills("en-US");

        Assert.True(skills.TryGetValue(22120011, out var skill));
        Assert.Equal("Absorption Scroll", skill.Name);
        Assert.Equal(SkillKind.ShieldOrBarrier, skill.Kind);
    }

    [Fact]
    public void LoadSkills_Backfills_Shield_ItemSkill_Metadata_From_SameName_Abnormal()
    {
        var skills = ResourceDatabase.LoadSkills("en-US");

        Assert.True(skills.TryGetValue(2212001, out var skill));
        Assert.Equal("Absorption Scroll", skill.Name);
        Assert.Equal(SkillKind.ShieldOrBarrier, skill.Kind);
        Assert.True((skill.Semantics & SkillSemantics.ShieldOrBarrier) != 0);
    }

    [Fact]
    public void LoadSkills_Classifies_Drain_From_Resource_Text()
    {
        var skills = ResourceDatabase.LoadSkills("en-US");

        Assert.True(skills.TryGetValue(16046601, out var skill));
        Assert.Equal("Cry of Life", skill.Name);
        Assert.Equal(SkillKind.DrainOrAbsorb, skill.Kind);
    }

    [Fact]
    public void LoadSkills_Classifies_Mixed_Damage_And_Drain_Skill_As_Damage_Primary_Kind()
    {
        var skills = ResourceDatabase.LoadSkills("en-US");

        Assert.True(skills.TryGetValue(12240010, out var skill));
        Assert.Equal("Judgment", skill.Name);
        Assert.Equal(SkillKind.Damage, skill.Kind);
        Assert.True((skill.Semantics & SkillSemantics.Damage) != 0);
        Assert.True((skill.Semantics & SkillSemantics.DrainOrAbsorb) != 0);
    }

    [Fact]
    public void LoadSkills_Classifies_SameName_DoT_PcSkills_From_Backfilled_Abnormal_Text()
    {
        var skills = ResourceDatabase.LoadSkills("en-US");

        Assert.True(skills.TryGetValue(13730007, out var poison));
        Assert.Equal(SkillKind.PeriodicDamage, poison.Kind);
        Assert.True((poison.Semantics & SkillSemantics.PeriodicDamage) != 0);
        Assert.True((poison.Semantics & SkillSemantics.Support) != 0);
        Assert.False((poison.Semantics & SkillSemantics.Healing) != 0);
        Assert.False((poison.Semantics & SkillSemantics.PeriodicHealing) != 0);

        Assert.True(skills.TryGetValue(17070240, out var chainOfTorment));
        Assert.Equal(SkillKind.PeriodicDamage, chainOfTorment.Kind);
        Assert.True((chainOfTorment.Semantics & SkillSemantics.PeriodicDamage) != 0);
        Assert.True((chainOfTorment.Semantics & SkillSemantics.Support) != 0);

        Assert.True(skills.TryGetValue(17080240, out var debilitatingMark));
        Assert.Equal(SkillKind.PeriodicDamage, debilitatingMark.Kind);

        Assert.True(skills.TryGetValue(17300030, out var voiceOfDoom));
        Assert.Equal(SkillKind.PeriodicDamage, voiceOfDoom.Kind);
        Assert.False((voiceOfDoom.Semantics & SkillSemantics.ShieldOrBarrier) != 0);
    }

    [Theory]
    [InlineData(17121450, "痊癒光輝")]
    [InlineData(18121450, "痊癒咒語")]
    public void LoadSkills_Does_Not_Misclassify_Periodic_Group_Heals_As_PeriodicDamage(int skillId, string expectedName)
    {
        var skills = ResourceDatabase.LoadSkills("zh-TW");

        Assert.True(skills.TryGetValue(skillId, out var skill));
        Assert.Equal(expectedName, skill.Name);
        Assert.Equal(SkillKind.PeriodicHealing, skill.Kind);
        Assert.True((skill.Semantics & SkillSemantics.PeriodicHealing) != 0);
        Assert.False((skill.Semantics & SkillSemantics.PeriodicDamage) != 0);
    }

    [Fact]
    public void LoadSkills_Classifies_Bare_Offensive_PcSkill_Names_As_Damage()
    {
        var skills = ResourceDatabase.LoadSkills("en-US");

        Assert.True(skills.TryGetValue(14342350, out var tempestShot));
        Assert.Equal("Tempest Shot", tempestShot.Name);
        Assert.Equal(SkillKind.Damage, tempestShot.Kind);

        Assert.True(skills.TryGetValue(14130240, out var snareShot));
        Assert.Equal(SkillKind.Damage, snareShot.Kind);
    }

    [Fact]
    public void LoadSkills_Does_Not_Misclassify_ShieldSmite_As_Shield()
    {
        var skills = ResourceDatabase.LoadSkills("en-US");

        Assert.True(skills.TryGetValue(12100000, out var skill));
        Assert.Equal("Shield Smite", skill.Name);
        Assert.Equal(SkillKind.Damage, skill.Kind);
        Assert.True((skill.Semantics & SkillSemantics.Damage) != 0);
        Assert.False((skill.Semantics & SkillSemantics.ShieldOrBarrier) != 0);
    }

    [Fact]
    public void LoadSkills_Uses_LanguageInvariant_Classification_Metadata()
    {
        var englishSkills = ResourceDatabase.LoadSkills("en-US");
        var chineseSkills = ResourceDatabase.LoadSkills("zh-TW");

        Assert.True(englishSkills.TryGetValue(12100000, out var englishSkill));
        Assert.True(chineseSkills.TryGetValue(12100000, out var chineseSkill));

        Assert.Equal(SkillKind.Damage, englishSkill.Kind);
        Assert.Equal(englishSkill.Kind, chineseSkill.Kind);
        Assert.Equal(englishSkill.Semantics, chineseSkill.Semantics);
    }

    [Fact]
    public void LoadSkills_Exposes_MultiSignal_Semantics_For_Barrier_Skills()
    {
        var skills = ResourceDatabase.LoadSkills("en-US");

        Assert.True(skills.TryGetValue(18730000, out var protectionCircle));
        Assert.Equal(SkillKind.ShieldOrBarrier, protectionCircle.Kind);
        Assert.True((protectionCircle.Semantics & SkillSemantics.ShieldOrBarrier) != 0);
        Assert.True((protectionCircle.Semantics & SkillSemantics.Support) != 0);
    }

    [Fact]
    public void Skills_Table_Does_Not_Persist_Runtime_Semantic_Text_Columns()
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

        Assert.DoesNotContain("SummaryEnUs", columns);
        Assert.DoesNotContain("SummaryKoKr", columns);
        Assert.DoesNotContain("SummaryZhTw", columns);
        Assert.DoesNotContain("EffectEnUs", columns);
        Assert.DoesNotContain("EffectKoKr", columns);
        Assert.DoesNotContain("EffectZhTw", columns);
    }

    [Fact]
    public void LoadSkills_Exposes_NonHealth_Resource_Restore_Metadata()
    {
        var skills = ResourceDatabase.LoadSkills("zh-TW");

        Assert.True(skills.TryGetValue(13360010, out var skill));
        Assert.Equal("入侵", skill.Name);
        Assert.Equal(SkillKind.Damage, skill.Kind);
        Assert.True((skill.Semantics & SkillSemantics.Support) != 0);
        Assert.True((skill.Semantics & SkillSemantics.NonHealthResourceRestore) != 0);
    }

    [Fact]
    public void LoadSkills_Exposes_Direct_Healing_Metadata_For_Support_Buffs()
    {
        var skills = ResourceDatabase.LoadSkills("en-US");

        Assert.True(skills.TryGetValue(17410040, out var skill));
        Assert.Equal("Light of Protection", skill.Name);
        Assert.Equal(SkillKind.Support, skill.Kind);
        Assert.True((skill.Semantics & SkillSemantics.Support) != 0);
        Assert.True((skill.Semantics & SkillSemantics.Healing) != 0);
    }

    [Fact]
    public void LoadSkills_Backfills_OnAttack_Healing_Proc_Metadata_From_SameName_Abnormal()
    {
        var skills = ResourceDatabase.LoadSkills("zh-TW");

        Assert.True(skills.TryGetValue(18160030, out var sprintMantra));
        Assert.Equal("疾走真言", sprintMantra.Name);
        Assert.Equal(SkillKind.PeriodicHealing, sprintMantra.Kind);
        Assert.True((sprintMantra.Semantics & SkillSemantics.PeriodicHealing) != 0);
        Assert.True((sprintMantra.Semantics & SkillSemantics.Support) != 0);
    }

    [Fact]
    public void LoadSkills_Backfills_ProtectionCircle_Metadata_As_ShieldBarrier()
    {
        var skills = ResourceDatabase.LoadSkills("zh-TW");

        Assert.True(skills.TryGetValue(18730000, out var protectionCircle));
        Assert.Equal("保護陣", protectionCircle.Name);
        Assert.Equal(SkillKind.ShieldOrBarrier, protectionCircle.Kind);
        Assert.True((protectionCircle.Semantics & SkillSemantics.ShieldOrBarrier) != 0);
        Assert.True((protectionCircle.Semantics & SkillSemantics.Support) != 0);
    }

    [Theory]
    [InlineData(3000024, "神石：萊特曼的野心")]
    [InlineData(3000122, "神石：海格黛的聰明")]
    public void LoadSkills_Classifies_Damage_Theostones_As_Damage(int skillId, string expectedName)
    {
        var skills = ResourceDatabase.LoadSkills("zh-TW");

        Assert.True(skills.TryGetValue(skillId, out var skill));
        Assert.Equal(expectedName, skill.Name);
        Assert.Equal(SkillKind.Damage, skill.Kind);
        Assert.True((skill.Semantics & SkillSemantics.Damage) != 0);
    }

    [Theory]
    [InlineData("en-US", 11800008, "Murderous Burst")]
    [InlineData("zh-TW", 11800008, "殺氣破裂")]
    public void LoadSkills_Preserves_MultiSignal_Metadata_For_DirectDamage_Trigger_Siblings(string language, int skillId, string expectedName)
    {
        var skills = ResourceDatabase.LoadSkills(language);

        Assert.True(skills.TryGetValue(skillId, out var skill));
        Assert.Equal(expectedName, skill.Name);
        Assert.Equal(SkillKind.Damage, skill.Kind);
        Assert.True((skill.Semantics & SkillSemantics.Damage) != 0);
        Assert.True((skill.Semantics & SkillSemantics.Support) != 0);
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
