using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

using RetakesPlugin.Utils;
using RetakesPlugin.Managers;
using RetakesPlugin.Services;
using RetakesPlugin.Commands.SpawnEditor;
using RetakesPluginShared.Enums;
using RetakesPluginShared.Events;

namespace RetakesPlugin.Events;

public class RoundEventHandlers
{
    private readonly RetakesPlugin _plugin;
    private readonly GameManager _gameManager;
    private readonly SpawnManager _spawnManager;
    private readonly BreakerManager? _breakerManager;
    private readonly AllocationService _allocationService;
    private readonly AnnouncementService _announcementService;
    private readonly bool _isAutoPlantEnabled;
    private readonly bool _isInstantPlantEnabled;
    private readonly bool _isForceArmEnabled;
    private readonly bool _enableFallbackAllocation;
    private readonly bool _enableFallbackBombsiteAnnouncement;
    private readonly Random _random;
    private ShowSpawnsCommand? _showSpawnsCommand;

    private Bombsite _currentBombsite = Bombsite.A;
    private CCSPlayerController? _planter;
    private CsTeam _lastRoundWinner = CsTeam.None;
    private Bombsite? _forcedBombsite;

    public RoundEventHandlers(RetakesPlugin plugin, GameManager gameManager, SpawnManager spawnManager, BreakerManager? breakerManager, AllocationService allocationService, AnnouncementService announcementService, bool isAutoPlantEnabled, bool isInstantPlantEnabled, bool isForceArmEnabled, bool enableFallbackAllocation, bool enableFallbackBombsiteAnnouncement, Random random)
    {
        _plugin = plugin;
        _gameManager = gameManager;
        _spawnManager = spawnManager;
        _breakerManager = breakerManager;
        _allocationService = allocationService;
        _announcementService = announcementService;
        _isAutoPlantEnabled = isAutoPlantEnabled;
        _isInstantPlantEnabled = isInstantPlantEnabled;
        _isForceArmEnabled = isForceArmEnabled;
        _enableFallbackAllocation = enableFallbackAllocation;
        _enableFallbackBombsiteAnnouncement = enableFallbackBombsiteAnnouncement;
        _random = random;

        Logger.LogInfo("RoundEventHandlers", $"EnableFallbackAllocation inicializado a: {_enableFallbackAllocation}");
    }

    public void SetCommandReferences(ShowSpawnsCommand? showSpawnsCommand)
    {
        _showSpawnsCommand = showSpawnsCommand;
    }

    public void SetForcedBombsite(Bombsite? bombsite)
    {
        _forcedBombsite = bombsite;
    }

    public HookResult OnRoundPreStart(EventRoundPrestart @event, GameEventInfo info)
    {
        var gameRules = GameRulesHelper.GetGameRulesOrNull();
        if (gameRules == null)
        {
            Logger.LogDebug("Round", "Game rules not available yet, skipping pre-start logic");
            return HookResult.Continue;
        }

        if (gameRules.WarmupPeriod)
        {
            Logger.LogDebug("Round", "Warmup round, skipping pre-start logic");
            return HookResult.Continue;
        }

        _gameManager.QueueManager.ClearRoundTeams();

        Logger.LogDebug("Round", "Updating queues");
        _gameManager.QueueManager.DebugQueues(true);
        _gameManager.QueueManager.Update();
        _gameManager.QueueManager.DebugQueues(false);

        if (_gameManager.ShouldWaitForPlayers())
        {
            Logger.LogInfo("Round", "Not enough players to start a retake, waiting for players");
            _gameManager.StartWaitingForPlayers();
            return HookResult.Continue;
        }

        // Heal any stale waiting state, e.g. if an admin manually ran mp_warmup_end
        _gameManager.CancelWaitingForPlayers();

        _gameManager.OnRoundPreStart(_lastRoundWinner);
        _gameManager.QueueManager.SetRoundTeams();

        Logger.LogInfo("Round", "Round pre-start complete");
        return HookResult.Continue;
    }

    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        // Handle weird alive spectators bug
        var weirdAliveSpectators = Utilities.GetPlayers().Where(x => x is { TeamNum: < (int)CsTeam.Terrorist, PawnIsAlive: true });
        foreach (var weirdAliveSpectator in weirdAliveSpectators)
        {
            Server.ExecuteCommand("mp_autoteambalance 0");
            weirdAliveSpectator.CommitSuicide(false, true);
        }

        var gameRules = GameRulesHelper.GetGameRulesOrNull();
        if (gameRules == null)
        {
            Logger.LogDebug("Round", "Game rules not available yet, skipping round start logic");
            return HookResult.Continue;
        }

        if (gameRules.WarmupPeriod)
        {
            Logger.LogDebug("Round", "Warmup round, skipping.");
            if (_showSpawnsCommand?.ShowingSpawnsForBombsite != null && _plugin.MapConfigService != null)
            {
                SpawnService.ShowSpawns(_plugin, _plugin.MapConfigService.GetSpawnsClone(), _showSpawnsCommand.ShowingSpawnsForBombsite);
                Logger.LogDebug("Round", $"Re-showing spawns for bombsite {_showSpawnsCommand.ShowingSpawnsForBombsite}");
            }

            return HookResult.Continue;
        }

        if (_gameManager == null)
        {
            Logger.LogDebug("Round", "Game manager not loaded.");
            return HookResult.Continue;
        }

        if (_gameManager.IsWaitingForPlayers)
        {
            Logger.LogDebug("Round", "Waiting for players, skipping round start logic");
            return HookResult.Continue;
        }

        if (_spawnManager == null)
        {
            Logger.LogDebug("Round", "Spawn manager not loaded.");
            return HookResult.Continue;
        }

        _breakerManager?.Handle();
        _currentBombsite = _forcedBombsite ?? (_random.Next(0, 2) == 0 ? Bombsite.A : Bombsite.B);
        _gameManager.ResetPlayerScores();

        // Anyone who got onto a team without going through the queue (joined before the module
        // loaded, team menu bypassed, hot reload) would otherwise be left on the map's own spawns.
        _gameManager.QueueManager.SyncActivePlayersFromTeams();

        try
        {
            _planter = _spawnManager.HandleRoundSpawns(_currentBombsite, _gameManager.QueueManager.ActivePlayers);
            Logger.LogInfo("Round", $"Moved {_gameManager.QueueManager.ActivePlayers.Count(PlayerHelper.IsValid)} players to the {_currentBombsite} retakes spawns (planter: {_planter?.PlayerName ?? "none"})");
        }
        catch (Exception ex)
        {
            // Usually a map config without enough spawns for this many players on the chosen site.
            Logger.LogException("Round", ex);
            Server.PrintToChatAll($"{_plugin.Localizer["retakes.prefix"]} Not enough retakes spawns for bombsite {_currentBombsite}; add more with !showspawns / !addspawn.");
            _planter = null;
        }

        if (_enableFallbackBombsiteAnnouncement)
        {
            _announcementService.AnnounceBombsite(_currentBombsite);
        }

        RetakesPlugin.RetakesPluginEventSenderCapability.Get()?.TriggerEvent(new AnnounceBombsiteEvent(_currentBombsite));

        Logger.LogInfo("Round", $"Round started on bombsite {_currentBombsite}");
        return HookResult.Continue;
    }

    public HookResult OnRoundPostStart(EventRoundPoststart @event, GameEventInfo info)
    {
        var gameRules = GameRulesHelper.GetGameRulesOrNull();
        if (gameRules == null)
        {
            Logger.LogDebug("Round", "Game rules not available yet, skipping post-start logic");
            return HookResult.Continue;
        }

        if (gameRules.WarmupPeriod)
        {
            Logger.LogDebug("Round", "Warmup round, skipping post-start logic");
            return HookResult.Continue;
        }

        if (_gameManager.IsWaitingForPlayers)
        {
            Logger.LogDebug("Round", "Waiting for players, skipping post-start logic");
            return HookResult.Continue;
        }

        Logger.LogDebug("Round", $"EnableFallbackAllocation: {_enableFallbackAllocation}");

        foreach (var player in _gameManager.QueueManager.ActivePlayers.Where(PlayerHelper.IsValid))
        {
            if (!PlayerHelper.IsValid(player))
            {
                continue;
            }

            PlayerHelper.RemoveHelmetAndHeavyArmour(player);
            player.RemoveWeapons();

            if (player == _planter && !_isAutoPlantEnabled)
            {
                PlayerHelper.GiveAndSwitchToBomb(player);
            }

            if (_enableFallbackAllocation)
            {
                Logger.LogDebug("Round", $"Asignando armas a {player.PlayerName} (fallback allocation habilitado)");
                _allocationService.AllocatePlayer(player);
            }
            else
            {
                Logger.LogDebug("Round", $"No asignando armas a {player.PlayerName} (fallback allocation deshabilitado)");
            }
        }

        RetakesPlugin.RetakesPluginEventSenderCapability.Get()?.TriggerEvent(new AllocateEvent());

        Logger.LogInfo("Round", "Round post-start complete");
        return HookResult.Continue;
    }

    public HookResult OnRoundFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        var gameRules = GameRulesHelper.GetGameRulesOrNull();
        if (gameRules == null)
        {
            Logger.LogDebug("Round", "Game rules not available yet, skipping freeze end logic");
            return HookResult.Continue;
        }

        if (gameRules.WarmupPeriod)
        {
            Logger.LogDebug("Round", "Warmup round, skipping freeze end logic");
            return HookResult.Continue;
        }

        if (_gameManager.IsWaitingForPlayers)
        {
            Logger.LogDebug("Round", "Waiting for players, skipping freeze end logic");
            return HookResult.Continue;
        }

        if (PlayerHelper.GetPlayerCount(CsTeam.Terrorist) > 0)
        {
            HandleAutoPlant();
        }

        if (!_isAutoPlantEnabled && _planter != null && PlayerHelper.IsValid(_planter))
        {
            if (_isForceArmEnabled)
            {
                // Hands-free plant through the game's own code: the planter holds the C4, the plugin starts
                // its arming and lets it finish at once. Nothing is spawned by the plugin.
                var planter = _planter;
                Server.NextFrame(() => ForceArm(planter, 0));
            }
            else
            {
                PromptManualPlant(_planter);
            }
        }

        return HookResult.Continue;
    }

    private void PromptManualPlant(CCSPlayerController planter)
    {
        if (!PlayerHelper.IsValid(planter)) return;
        var plantText = AnnouncementService.StripColors(_plugin.Localizer["retakes.plant_now"]);
        src.utils.SkillUtils.ShowCenterNotice(planter, src.utils.SkillUtils.NoticeHtml(plantText), plantText, 8f);
        planter.PrintToChat($"{_plugin.Localizer["retakes.prefix"]} {_plugin.Localizer["retakes.plant_now"]}");
    }

    private const int ForceArmAttempts = 20; // 2 s at 0.1 s

    // Arms the planter's C4 as if they had started planting with E, with the arming time already over, so the
    // weapon's next think completes the plant. Retried for a short while (the C4 may not be the active weapon
    // on the first frame); if the game refuses (planter outside the bomb zone, dead, no C4) the planter is told
    // to plant by hand.
    private void ForceArm(CCSPlayerController planter, int attempt)
    {
        if (!PlayerHelper.IsValid(planter) || !PlayerHelper.IsConnected(planter) || !planter.PawnIsAlive) return;
        if (src.utils.PlayerManager.GetPlantedBomb() != null) return;

        var gameRules = GameRulesHelper.GetGameRulesOrNull();
        if (gameRules == null || gameRules.WarmupPeriod || gameRules.BombPlanted) return;

        var pawn = planter.PlayerPawn.Value;
        var weapon = pawn?.WeaponServices?.ActiveWeapon.Value;

        if (pawn != null && pawn.IsValid && weapon != null && weapon.IsValid && weapon.DesignerName == "weapon_c4")
        {
            var c4 = weapon.As<CC4>();
            if (!c4.StartedArming)
            {
                c4.IsPlantingViaUse = true;
                c4.StartedArming = true;
            }
            c4.ArmedTime = Server.CurrentTime;
        }
        else if (pawn != null && pawn.IsValid && planter.UserId != null)
        {
            // Bring the C4 up; it is the only weapon the planter can have been given with it.
            NativeAPI.IssueClientCommand((int)planter.UserId, "slot5");
        }

        if (attempt + 1 >= ForceArmAttempts)
        {
            Logger.LogWarning("Bomb", $"Arming {planter.PlayerName}'s C4 did not plant it (outside the bomb zone or no C4 in hand); they plant by hand this round.");
            PromptManualPlant(planter);
            return;
        }

        _plugin.AddTimer(0.1f, () => ForceArm(planter, attempt + 1));
    }

    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _lastRoundWinner = (CsTeam)@event.Winner;
        Logger.LogInfo("Round", $"Round ended. Winner: {_lastRoundWinner}");
        return HookResult.Continue;
    }

    public HookResult OnBombBeginPlant(EventBombBeginplant @event, GameEventInfo info)
    {
        if (_isAutoPlantEnabled || !_isInstantPlantEnabled)
        {
            return HookResult.Continue;
        }

        var player = @event.Userid;
        if (!PlayerHelper.IsValid(player))
        {
            return HookResult.Continue;
        }

        // The C4 sets its arming deadline when planting starts; move it to now so the plant completes on its next think.
        Server.NextFrame(() =>
        {
            if (!PlayerHelper.IsValid(player))
            {
                return;
            }

            var weapon = player!.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
            if (weapon == null || !weapon.IsValid || weapon.DesignerName != "weapon_c4")
            {
                return;
            }

            var c4 = weapon.As<CC4>();
            if (c4.StartedArming)
            {
                c4.ArmedTime = Server.CurrentTime;
            }
        });

        return HookResult.Continue;
    }

    public HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
    {
        Logger.LogInfo("Round", "Bomb planted");

        _announcementService.AnnouncePlantLocation(_spawnManager.CurrentPlanterSpawn?.PlantLocation, _currentBombsite);

        // The stock plugin repeats the site banner a few seconds after the plant; with the bomb planted at round
        // start that second banner only looked like a late first one, so it is off unless asked for.
        if (src.utils.Config.LoadedConfig.Modules.Retakes.RepeatSiteCallAfterPlant)
        {
            _plugin.AddTimer(4.1f, () =>
            {
                _announcementService.AnnounceBombsite(_currentBombsite, true);
            });
        }

        return HookResult.Continue;
    }

    public HookResult OnBombDefused(EventBombDefused @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (PlayerHelper.IsValid(player))
        {
            _gameManager.AddDefuse(player);
        }

        Logger.LogInfo("Round", $"Bomb defused by {player?.PlayerName ?? "unknown"}");
        return HookResult.Continue;
    }

    private void HandleAutoPlant()
    {
        if (!_isAutoPlantEnabled)
        {
            return;
        }

        if (_planter != null && PlayerHelper.IsValid(_planter))
        {
            try
            {
                BombService.PlantTickingBomb(_planter, _currentBombsite);
                Logger.LogInfo("Round", $"Auto-planted bomb at {_currentBombsite}");
            }
            catch (Exception ex)
            {
                // Let the planter plant normally instead of leaving the round without a bomb.
                Logger.LogException("Round", ex);
                PlayerHelper.GiveAndSwitchToBomb(_planter);
            }
        }
        else
        {
            Logger.LogWarning("Round", "No valid planter found, terminating round");
            GameRulesHelper.TerminateRound(CounterStrikeSharp.API.Modules.Entities.Constants.RoundEndReason.RoundDraw);
        }
    }
}