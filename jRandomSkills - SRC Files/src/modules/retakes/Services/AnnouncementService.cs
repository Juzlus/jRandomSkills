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

    private static bool _screenFallbackNoted;

    // The call is kept pending for a while: at round start a player is often not on a team or not alive yet
    // (queue placement, late spawn) and the on-screen text needs a live pawn. A short timer then draws it for
    // every eligible player as soon as they are alive, once each.
    private Bombsite? _pendingSite;
    private DateTime _pendingUntil = DateTime.MinValue;
    private readonly HashSet<uint> _pendingShown = [];
    private CounterStrikeSharp.API.Modules.Timers.Timer? _pendingTimer;
    private const float PendingSeconds = 20f;

    private void StartPending(Bombsite bombsite)
    {
        _pendingSite = bombsite;
        _pendingUntil = DateTime.Now.AddSeconds(PendingSeconds);
        _pendingShown.Clear();

        _pendingTimer ??= _plugin.AddTimer(0.25f, PendingTick,
            CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT | CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void PendingTick()
    {
        if (_pendingSite == null || DateTime.Now > _pendingUntil)
        {
            _pendingSite = null;
            _pendingTimer?.Kill();
            _pendingTimer = null;
            return;
        }

        var retakes = src.utils.Config.LoadedConfig.Modules.Retakes;
        var site = _pendingSite.Value;
        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.IsBot) continue;
            if (_pendingShown.Contains(player.Index)) continue;
            if (retakes.SiteCallCtOnly && player.Team == CounterStrikeSharp.API.Modules.Utils.CsTeam.Terrorist) continue;
            if (player.Team is not (CounterStrikeSharp.API.Modules.Utils.CsTeam.CounterTerrorist or CounterStrikeSharp.API.Modules.Utils.CsTeam.Terrorist)) continue;

            if (ShowSiteBanner(player, site, BannerHtml(site), $"SITE {site}"))
                _pendingShown.Add(player.Index);
        }
    }

    // true: shown (or handed to the HUD). false: the on-screen text needs a live pawn and this player has none
    // yet; the pending loop tries again.
    private static bool ShowSiteBanner(CCSPlayerController player, Bombsite bombsite, string bannerHtml, string plainText)
    {
        var retakes = src.utils.Config.LoadedConfig.Modules.Retakes;
        float seconds = Math.Max(1f, retakes.SiteBannerSeconds);

        // Preferred: text drawn at the top of the player's screen, outside the centre HUD slot.
        if (retakes.SiteBannerStyle.Trim().Equals("Screen", StringComparison.OrdinalIgnoreCase))
        {
            var color = ParseColor(bombsite == Bombsite.A ? retakes.ScreenBannerColorA : retakes.ScreenBannerColorB, bombsite == Bombsite.A ? System.Drawing.Color.FromArgb(255, 80, 80) : System.Drawing.Color.FromArgb(80, 160, 255));
            var style = new src.utils.ScreenText.Style(retakes.ScreenBannerX, retakes.ScreenBannerY, retakes.ScreenBannerZ, retakes.ScreenBannerFontSize, retakes.ScreenBannerUnitsPerPx, retakes.ScreenBannerFont, retakes.ScreenBannerBackgroundBorder, retakes.ScreenBannerBackgroundBorder);
            if (src.utils.ScreenText.Show(player, $"SITE {bombsite}", color, style, seconds, src.utils.ScreenText.ParseMethod(retakes.ScreenBannerMethod)))
                return true;

            if (src.utils.ScreenText.Available)
            {
                var pawn = player.PlayerPawn.Value;
                bool alive = pawn != null && pawn.IsValid && pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE;
                if (!alive) return false; // not spawned yet: retried by the pending loop

                if (!_screenFallbackNoted)
                {
                    _screenFallbackNoted = true;
                    Logger.LogWarning("Announcement", "Screen text entity could not be created for an alive player; the HUD banner is used instead.");
                }
            }
            else if (!_screenFallbackNoted)
            {
                _screenFallbackNoted = true;
                Logger.LogWarning("Announcement", "Screen text is unavailable because entity spawning is blocked (EntitySpawnSafety.AllowScreenText=false); the HUD banner is used instead.");
            }
        }

        // Round-start alert in the centre box: only this banner, lifted towards the top of the screen, for a
        // few seconds; the skill HUD takes the box back afterwards.
        src.utils.SkillUtils.ShowCenterNotice(player, bannerHtml, plainText, seconds, exclusive: retakes.SiteBannerHidesSkill);
        return true;
    }

    private static System.Drawing.Color ParseColor(string hex, System.Drawing.Color fallback)
    {
        try
        {
            hex = hex.Trim().TrimStart('#');
            if (hex.Length == 6)
                return System.Drawing.Color.FromArgb(Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16));
        }
        catch { }
        return fallback;
    }

    // The banner is a picture when the config has an image URL for the site (same trick as CS2-CenterAdvert:
    // an <img> inside the centre HTML message), otherwise the big coloured "SITE A" text.
    private static string BannerHtml(Bombsite bombsite)
    {
        var retakes = src.utils.Config.LoadedConfig.Modules.Retakes;
        string image = (bombsite == Bombsite.A ? retakes.SiteImageA : retakes.SiteImageB)?.Trim() ?? "";

        // A picture only in the explicit "Image" style: it must be re-sent every tick to stay up (HUD traffic).
        // "Html" and the "Screen" fallback use plain text at the top of the HUD box, re-sent a few ticks apart.
        bool imageStyle = retakes.SiteBannerStyle.Trim().Equals("Image", StringComparison.OrdinalIgnoreCase);
        if (imageStyle && (image.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || image.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            return $"<img src='{image.Replace("'", "%27")}'/>";

        string siteColor = bombsite == Bombsite.A ? "#FF5050" : "#50A0FF";
        return $"<font class='fontWeight-Bold fontSize-l' color='{siteColor}'>▶  SITE {bombsite}  ◀</font>";
    }

    private static bool _standaloneNoted;
    private static bool _centerFlagNoted;

    // RetakesSiteAnnounce registers this convar while it is loaded.
    private static bool StandaloneSiteAnnounceLoaded()
    {
        bool loaded = CounterStrikeSharp.API.Modules.Cvars.ConVar.Find("retakes_site_announce_version") != null;
        if (loaded && !_standaloneNoted)
        {
            _standaloneNoted = true;
            Logger.LogInfo("Announcement", "RetakesSiteAnnounce is loaded: it draws the site call, the built-in banner stays off.");
        }
        return loaded;
    }

    // Chat colour codes are control characters; they show up as junk in a center alert.
    public static string StripColors(string text)
    {
        return new string(text.Where(c => c >= ' ').ToArray()).Trim();
    }

    public void AnnounceBombsite(Bombsite bombsite, bool onlyCenter = false)
    {
        var numTerrorist = PlayerHelper.GetPlayerCount(CounterStrikeSharp.API.Modules.Utils.CsTeam.Terrorist);
        var numCounterTerrorist = PlayerHelper.GetPlayerCount(CounterStrikeSharp.API.Modules.Utils.CsTeam.CounterTerrorist);

        var announcementMessage = _plugin.Localizer["retakes.bombsite.announcement", bombsite.ToString(), numTerrorist, numCounterTerrorist];
        var centerAnnouncementMessage = _plugin.Localizer["retakes.center.bombsite.announcement", bombsite.ToString(), numTerrorist, numCounterTerrorist];

        // The site call is a round-start alert in the centre HTML box: the box shows only this banner, lifted
        // towards the top of the screen, and the skill HUD takes the box back when it expires.
        var centerText = StripColors(centerAnnouncementMessage);
        var centerHtml = BannerHtml(bombsite);

        bool ctOnly = src.utils.Config.LoadedConfig.Modules.Retakes.SiteCallCtOnly;
        bool chatLine = src.utils.Config.LoadedConfig.Modules.Retakes.SiteCallChat;
        // The agent voice line plays only when both retakes.json and config.json allow it.
        bool voice = _voicesEnabled && src.utils.Config.LoadedConfig.Modules.Retakes.SiteCallVoice;

        // The standalone RetakesSiteAnnounce plugin draws the on-screen call itself (it listens to the same
        // bombsite event); with it loaded, or with SiteBannerStyle "Off", only the chat line and the voice stay.
        var retakesConfig = src.utils.Config.LoadedConfig.Modules.Retakes;
        bool standalone = retakesConfig.DeferToStandaloneSiteAnnounce && StandaloneSiteAnnounceLoaded();
        bool styleOff = retakesConfig.SiteBannerStyle.Trim().Equals("Off", StringComparison.OrdinalIgnoreCase);
        // One switch for the banner: Modules.Retakes.SiteBannerStyle in config.json. The old retakes.json flag
        // EnableBombsiteAnnouncementCenter no longer matters (a false there silently hid the call).
        bool showBanner = !standalone && !styleOff;
        string bannerState = showBanner ? "on"
            : standalone ? "off (RetakesSiteAnnounce draws it)"
            : "off (SiteBannerStyle=Off)";
        if (!_centerEnabled && showBanner && !_centerFlagNoted)
        {
            _centerFlagNoted = true;
            Logger.LogInfo("Announcement", "EnableBombsiteAnnouncementCenter=false in retakes.json is ignored; the banner follows SiteBannerStyle in config.json.");
        }

        if (showBanner && !onlyCenter)
            StartPending(bombsite);

        foreach (var player in Utilities.GetPlayers())
        {
            // The Ts spawn on the site; the call is for the CTs (and spectators) unless configured otherwise.
            if (ctOnly && player.Team == CounterStrikeSharp.API.Modules.Utils.CsTeam.Terrorist)
            {
                continue;
            }

            if (!onlyCenter)
            {
                if (chatLine)
                    player.PrintToChat($"{_plugin.Localizer["retakes.prefix"]} {announcementMessage}");

                if (showBanner && ShowSiteBanner(player, bombsite, centerHtml, centerText))
                {
                    _pendingShown.Add(player.Index);
                }

                if (voice && !_hasMutedVoices.Contains(player))
                {
                    // A "play" sent in the same frame as the round restart is dropped by the client now and then;
                    // a moment later it always goes through.
                    var bombsiteAnnouncer = BombsiteAnnouncers[_random.Next(BombsiteAnnouncers.Length)];
                    string command = $"play sounds/vo/agents/{bombsiteAnnouncer}/loc_{bombsite.ToString().ToLower()}_01";
                    var target = player;
                    _plugin.AddTimer(0.3f, () =>
                    {
                        if (PlayerHelper.IsValid(target) && PlayerHelper.IsConnected(target))
                            target.ExecuteClientCommand(command);
                    });
                }

                continue;
            }

            if (!showBanner)
            {
                continue;
            }

            ShowSiteBanner(player, bombsite, centerHtml, centerText);
        }

        Logger.LogInfo("Announcement", $"Announced bombsite {bombsite} ({numTerrorist}T vs {numCounterTerrorist}CT): CT only={ctOnly}, banner {bannerState}, chat={chatLine}, voice={voice}");
    }
}