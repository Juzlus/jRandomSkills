using System.Text.Json.Serialization;

namespace RetakesPlugin.Configs;

public class BombSettings
{
    // Auto-plant spawns the planted_c4 entity itself. While jRandomSkills blocks entity spawning
    // (EntitySpawnSafety in config.json) it falls back to giving the planter the bomb to plant normally.
    [JsonPropertyName("IsAutoPlantEnabled")]
    public bool IsAutoPlantEnabled { get; set; } = true;

    // When the planter plants by hand, finish the plant the moment they start it.
    [JsonPropertyName("IsInstantPlantEnabled")]
    public bool IsInstantPlantEnabled { get; set; } = false;
}