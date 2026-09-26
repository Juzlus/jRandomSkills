using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

using RetakesPlugin.Utils;
using RetakesPluginShared.Enums;

namespace RetakesPlugin.Services;

public class AnnouncementService
{
    private readonly RetakesPlugin _plugin;
    private readonly Random _random;
    private readonly HashSet<CCSPlayerController> _hasMutedVoices;
    private readonly bool _voicesEnabled;
    private readonly bool _centerEnabled;
    private readonly bool _plantLocationEnabled;

    private static readonly string[] BombsiteAnnouncers =
    [
        "balkan_epic",
        "leet_epic",
        "professional_epic",
        "professional_fem",
        "seal_epic",
        "swat_epic",
        "swat_fem"
    ];

    public AnnouncementService(RetakesPlugin plugin, Random random, HashSet<CCSPlayerController> hasMutedVoices, bool voicesEnabled, bool centerEnabled, bool plantLocationEnabled)
    {
        _plugin = plugin;
        _random = random;
        _hasMutedVoices = hasMutedVoices;
        _voicesEnabled = voicesEnabled;
        _centerEnabled = centerEnabled;
        _plantLocationEnabled = plantLocationEnabled;
    }

    public void AnnouncePlantLocation(string? plantLocation, Bombsite bombsite)
    {
        if (!_plantLocationEnabled)
        {
            return;
        }

        // Fall back to the bombsite letter when the planter spawn has no callout name configured
        var locationName = string.IsNullOrWhiteSpace(plantLocation) ? bombsite.ToString() : plantLocation;

        var message = $"{_plugin.Localizer["retakes.prefix"]} {_plugin.Localizer["retakes.bombsite.plant_location", locationName]}";

        foreach (var player in Utilities.GetPlayers())
        {
            if (player.Team == CounterStrikeSharp.API.Modules.Utils.CsTeam.Terrorist)
            {
                player.PrintToChat(message);
            }
        }

        Logger.LogInfo("Announcement", $"Announced plant location: {locationName}");
    }

    public void AnnounceBombsite(Bombsite bombsite, bool onlyCenter = false)
    {
        var numTerrorist = PlayerHelper.GetPlayerCount(CounterStrikeSharp.API.Modules.Utils.CsTeam.Terrorist);
        var numCounterTerrorist = PlayerHelper.GetPlayerCount(CounterStrikeSharp.API.Modules.Utils.CsTeam.CounterTerrorist);

        var announcementMessage = _plugin.Localizer["retakes.bombsite.announcement", bombsite.ToString(), numTerrorist, numCounterTerrorist];
        var centerAnnouncementMessage = _plugin.Localizer["retakes.center.bombsite.announcement", bombsite.ToString(), numTerrorist, numCounterTerrorist];

        foreach (var player in Utilities.GetPlayers())
        {
            if (!onlyCenter)
            {
                player.PrintToChat($"{_plugin.Localizer["retakes.prefix"]} {announcementMessage}");

                if (_voicesEnabled && !_hasMutedVoices.Contains(player))
                {
                    var bombsiteAnnouncer = BombsiteAnnouncers[_random.Next(BombsiteAnnouncers.Length)];
                    player.ExecuteClientCommand($"play sounds/vo/agents/{bombsiteAnnouncer}/loc_{bombsite.ToString().ToLower()}_01");
                }

                continue;
            }

            if (!_centerEnabled)
            {
                continue;
            }

            if (player.Team == CounterStrikeSharp.API.Modules.Utils.CsTeam.CounterTerrorist)
            {
                player.PrintToCenter(centerAnnouncementMessage);
            }
        }

        Logger.LogInfo("Announcement", $"Announced bombsite {bombsite} ({numTerrorist}T vs {numCounterTerrorist}CT)");
    }
}