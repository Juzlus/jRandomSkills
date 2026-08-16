using CounterStrikeSharp.API.Modules.Utils;
using src.player;

namespace src.utils
{
    public static class DecoyTracker
    {
        private sealed class Entry
        {
            public required Skills Skill { get; init; }
            public required uint EntityId { get; init; }
            public required Vector Position { get; init; }
            public required uint Owner { get; init; }
        }

        private static readonly object gate = new();
        private static readonly List<Entry> entries = [];
        private static readonly Dictionary<Skills, Vector[]> positionCache = [];

        public static void Add(Skills skill, uint entityId, Vector position, uint owner)
        {
            lock (gate)
            {
                entries.RemoveAll(e => e.Skill == skill && e.EntityId == entityId);
                entries.Add(new Entry { Skill = skill, EntityId = entityId, Position = position, Owner = owner });
                positionCache.Remove(skill);
            }
        }

        public static void Remove(Skills skill, uint entityId)
        {
            lock (gate)
            {
                if (entries.RemoveAll(e => e.Skill == skill && e.EntityId == entityId) == 0) return;
                positionCache.Remove(skill);
            }

            DecoyRing.Hide(skill, entityId);
        }

        public static void RemoveOwner(Skills skill, uint owner)
        {
            List<uint> removed = [];

            lock (gate)
            {
                foreach (var entry in entries)
                    if (entry.Skill == skill && entry.Owner == owner)
                        removed.Add(entry.EntityId);

                if (removed.Count == 0) return;

                entries.RemoveAll(e => e.Skill == skill && e.Owner == owner);
                positionCache.Remove(skill);
            }

            foreach (var entityId in removed)
                DecoyRing.Hide(skill, entityId);
        }

        public static void Clear(Skills skill)
        {
            lock (gate)
            {
                entries.RemoveAll(e => e.Skill == skill);
                positionCache.Remove(skill);
            }

            DecoyRing.ClearAll(skill);
        }

        public static Vector[] Positions(Skills skill)
        {
            lock (gate)
            {
                if (positionCache.TryGetValue(skill, out var cached)) return cached;

                List<Vector> list = [];
                foreach (var entry in entries)
                    if (entry.Skill == skill)
                        list.Add(entry.Position);

                var result = list.ToArray();
                positionCache[skill] = result;
                return result;
            }
        }

        public static bool IsEmpty(Skills skill) => Positions(skill).Length == 0;
    }
}
