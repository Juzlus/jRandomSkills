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

    // "Auto" (default): a planted_c4 is created at freeze end while entity spawning is allowed (EntitySpawnSafety);
    //   while it is blocked the planter spawns holding the C4, the plugin tries to arm it so the game plants it,
    //   and failing that the planter is told to plant by hand (one click plants instantly).
    // "Entity": always create the planted_c4 (falls back like Auto while spawning is blocked).
    // "Weapon": never create an entity; arm the planter's C4 (the game may refuse without a held button).
    [JsonPropertyName("AutoPlantMethod")]
    public string AutoPlantMethod { get; set; } = "Auto";
}