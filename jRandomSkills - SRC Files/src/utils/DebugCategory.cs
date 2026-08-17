using Newtonsoft.Json;

namespace src.utils
{
    [Flags]
    public enum DebugCategory
    {
        None = 0,
        Skill = 1 << 0,
        Round = 1 << 1,
        Entity = 1 << 2,
        Damage = 1 << 3,
        Core = 1 << 4,
    }

    public static class DebugCategories
    {
        public const int LegacyEnabledValue = 1234;

        public static DebugCategory Parse(int value)
        {
            if (value <= 0) return DebugCategory.None;

            DebugCategory flags = DebugCategory.None;
            for (int rest = value; rest > 0; rest /= 10)
            {
                flags |= (rest % 10) switch
                {
                    1 => DebugCategory.Skill,
                    2 => DebugCategory.Round,
                    3 => DebugCategory.Entity,
                    4 => DebugCategory.Damage,
                    _ => DebugCategory.None,
                };
            }

            if (flags != DebugCategory.None) flags |= DebugCategory.Core;
            return flags;
        }

        public static string Describe(DebugCategory flags)
        {
            if (flags == DebugCategory.None) return "Off";

            List<string> parts = [];
            if (flags.HasFlag(DebugCategory.Skill)) parts.Add("Skill");
            if (flags.HasFlag(DebugCategory.Round)) parts.Add("Round");
            if (flags.HasFlag(DebugCategory.Entity)) parts.Add("Entity");
            if (flags.HasFlag(DebugCategory.Damage)) parts.Add("Damage");

            return parts.Count == 0 ? "Off" : string.Join(" + ", parts);
        }
    }

    public sealed class DebugModeConverter : JsonConverter<int>
    {
        public override int ReadJson(JsonReader reader, Type objectType, int existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            return reader.TokenType switch
            {
                JsonToken.Boolean => (bool)reader.Value! ? DebugCategories.LegacyEnabledValue : 0,
                JsonToken.Integer => Convert.ToInt32(reader.Value),
                JsonToken.Float => Convert.ToInt32(reader.Value),
                JsonToken.String => int.TryParse((string?)reader.Value, out int parsed) ? parsed : 0,
                _ => 0,
            };
        }

        public override void WriteJson(JsonWriter writer, int value, JsonSerializer serializer)
        {
            writer.WriteValue(value);
        }
    }
}
