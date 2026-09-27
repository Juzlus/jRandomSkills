using System.Text.Json.Serialization;

namespace RetakesPlugin.Configs;

public class BombSettings
{
    // true: the bomb is planted for the T side. Auto-plant spawns the planted bomb at freeze end; while
    // jRandomSkills blocks entity spawning (EntitySpawnSafety in config.json, CounterStrikeSharp behind the
    // CS2 build) the planter spawns with the bomb instead and their plant finishes on the first click.
    // false: the planter gets the bomb and plants it by hand like in a normal round.
    [JsonPropertyName("IsAutoPlantEnabled")]
    public bool IsAutoPlantEnabled { get; set; } = true;
}