using System.Collections;

namespace Cloris.Aion2Flow.Resources.Catalog;

public sealed class SkillDisplayCatalog(int capacity = 0) : IReadOnlyCollection<SkillDisplayEntry>
{
    private readonly List<SkillDisplayEntry> _items = capacity > 0 ? new List<SkillDisplayEntry>(capacity) : [];
    private readonly Dictionary<int, SkillDisplayEntry> _bySkillId = capacity > 0 ? new Dictionary<int, SkillDisplayEntry>(capacity) : [];

    public int Count => _items.Count;

    public SkillDisplayEntry this[int skillId] => _bySkillId[skillId];

    public void Add(SkillDisplayEntry item)
    {
        _bySkillId.Add(item.SkillId, item);
        _items.Add(item);
    }

    public bool Contains(int skillId) => _bySkillId.ContainsKey(skillId);

    public bool TryGetValue(int skillId, out SkillDisplayEntry skill) => _bySkillId.TryGetValue(skillId, out skill);

    public IEnumerator<SkillDisplayEntry> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
