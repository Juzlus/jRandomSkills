using System.Text.Json.Serialization;

namespace RetakesPlugin.Configs;

public class BombSettings
{
    // Auto-plant creates the planted_c4 entity itself. On CS2 1.41.8.5 with CounterStrikeSharp 1.0.375 that
    // spawn crashes the server, so it is off by default; the planter gets the bomb instead.
    [JsonPropertyName("IsAutoPlantEnabled")]
    public bool IsAutoPlantEnabled { get; set; } = false;

    // When auto-plant is off, the planter's plant finishes the moment they start it.
    [JsonPropertyName("IsInstantPlantEnabled")]
    public bool IsInstantPlantEnabled { get; set; } = true;
}