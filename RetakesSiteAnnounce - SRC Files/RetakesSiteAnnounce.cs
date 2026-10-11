using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakesPluginShared;
using RetakesPluginShared.Enums;
using RetakesPluginShared.Events;

namespace RetakesSiteAnnounce;

// Site call for retakes: "SITE A" / "SITE B" drawn in front of each CT's eyes for a few seconds after they spawn.
// The text is a point_worldtext entity (see ScreenText), not a HUD message, so there is no per-tick user-message
// traffic. The bombsite comes from the retakes plugin through the RetakesPluginShared event API
// ("retakes_plugin:event_sender"); without it the planted bomb tells us the site.
public class RetakesSiteAnnounce : BasePlugin, IPluginConfig<SiteAnnounceConfig>
{
    public override string ModuleName => "RetakesSiteAnnounce";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "mbdevspace";
    public override string ModuleDescription => "Shows the retake bombsite as on-screen text (point_worldtext, no HUD messages) to the CTs when they spawn.";

    public SiteAnnounceConfig Config { get; set; } = new();

    private static readonly PluginCapability<IRetakesPluginEventSender> RetakesEvents = new("retakes_plugin:event_sender");

    // Lets other plugins see that this one draws the site call: the retakes module bundled in jRandomSkills skips its
    // own banner while this convar exists, so the call is never on screen twice.
    public FakeConVar<string> SiteAnnounceActive = new("retakes_site_announce_version", "RetakesSiteAnnounce is loaded; its version.", "1.0.0");

    private readonly ScreenText _screen = new();
    private IRetakesPluginEventSender? _sender;

    // Site of the current round and when it was called; players who spawn (or get their pawn) a moment later
    // still get the call for what is left of the window.
    private Bombsite? _roundSite;
    private DateTime _calledAt;
    private readonly Dictionary<uint, int> _shownGeneration = [];
    private int _generation;
    private bool _tickHooked;

    public void OnConfigParsed(SiteAnnounceConfig config)
    {
        if (config.Seconds < 0.5f) config.Seconds = 0.5f;
        if (config.FontSize <= 0f) config.FontSize = 40f;
        if (config.UnitsPerPixel <= 0f) config.UnitsPerPixel = 0.25f;
        if (config.PositionZ <= 0f) config.PositionZ = 80f;
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        RegisterFakeConVars(typeof(RetakesSiteAnnounce), this);

        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
        RegisterListener<Listeners.CheckTransmit>(OnCheckTransmit);

        RegisterEventHandler<EventRoundPrestart>(OnRoundPrestart);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect, HookMode.Pre);
        RegisterEventHandler<EventBombPlanted>(OnBombPlanted);

        AddCommand("css_sitetest", "Show the site banner to yourself: !sitetest [A|B]", OnSiteTestCommand);

        // The retakes plugin may load after us; keep trying at every round start until it is there.
        TryHookRetakes();

        Logger.LogInformation("[RetakesSiteAnnounce] Loaded. Method={Method}, {Seconds}s, CT only={CtOnly}, retakes events={Hooked}",
            Config.Method, Config.Seconds, Config.CtOnly, _sender != null ? "hooked" : "not available yet (will retry each round; planted bomb fallback is " + (Config.FallbackDetection ? "on" : "off") + ")");
    }

    public override void Unload(bool hotReload)
    {
        UnhookRetakes();
        if (_tickHooked)
        {
            RemoveListener<Listeners.OnTick>(OnTick);
            _tickHooked = false;
        }
        _screen.RemoveAll();
    }

    #region Retakes event API
    private void TryHookRetakes()
    {
        if (_sender != null) return;
        try
        {
            var sender = RetakesEvents.Get();
            if (sender == null) return;
            sender.RetakesPluginEventHandlers += OnRetakesEvent;
            _sender = sender;
            Logger.LogInformation("[RetakesSiteAnnounce] Hooked the retakes plugin event sender.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning("[RetakesSiteAnnounce] Retakes event sender not available: {Message}", ex.Message);
        }
    }

    private void UnhookRetakes()
    {
        if (_sender == null) return;
        try { _sender.RetakesPluginEventHandlers -= OnRetakesEvent; } catch { }
        _sender = null;
    }

    private void OnRetakesEvent(object? sender, IRetakesPluginEvent @event)
    {
        if (@event is AnnounceBombsiteEvent announce)
        {
            // Fired from the retakes round_start handler, right after the players were put on their spawns.
            Call(announce.Bombsite, "retakes");
        }
    }
    #endregion

    #region Game events
    private void OnMapStart(string mapName)
    {
        _screen.Clear();
        _shownGeneration.Clear();
        _roundSite = null;
    }

    private void OnMapEnd()
    {
        _screen.Clear();
        _shownGeneration.Clear();
        _roundSite = null;
    }

    // Reset here and not in round_start: the retakes plugin sends its bombsite event from ITS round_start
    // handler, and ours may run after it in the same event.
    private HookResult OnRoundPrestart(EventRoundPrestart @event, GameEventInfo info)
    {
        _roundSite = null;
        _shownGeneration.Clear();
        _screen.HideAll();
        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        TryHookRetakes();

        // No retakes event (plugin missing or disabled): a bomb already planted at round start tells the site.
        if (Config.FallbackDetection && _sender == null)
        {
            AddTimer(0.5f, () =>
            {
                if (_roundSite != null || IsWarmup()) return;
                var c4 = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault(c => c.IsValid);
                if (c4 != null)
                    Call(c4.BombSite == 1 ? Bombsite.B : Bombsite.A, "planted_c4");
            });
        }

        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _screen.HideAll();
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        // The pawn is not ready to carry entities in the spawn event itself; next frame it is.
        var index = player.Index;
        Server.NextFrame(() =>
        {
            var p = Utilities.GetPlayerFromIndex((int)index);
            if (p != null && p.IsValid) ShowIfPending(p);
        });
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid) _screen.Hide(player.Index);
        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid && !IsAudience((CsTeam)@event.Team)) _screen.Hide(player.Index);
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid)
        {
            _screen.Remove(player.Index);
            _shownGeneration.Remove(player.Index);
        }
        return HookResult.Continue;
    }

    private HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
    {
        if (IsWarmup()) return HookResult.Continue;

        Bombsite? site = null;
        var target = Utilities.GetEntityFromIndex<CBombTarget>(@event.Site);
        if (target != null && target.IsValid)
            site = target.IsBombSiteB ? Bombsite.B : Bombsite.A;

        if (_roundSite == null)
        {
            // Nothing called this round yet (no retakes plugin, or it plants by hand): the plant is the call.
            if (Config.FallbackDetection && site != null) Call(site.Value, "bomb_planted");
        }
        else if (Config.AnnounceOnPlant)
        {
            Call(site ?? _roundSite.Value, "bomb_planted (repeat)");
        }

        return HookResult.Continue;
    }

    private void OnCheckTransmit(CCheckTransmitInfoList infoList) => _screen.FilterTransmit(infoList);

    private void OnTick()
    {
        _screen.OnTick();
        if (!_screen.AnyVisible) UnhookTick();
    }
    #endregion

    #region Announce
    // New site call for everyone who should get it; late spawners pick it up in OnPlayerSpawn.
    private void Call(Bombsite site, string source)
    {
        if (!Config.Enabled) return;

        _roundSite = site;
        _calledAt = DateTime.UtcNow;
        _generation++;
        _shownGeneration.Clear();

        int shown = 0;
        foreach (var player in Utilities.GetPlayers())
            if (ShowIfPending(player)) shown++;

        // Pawns still being set up this frame pick the call up a moment later (or in OnPlayerSpawn).
        AddTimer(0.2f, () =>
        {
            foreach (var player in Utilities.GetPlayers())
                ShowIfPending(player);
        });

        Logger.LogInformation("[RetakesSiteAnnounce] Site {Site} from {Source}; shown to {Count} player(s) now, late spawners get it on spawn.", site, source, shown);
    }

    // Show the current round's call to this player if they have not had it yet and the window is still open.
    private bool ShowIfPending(CCSPlayerController player)
    {
        if (!Config.Enabled || _roundSite == null) return false;
        if (!player.IsValid || player.IsBot || player.IsHLTV) return false;
        if (!IsAudience(player.Team)) return false;
        if (_shownGeneration.TryGetValue(player.Index, out var gen) && gen == _generation) return false;

        float remaining = Config.Seconds - (float)(DateTime.UtcNow - _calledAt).TotalSeconds;
        if (remaining < 0.5f) return false;

        if (!Show(player, _roundSite.Value, remaining)) return false;
        _shownGeneration[player.Index] = _generation;
        return true;
    }

    private bool Show(CCSPlayerController player, Bombsite site, float seconds)
    {
        string text = (site == Bombsite.A ? Config.TextA : Config.TextB).Replace("\\n", "\n");
        var color = ParseColor(site == Bombsite.A ? Config.ColorA : Config.ColorB,
            site == Bombsite.A ? Color.FromArgb(255, 80, 80) : Color.FromArgb(80, 160, 255));
        var style = new ScreenText.Style(Config.PositionX, Config.PositionY, Config.PositionZ, Config.FontSize, Config.UnitsPerPixel, Config.Font, Config.BackgroundBorder);
        var method = ScreenText.ParseMethod(Config.Method);

        bool ok;
        try
        {
            ok = _screen.Show(player, text, color, style, method);
        }
        catch (Exception ex)
        {
            Logger.LogError("[RetakesSiteAnnounce] Could not create the screen text for {Player}: {Message}", player.PlayerName, ex.Message);
            return false;
        }
        if (!ok) return false;

        if (method == ScreenText.Method.Pawn) HookTick();

        // Hide when the window closes, unless a newer call replaced this one in the meantime.
        var index = player.Index;
        var generation = _generation;
        AddTimer(seconds, () =>
        {
            if (_generation == generation) _screen.Hide(index);
        });
        return true;
    }

    private bool IsAudience(CsTeam team)
    {
        if (team == CsTeam.CounterTerrorist) return true;
        if (team == CsTeam.Terrorist) return !Config.CtOnly;
        return Config.IncludeSpectators && team == CsTeam.Spectator;
    }

    private void HookTick()
    {
        if (_tickHooked) return;
        RegisterListener<Listeners.OnTick>(OnTick);
        _tickHooked = true;
    }

    private void UnhookTick()
    {
        if (!_tickHooked) return;
        RemoveListener<Listeners.OnTick>(OnTick);
        _tickHooked = false;
    }

    private static bool IsWarmup()
    {
        var proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        return proxy?.GameRules?.WarmupPeriod ?? false;
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            hex = hex.Trim().TrimStart('#');
            if (hex.Length == 6)
                return Color.FromArgb(Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16));
        }
        catch { }
        return fallback;
    }
    #endregion

    #region Commands
    // !sitetest [A|B]: show the banner to the caller for tuning the placement.
    private void OnSiteTestCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            command.ReplyToCommand("[RetakesSiteAnnounce] This command is for players.");
            return;
        }
        if (!string.IsNullOrWhiteSpace(Config.TestCommandPermission) && !AdminManager.PlayerHasPermissions(player, Config.TestCommandPermission))
        {
            command.ReplyToCommand("[RetakesSiteAnnounce] You don't have permission to use this command.");
            return;
        }

        var site = command.ArgCount > 1 && command.GetArg(1).Trim().Equals("B", StringComparison.OrdinalIgnoreCase) ? Bombsite.B : Bombsite.A;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
        {
            command.ReplyToCommand("[RetakesSiteAnnounce] You must be alive to see the banner.");
            return;
        }

        _generation++;
        bool ok = Show(player, site, Config.Seconds);
        command.ReplyToCommand(ok
            ? $"[RetakesSiteAnnounce] Showing SITE {site} for {Config.Seconds:0.#}s (Method={Config.Method}, X={Config.PositionX}, Y={Config.PositionY}, Z={Config.PositionZ}, font {Config.FontSize})."
            : "[RetakesSiteAnnounce] Could not create the text entity (see server console).");
    }
    #endregion
}
