using System.Collections.Generic;
using CMS21Together.Logic.Car.Parts;

namespace TogetherTestHarness.Features;

// PartScript.blockedNo is a counter: every mount adds one to the parts in unblockOnUnmount, every unmount takes one.
// The count of mounted blockers is reported next to it for diagnosis only: right after a car loads it already
// differs from the game's counter for a few parts.
public static class PartBlocking
{
    public static IEnumerable<PartScript> BlockedBy(PartScript part)
    {
        var seen = new HashSet<int>();
        if (part.unblockOnUnmount != null)
            foreach (var neighbour in part.unblockOnUnmount)
                if (neighbour != null && neighbour && seen.Add(neighbour.GetInstanceID())) yield return neighbour;
        if (part.unblockOnUnmountName != null)
            foreach (string name in part.unblockOnUnmountName)
            {
                var neighbour = string.IsNullOrEmpty(name) ? null : part.LookForPart(name);
                if (neighbour != null && neighbour && seen.Add(neighbour.GetInstanceID())) yield return neighbour;
            }
    }

    public static Dictionary<int, int> ExpectedCounts(PartRegistry registry)
    {
        var counts = new Dictionary<int, int>();
        foreach (string key in registry.SubKeys)
        {
            var part = registry.Sub(key);
            if (part == null || part.IsUnmounted) continue;
            foreach (var neighbour in BlockedBy(part))
            {
                int id = neighbour.GetInstanceID();
                counts[id] = (counts.TryGetValue(id, out int n) ? n : 0) + 1;
            }
        }
        return counts;
    }
}
