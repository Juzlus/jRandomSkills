using System.Text.Json.Serialization;

namespace RetakesPlugin.Configs;

public class BombSettings
{
    // Auto-plant spawns the planted_c4 entity itself. While jRandomSkills blocks entity spawning
    // (EntitySpawnSafety in config.json) it falls back to giving the planter the bomb to plant normally.
    [JsonPropertyName("IsAutoPlantEnabled")]
    public bool IsAutoPlantEnabled { get; set; } = true;

    // Only used while auto-plant is unavailable: the planter's plant finishes the moment they start it,
    // so a single click plants the bomb.
    [JsonPropertyName("IsInstantPlantEnabled")]
    public bool IsInstantPlantEnabled { get; set; } = true;
}