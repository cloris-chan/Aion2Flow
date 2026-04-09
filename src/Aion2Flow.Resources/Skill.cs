using System;
using System.Collections.Generic;

namespace Cloris.Aion2Flow.Resources
{
public readonly record struct Skill(
    int Id,
    string Name,
    SkillCategory Category,
        SkillSourceType SourceType,
        string SourceKey,
    SkillKind Kind,
    SkillSemantics Semantics,
    string? TriggeredSkillIdsCsv)
    {
        public IEnumerable<int> EnumerateTriggeredSkillIds()
        {
            if (string.IsNullOrWhiteSpace(TriggeredSkillIdsCsv))
            {
                yield break;
            }

            foreach (var part in TriggeredSkillIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var id) && id > 0)
                {
                    yield return id;
                }
            }
        }
    }
}
