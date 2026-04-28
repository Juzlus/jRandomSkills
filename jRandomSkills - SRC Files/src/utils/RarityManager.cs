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
            { Rarity.Common, 40f },
            { Rarity.Uncommon, 25f },
            { Rarity.Rare, 20f },
            { Rarity.Epic, 10f },
            { Rarity.Legendary, 5f }
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
            if (percentages == null || percentages.Count == 0) return;

            lock (rarityLock)
            {
                double sum = percentages.Values.Sum(v => (double)v);
                if (sum <= 0)
                    return;

                if (Math.Abs(sum - 100.0) > 0.0001)
                {
                    var normalized = new Dictionary<Rarity, float>();

                    foreach (var kv in percentages)
                        normalized[kv.Key] = (float)((kv.Value / sum) * 100.0);

                    rarityPercentages = normalized;
                }
                else
                    rarityPercentages = percentages.ToDictionary(k => k.Key, v => v.Value);
            }
        }

        public static float GetRarityPercentage(Rarity rarity)
        {
            lock (rarityLock)
                return rarityPercentages.TryGetValue(rarity, out var v) ? v : 0f;
        }

        public static Rarity RollRarity()
        {
            var random = jRandomSkills.Instance?.Random ?? new Random();
            double roll = (random.NextDouble()) * 100.0;
            double accum = 0.0;

            lock (rarityLock)
            {
                foreach (var r in Enum.GetValues(typeof(Rarity)).Cast<Rarity>())
                {
                    accum += GetRarityPercentage(r);
                    if (roll <= accum)
                        return r;
                }
            }

            return Rarity.Common;
        }
    }
}
