using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace RetakesSiteAnnounce;

// Written by CounterStrikeSharp to addons/counterstrikesharp/configs/plugins/RetakesSiteAnnounce/RetakesSiteAnnounce.json
// on first load; edit it there.
public class SiteAnnounceConfig : BasePluginConfig
{
    [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;

    // How long the site call stays on screen after the player spawns.
    [JsonPropertyName("Seconds")] public float Seconds { get; set; } = 5f;

    // The CTs are the ones retaking; the Ts spawn on the site and already know it.
    [JsonPropertyName("CtOnly")] public bool CtOnly { get; set; } = true;
    [JsonPropertyName("IncludeSpectators")] public bool IncludeSpectators { get; set; } = false;

    // Text per site. "\n" starts a new line.
    [JsonPropertyName("TextA")] public string TextA { get; set; } = "SITE A";
    [JsonPropertyName("TextB")] public string TextB { get; set; } = "SITE B";
    [JsonPropertyName("ColorA")] public string ColorA { get; set; } = "#FF5050";
    [JsonPropertyName("ColorB")] public string ColorB { get; set; } = "#50A0FF";

    // How the text follows the eyes (CS2-GameHUD methods):
    //   "Pawn"   - parented to the player and re-aimed from the view angles every tick while visible (GameHUD default).
    //   "Orient" - parented to a point_orient that turns with the eyes on the client; nothing to do per tick.
    [JsonPropertyName("Method")] public string Method { get; set; } = "Pawn";

    // Placement in front of the eyes, GameHUD / InfoTop convention: X right, Y up, Z distance (world units).
    [JsonPropertyName("PositionX")] public float PositionX { get; set; } = 0f;
    [JsonPropertyName("PositionY")] public float PositionY { get; set; } = 40f;
    [JsonPropertyName("PositionZ")] public float PositionZ { get; set; } = 80f;
    [JsonPropertyName("FontSize")] public float FontSize { get; set; } = 40f;
    [JsonPropertyName("UnitsPerPixel")] public float UnitsPerPixel { get; set; } = 0.25f;
    [JsonPropertyName("Font")] public string Font { get; set; } = "Arial Bold";
    // Dark box behind the text (border size in units); 0 draws no box.
    [JsonPropertyName("BackgroundBorder")] public float BackgroundBorder { get; set; } = 0.5f;

    // Show the call again when the bomb is planted (stock retakes repeats it after the plant).
    [JsonPropertyName("AnnounceOnPlant")] public bool AnnounceOnPlant { get; set; } = false;

    // Without a retakes plugin sending the bombsite event, read the site from the planted bomb instead.
    [JsonPropertyName("FallbackDetection")] public bool FallbackDetection { get; set; } = true;

    // Permission for !sitetest [A|B], which shows the banner to the caller for tuning the placement.
    [JsonPropertyName("TestCommandPermission")] public string TestCommandPermission { get; set; } = "@css/root";

    public SiteAnnounceConfig()
    {
        Version = 1;
    }
}
