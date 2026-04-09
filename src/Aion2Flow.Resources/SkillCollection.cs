using System.Collections.ObjectModel;

namespace Cloris.Aion2Flow.Resources;

public class SkillCollection : KeyedCollection<int, Skill>
{
    protected override int GetKeyForItem(Skill item) => item.Id;
}