using System.Drawing;

namespace src.utils
{
    public enum Rarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary
    }

    public static class RarityManager
    {
        private static readonly object rarityLock = new();
        private static Dictionary<Rarity, float> rarityPercentages = new()
        {
            { Rarity.Common, 70f },
            { Rarity.Uncommon, 14f },
            { Rarity.Rare, 10f },
            { Rarity.Epic, 5f },
            { Rarity.Legendary, 1f }
        };

        private static Dictionary<Rarity, float> vipRarityPercentages = new()
        {
            { Rarity.Common, 55f },
            { Rarity.Uncommon, 23f },
            { Rarity.Rare, 14f },
            { Rarity.Epic, 7f },
            { Rarity.Legendary, 1f }
        };

        public static IReadOnlyDictionary<Rarity, float> RarityPercentages
        {
            get
            {
                lock (rarityLock)
                    return rarityPercentages.ToDictionary(k => k.Key, v => v.Value);
            }
        }

        public static void SetRarityPercentages(IDictionary<Rarity, float> percentages)
        {
            var table = Normalize(percentages);
            if (table == null) return;

            lock (rarityLock)
                rarityPercentages = table;
        }

        public static void SetVipRarityPercentages(IDictionary<Rarity, float> percentages)
        {
            var table = Normalize(percentages);
            if (table == null) return;

            lock (rarityLock)
                vipRarityPercentages = table;
        }

        // Accepts either percentages (70, 14, ...) or fractions (0.7, 0.14, ...); both are
        // rescaled so the table sums to 100.
        private static Dictionary<Rarity, float>? Normalize(IDictionary<Rarity, float> percentages)
        {
            if (percentages == null || percentages.Count == 0) return null;

            double sum = percentages.Values.Sum(v => (double)v);
            if (sum <= 0) return null;

            if (Math.Abs(sum - 100.0) <= 0.0001)
                return percentages.ToDictionary(k => k.Key, v => v.Value);

            var normalized = new Dictionary<Rarity, float>();
            foreach (var kv in percentages)
                normalized[kv.Key] = (float)((kv.Value / sum) * 100.0);

            return normalized;
        }

        public static float GetRarityPercentage(Rarity rarity)
        {
            lock (rarityLock)
                return rarityPercentages.TryGetValue(rarity, out var v) ? v : 0f;
        }

        public static (double, Rarity) RollRarity(bool vip = false)
        {
            double roll = Random.Shared.NextDouble() * 100.0;
            double accum = 0.0;

            lock (rarityLock)
            {
                var table = vip ? vipRarityPercentages : rarityPercentages;

                foreach (var r in Enum.GetValues(typeof(Rarity)).Cast<Rarity>())
                {
                    float chance = table.TryGetValue(r, out var val) ? val : 0f;
                    accum += chance;
                    if (roll <= accum)
                        return (roll, r);
                }
            }

            return (roll, Rarity.Common);
        }
    }
}
