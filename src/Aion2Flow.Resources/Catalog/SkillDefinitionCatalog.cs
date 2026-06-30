using System.Collections;

namespace Cloris.Aion2Flow.Resources.Catalog;

public sealed class SkillDefinitionCatalog(int capacity) : IReadOnlyCollection<SkillDefinition>
{
    private readonly List<SkillDefinition> _items = capacity > 0 ? new(capacity) : [];
    private readonly Dictionary<int, SkillDefinition> _bySkillId = capacity > 0 ? new(capacity) : [];

    public SkillDefinitionCatalog()
        : this(0)
    {
    }

    public int Count => _items.Count;

    public SkillDefinition this[int skillId] => _bySkillId[skillId];

    public void Add(SkillDefinition item)
    {
        _bySkillId.Add(item.SkillId, item);
        _items.Add(item);
    }

    public bool Contains(int skillId) => _bySkillId.ContainsKey(skillId);

    public bool TryGetValue(int skillId, out SkillDefinition skill) => _bySkillId.TryGetValue(skillId, out skill);

    public IEnumerator<SkillDefinition> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
