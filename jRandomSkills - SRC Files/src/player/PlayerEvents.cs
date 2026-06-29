using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using RayTraceAPI;
using src.player.skills;
using src.utils;
using System.Collections.Concurrent;
using static CounterStrikeSharp.API.Core.Listeners;
using static src.jRandomSkills;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player
{
    public static partial class Event
    {
        private static Timer? setSkillTimer = null;
        private static DateTime freezeTimeEnd = DateTime.MinValue;
        private static bool isTransmitRegistered = false;
        public static readonly jSkill_SkillInfo noneSkill = new(Skills.None, SkillsInfo.GetValue<string>(Skills.None, "color"), false);

        private static jSkill_SkillInfo ctSkill = noneSkill;
        private static jSkill_SkillInfo tSkill = noneSkill;
        private static jSkill_SkillInfo allSkill = noneSkill;
        private static List<jSkill_SkillInfo> debugSkills = [.. SkillData.Skills];

        public static readonly SkillsInfo.DefaultSkillInfo[] terroristSkills = [.. SkillsInfo.LoadedConfig.Where(s => s.OnlyTeam == (int)CsTeam.Terrorist)];
        public static readonly SkillsInfo.DefaultSkillInfo[] counterterroristSkills = [.. SkillsInfo.LoadedConfig.Where(s => s.OnlyTeam == (int)CsTeam.CounterTerrorist)];
        private static readonly SkillsInfo.DefaultSkillInfo[] allTeamsSkills = [.. SkillsInfo.LoadedConfig.Where(s => s.OnlyTeam == 0)];

        private static readonly ConcurrentDictionary<uint, ConcurrentBag<jSkill_SkillInfo>> playersSkills = [];
        public static readonly ConcurrentDictionary<uint, jSkill_SkillInfo> staticSkills = [];
        private static readonly object setLock = new();

        public static void Load()
        {
            Instance.RegisterEventHandler<EventPlayerConnectFull>(PlayerConnectFull);
            Instance.RegisterEventHandler<EventPlayerDisconnect>(PlayerDisconnect);
            // Instance.RegisterEventHandler<EventPlayerChat>(PlayerChat);
            Instance.RegisterEventHandler<EventPlayerSpawned>(PlayerSpawned);
            Instance.RegisterEventHandler<EventRoundStart>(RoundStart);
            Instance.RegisterEventHandler<EventRoundEnd>(RoundEnd);
            
            Instance.RegisterEventHandler<EventPlayerDeath>(PlayerDeath);
            Instance.RegisterEventHandler<EventPlayerBlind>(PlayerBlind);
            Instance.RegisterEventHandler<EventPlayerHurt>(PlayerHurt);
            Instance.RegisterEventHandler<EventPlayerJump>(PlayerJump);

            Instance.RegisterEventHandler<EventWeaponFire>(WeaponFire);
            Instance.RegisterEventHandler<EventItemEquip>(WeaponEquip);
            Instance.RegisterEventHandler<EventItemPickup>(WeaponPickup);
            Instance.RegisterEventHandler<EventWeaponReload>(WeaponReload);
            Instance.RegisterEventHandler<EventGrenadeThrown>(GrenadeThrown);

            Instance.RegisterEventHandler<EventBombBeginplant>(BombBeginplant);
            Instance.RegisterEventHandler<EventBombAbortplant>(BombAbortplant);
            Instance.RegisterEventHandler<EventBombPlanted>(BombPlanted);
            Instance.RegisterEventHandler<EventBombBegindefuse>(BombBegindefuse);

            Instance.RegisterEventHandler<EventDecoyStarted>(DecoyStarted);
            Instance.RegisterEventHandler<EventDecoyDetonate>(DecoyDetonate);

            Instance.RegisterEventHandler<EventSmokegrenadeDetonate>(SmokegrenadeDetonate);
            Instance.RegisterEventHandler<EventSmokegrenadeExpired>(SmokegrenadeExpired);

            Instance.RegisterListener<OnPlayerButtonsChanged>(CheckUseSkill);
            Instance.RegisterListener<OnEntitySpawned>(EntitySpawned);
            Instance.RegisterListener<OnTick>(OnTick);
            Instance.RegisterListener<OnClientPutInServer>(OnPlayerConnectedBot);

            Instance.HookUserMessage(208, PlayerMakeSound);
            VirtualFunctions.CBaseEntity_TakeDamageOldFunc.Hook(OnTakeDamage, HookMode.Pre);

            Instance.RegisterEventHandler<EventBulletImpact>(BulletImpact);

            VirtualFunctions.CBaseTrigger_StartTouchFunc.Hook(OnTriggerEnter, HookMode.Post);
            VirtualFunctions.CBaseTrigger_EndTouchFunc.Hook(OnTriggerExit, HookMode.Pre);
            VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Hook(OnWeaponCanAcquire, HookMode.Pre);

            // Disabled after CS2 updates started crashing Linux servers on player join.
            // The hooked native signature is only used to block weapon drops for Iana clones.
            // Keeping the plugin alive is safer than installing a stale global hook at load time.
        }

        private static jSkill_SkillInfo ChooseSkillByRarityAndMax(List<jSkill_SkillInfo> candidates, Dictionary<Skills, int> assignmentCounts, Config.GameModes gameMode)
        {
            if (candidates == null || candidates.Count == 0) return noneSkill;

            bool ignoreMax = gameMode == Config.GameModes.SameSkills || gameMode == Config.GameModes.TeamSkills;

            const int attempts = 6;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var (roll, rolled) = RarityManager.RollRarity();

                var filtered = candidates.Where(s =>
                {
                    if (s == null) return false;
                    var def = SkillsInfo.LoadedConfig.FirstOrDefault(d => d.Name == s.Skill.ToString());
                    if (def == null) return false;

                    if (!string.Equals(def.Rarity ?? string.Empty, rolled.ToString(), StringComparison.OrdinalIgnoreCase))
                        return false;

                    if (!ignoreMax && def.MaxPerServer >= 0)
                    {
                        int current = assignmentCounts.TryGetValue(s.Skill, out var c) ? c : 0;
                        if (current >= def.MaxPerServer) return false;
                    }

                    return true;
                }).ToList();

                if (filtered.Count > 0)
                    return filtered[Random.Shared.Next(filtered.Count)];
            }

            var fallback = candidates.Where(s =>
            {
                var def = SkillsInfo.LoadedConfig.FirstOrDefault(d => d.Name == s.Skill.ToString());
                if (def == null) return true;
                if (ignoreMax) return true;
                if (def.MaxPerServer < 0) return true;
                int current = assignmentCounts.TryGetValue(s.Skill, out var c) ? c : 0;
                return current < def.MaxPerServer;
            }).ToList();

            if (fallback.Count > 0)
                return fallback[Random.Shared.Next(fallback.Count)];

            return candidates[Random.Shared.Next(candidates.Count)];
        }

        private static void DispatchToActiveSkills(string methodName, params object[] args)
        {
            var seen = new HashSet<Skills>();
            foreach (var p in Instance.SkillPlayer)
            {
                if (p.IsDrawing || !seen.Add(p.Skill)) continue;
                Instance.SkillAction(p.Skill.ToString(), methodName, args);
            }
        }

        private static HookResult PlayerMakeSound(UserMessage um)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("PlayerMakeSound", um);
                return HookResult.Continue;
            }
        }

        private static HookResult WeaponFire(EventWeaponFire @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("WeaponFire", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult WeaponEquip(EventItemEquip @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("WeaponEquip", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult WeaponPickup(EventItemPickup @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("WeaponPickup", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult WeaponReload(EventWeaponReload @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("WeaponReload", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult GrenadeThrown(EventGrenadeThrown @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("GrenadeThrown", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult BombBeginplant(EventBombBeginplant @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("BombBeginplant", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult BombAbortplant(EventBombAbortplant @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("BombAbortplant", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult BombPlanted(EventBombPlanted @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("BombPlanted", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult BombBegindefuse(EventBombBegindefuse @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("BombBegindefuse", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult DecoyStarted(EventDecoyStarted @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("DecoyStarted", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult DecoyDetonate(EventDecoyDetonate @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("DecoyDetonate", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult SmokegrenadeDetonate(EventSmokegrenadeDetonate @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("SmokegrenadeDetonate", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult SmokegrenadeExpired(EventSmokegrenadeExpired @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("SmokegrenadeExpired", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult PlayerHurt(EventPlayerHurt @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("PlayerHurt", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult PlayerJump(EventPlayerJump @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("PlayerJump", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult PlayerBlind(EventPlayerBlind @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("PlayerBlind", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult OnTakeDamage(DynamicHook h)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("OnTakeDamage", h);

                if (Fortnite.skillInThisRound == true &&
                    !Instance.SkillPlayer.Any(p => !p.IsDrawing && p.Skill == Skills.Fortnite))
                    Instance.SkillAction("Fortnite", "OnTakeDamage", [h]);

                return HookResult.Continue;
            }
        }

        private static HookResult OnTriggerEnter(DynamicHook hook)
        {
            lock (setLock)
            {
                CBaseTrigger trigger = hook.GetParam<CBaseTrigger>(0);
                CBaseEntity entity = hook.GetParam<CBaseEntity>(1);

                DispatchToActiveSkills("OnTriggerEnter", trigger, entity);
                return HookResult.Continue;
            }
        }

        private static HookResult OnTriggerExit(DynamicHook hook)
        {
            lock (setLock)
            {
                CBaseTrigger trigger = hook.GetParam<CBaseTrigger>(0);
                CBaseEntity entity = hook.GetParam<CBaseEntity>(1);

                DispatchToActiveSkills("OnTriggerExit", trigger, entity);
                return HookResult.Continue;
            }
        }

        private static HookResult OnWeaponCanAcquire(DynamicHook hook)
        {
            lock (setLock)
            {
                CCSPlayer_ItemServices itemServices = hook.GetParam<CCSPlayer_ItemServices>(0);
                if (itemServices == null || itemServices.Pawn.Value == null || !itemServices.Pawn.Value.IsValid) return HookResult.Continue;

                CEconItemView econItem = hook.GetParam<CEconItemView>(1);
                if (econItem == null) return HookResult.Continue;

                CBasePlayerPawn pawn = itemServices.Pawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return HookResult.Continue;

                CCSPlayerController player = pawn.Controller.Value.As<CCSPlayerController>();
                if (player == null || !player.IsValid) return HookResult.Continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
                if (playerInfo == null) return HookResult.Continue;

                CCSWeaponBaseVData vdata = VirtualFunctions.GetCSWeaponDataFromKeyFunc.Invoke(-1, econItem.ItemDefinitionIndex.ToString());
                if (vdata == null) return HookResult.Continue;

                var activeSkills = Instance.SkillPlayer
                    .Where(p => !p.IsDrawing)
                    .Select(p => p.Skill.ToString())
                    .Distinct();

                bool block = false;
                foreach (string skillName in activeSkills)
                {
                    bool? result = (bool?)Instance.SkillAction(skillName, "OnWeaponCanAcquire", [hook, player, econItem, vdata]);
                    if (result == true)
                    {
                        block = true;
                        break;
                    }
                }

                return block ? HookResult.Handled : HookResult.Continue;
            }
        }

        private static HookResult WeaponDrop(DynamicHook hook)
        {
            lock (setLock)
            {
                CCSPlayerController player = hook.GetParam<CCSPlayerController>(0);
                if (player == null || !player.IsValid)
                    return HookResult.Continue;

                var activeSkills = Instance.SkillPlayer
                    .Where(p => !p.IsDrawing)
                    .Select(p => p.Skill.ToString())
                    .Distinct();

                bool block = false;
                foreach (string skillName in activeSkills)
                {
                    bool? result = (bool?)Instance.SkillAction(skillName, "WeaponDrop", [hook, player]);
                    if (result == true)
                    {
                        block = true;
                        break;
                    }
                }
                return block ? HookResult.Handled : HookResult.Continue;
            }
        }

        private static void OnTick()
        {
            lock (setLock)
            {
                var activeSkills = Instance.SkillPlayer
                    .Where(p => !p.IsDrawing)
                    .Select(p => p.Skill)
                    .Distinct()
                    .OrderBy(skill => skill.ToString() == "AreaReaper")
                    .ThenBy(skill => skill.ToString() == "ChillOut");

                foreach (var skill in activeSkills)
                    if (SkillsInfo.GetValue<bool>(skill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                            continue;
                        else
                            Instance.SkillAction(skill.ToString(), "OnTick");
            }
        }

        private static void OnPlayerConnectedBot(int playerSlot)
        {
            lock (setLock)
            {
                var player = Utilities.GetPlayerFromSlot(playerSlot);
                if (player == null || !player.IsValid) return;

                if (player.IsBot && !Config.LoadedConfig.EnableBotSkills)
                    return;

                var existing = Instance.SkillPlayer.FirstOrDefault(p => p.PlayerIndex == player.Index);
                if (existing != null)
                {
                    PlayerManager.Register(existing);
                    return;
                }

                var playerInfo = new jSkill_PlayerInfo
                {
                    IsBot = player.IsBot,
                    PlayerName = player.PlayerName,
                    PlayerIndex = player.Index,
                    Skill = Skills.None,
                    SpecialSkill = Skills.None,
                    IsDrawing = false,
                    SkillChance = 1,
                    PrintHTML = null,
                    DisplayHUD = true,
                    SkillUsed = false,
                };
                Instance.SkillPlayer.Add(playerInfo);
                PlayerManager.Register(playerInfo);
            }
        }

        private static HookResult PlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            lock (setLock)
            {
                var player = PlayerManager.GetPlayerEvent(@event.Userid);
                if (player == null || !player.IsValid) return HookResult.Continue;

                string welcomeMsg = player.GetTranslation("welcome_message", "welcome");
                foreach (string line in welcomeMsg.Split("\n"))
                    player.PrintToChat($" {ChatColors.Green}" + line.Replace("{PLAYER}", $" {ChatColors.Red}\u202A{player.PlayerName}\u202C{ChatColors.Green}", StringComparison.OrdinalIgnoreCase)
                                            .Replace("{SERVER_NAME}", $" {ChatColors.Red}{ConVar.Find("hostname")?.StringValue ?? "Default Server"}{ChatColors.Green}", StringComparison.OrdinalIgnoreCase)
                                            .Replace("{VERSION}", $" {ChatColors.Red}v{Instance.ModuleVersion}{ChatColors.Green}", StringComparison.OrdinalIgnoreCase)
                                            .Replace("{SKILLS_COUNT}", $" {ChatColors.Red}{SkillData.Skills.Count - 1}{ChatColors.Green}", StringComparison.OrdinalIgnoreCase)
                                            .Replace("{AUTHOR1}", $" {ChatColors.Red}Jakub Bartosik (D3X){ChatColors.Green} ({ChatColors.Red}https://github.com/jakubbartosik/dRandomSkills{ChatColors.Green})", StringComparison.OrdinalIgnoreCase)
                                            .Replace("{AUTHOR2}", $" {ChatColors.Red}Juzlus{ChatColors.Green} ({ChatColors.Red}https://github.com/Juzlus/jRandomSkills{ChatColors.Green})", StringComparison.OrdinalIgnoreCase));
                return HookResult.Continue;
            }
        }

        private static HookResult PlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            lock (setLock)
            {
                var player = PlayerManager.GetPlayerEvent(@event.Userid);
                if (player == null || !player.IsValid) return HookResult.Continue;

                var skillPlayer = PlayerManager.GetPlayerByIndex(player!.Index);
                if (skillPlayer == null) return HookResult.Continue;

                Instance.SkillAction(skillPlayer.Skill.ToString(), "DisableSkill", [player]);

                var items = Instance.SkillPlayer.ToList();
                Instance.SkillPlayer = [.. items.Where(p => p.PlayerIndex != player.Index)];
                PlayerManager.UnregisterPlayer(player.Index);
                EntityManager.DestroyPlayerEntities(player.Index);

                return HookResult.Continue;
            }
        }

        private static HookResult PlayerChat(EventPlayerChat @event, GameEventInfo info)
        {
            var userID = @event.Userid;
            if (userID == 0 || string.IsNullOrEmpty(@event.Text)) return HookResult.Continue;
  
            string text = @event.Text.Split(' ')[0].Trim();

            string commandName = text.StartsWith('!') || text.StartsWith('/') ? text[1..] : text;
            
            var player = Utilities.GetPlayerFromUserid(userID);
            if (player == null || !player.IsValid) return HookResult.Continue;

            var skillPlayer = PlayerManager.GetPlayerByIndex(player!.Index);
            if (skillPlayer == null) return HookResult.Continue;

            var temp = skillPlayer.SkillHudExpired;
            skillPlayer.SkillHudExpired = DateTime.MinValue;
;
            Instance.AddTimer(5f, () =>
            {
                if (skillPlayer.SkillHudExpired == DateTime.MinValue)
                    skillPlayer.SkillHudExpired = temp;
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

            return HookResult.Continue;
        }

        private static HookResult PlayerSpawned(EventPlayerSpawned @event, GameEventInfo info)
        {
            lock (setLock)
            {
                var player = PlayerManager.GetPlayerEvent(@event.Userid);
                if (player == null || !player.IsValid) return HookResult.Continue;

                var skillPlayer = PlayerManager.GetPlayerByIndex(player!.Index);
                if (skillPlayer == null) return HookResult.Continue;

                if (setSkillTimer != null)
                {
                    skillPlayer.IsDrawing = true;
                    return HookResult.Continue;
                }

                skillPlayer.IsDrawing = false;
                if (Instance?.GameRules != null &&
                    Instance?.GameRules.WarmupPeriod == false &&
                    skillPlayer.Skill == Skills.None &&
                    skillPlayer.SpecialSkill == Skills.None)
                    SetRandomSkill(player);
                return HookResult.Continue;
            }
        }

        private static HookResult RoundStart(EventRoundStart @event, GameEventInfo info)
        {
            lock (setLock)
            {
                bool isWarmup = Instance.GameRules != null && Instance.GameRules.WarmupPeriod == true;
                isTransmitRegistered = false;
                Instance.AddTimer(.1f, () => DisableAll(), CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

                foreach (var player in Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsHLTV && p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist))
                {
                    var skillPlayer = PlayerManager.GetPlayerByIndex(player!.Index);
                    if (skillPlayer == null) continue;
                    skillPlayer.IsDrawing = !isWarmup;
                    skillPlayer.PrintHTML = null;
                }

                Instance.RemoveListener<CheckTransmit>(CheckTransmit);
                int freezetime = ConVar.Find("mp_freezetime")?.GetPrimitiveValue<Int32>() ?? 0;
                freezeTimeEnd = DateTime.Now.AddSeconds(freezetime + (Instance?.GameRules?.TeamIntroPeriod == true ? 7 : 0));

                setSkillTimer?.Kill();

                float timeToDraw = (Instance?.GameRules?.TeamIntroPeriod == true ? 7 : 0) + Math.Max(freezetime - Config.LoadedConfig.SkillTimeBeforeStart, 0) + .3f;
                setSkillTimer = Instance?.AddTimer(timeToDraw, SetSkill, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                return HookResult.Continue;
            }
        }

        private static void DisableAll()
        {
            lock (setLock)
            {
                Fortnite.skillInThisRound = false;
                EntityManager.DestroyAllTracked();

                foreach (var player in Utilities.GetPlayers().Where(p => p != null && p.IsValid))
                {
                    if (player == null || !player.IsValid) continue;

                    var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                    if (playerInfo == null) continue;

                    Instance.SkillAction(playerInfo.Skill.ToString(), "DisableSkill", [player]);

                    playerInfo.Skill = noneSkill.Skill;
                    playerInfo.SpecialSkill = noneSkill.Skill;
                    playerInfo.PrintHTML = null;
                    playerInfo.SkillChance = 1;
                    playerInfo.SkillUsed = false;

                    RestorePlayer(player);
                }

                foreach (var skill in SkillData.Skills)
                    Instance.SkillAction(skill.Skill.ToString(), "NewRound");
            }
        }

        public static void RestorePlayer(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid) return;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid) return;

            pawn.HideHUD = (uint)(pawn.HideHUD & ~(1 << 8));
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_iHideHUD");

            player.ReplicateConVar("sv_disable_radar", "0");

            player.DesiredFOV = 0;
            Utilities.SetStateChanged(player, "CBasePlayerController", "m_iDesiredFOV");
        }

        public static void OnMapChange()
        {
            lock (setLock)
            {
                isTransmitRegistered = false;
                Instance.RemoveListener<CheckTransmit>(CheckTransmit);

                Fortnite.skillInThisRound = false;
                EntityManager.DestroyAllTracked();
                foreach (var skill in SkillData.Skills)
                    Instance.SkillAction(skill.Skill.ToString(), "NewRound");

                playersSkills.Clear();
                staticSkills.Clear();

                ctSkill = noneSkill;
                tSkill = noneSkill;
                allSkill = noneSkill;

                Instance.SkillPlayer.Clear();
                PlayerManager.Clear();

                ConVar.Find("sv_legacy_jump")?.SetValue("1");
            }
        }

        private static HookResult RoundEnd(EventRoundEnd @event, GameEventInfo info)
        {
            Illiterate.Disable();

            lock (setLock)
            {
                Instance.AddTimer(.5f, () =>
                {
                    if (!Config.LoadedConfig.SummaryAfterTheRound) return;

                    var _players = Utilities.GetPlayers().Where(p => p.IsValid && p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist).OrderBy(p => p.Team).ToList();

                    foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid))
                    {
                        string skillsText = "";
                        foreach (var _player in _players)
                        {
                            var _playerSkill = PlayerManager.GetPlayerByIndex(_player.Index);
                            if (_playerSkill == null) continue;

                            var skillInfo = SkillData.Skills.FirstOrDefault(s => s.Skill == _playerSkill.Skill);
                            var specialSkillInfo = SkillData.Skills.FirstOrDefault(s => s.Skill == _playerSkill.SpecialSkill);
                            if (skillInfo == null) continue;

                            skillsText += $" {ChatColors.DarkRed}\u202A{_player.PlayerName}\u202C{ChatColors.Lime}: {(_playerSkill.SpecialSkill == Skills.None || specialSkillInfo == null ? player.GetSkillName(skillInfo.Skill, _playerSkill.SkillChance) : $"{player.GetSkillName(specialSkillInfo.Skill)} -> {player.GetSkillName(skillInfo.Skill, _playerSkill.SkillChance)}")}\n";
                        }

                        if (string.IsNullOrEmpty(skillsText)) continue;

                        SkillUtils.PrintToChat(player, string.Empty, title: player.GetTranslation("summary"), border: "t");
                        foreach (string text in skillsText.Split("\n"))
                            if (!string.IsNullOrEmpty(text))
                                SkillUtils.PrintToChat(player, text, title: player.GetTranslation("teammate_skills"), border: "");
                        SkillUtils.PrintToChat(player, string.Empty, border: "b");
                    }
                }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

                if (Config.LoadedConfig.DisableSkillsOnRoundEnd)
                {
                    isTransmitRegistered = false;
                    Instance.AddTimer(1f, () => DisableAll(), CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                    Instance.RemoveListener<CheckTransmit>(CheckTransmit);
                }
                return HookResult.Continue;
            }
        }

        private static HookResult PlayerDeath(EventPlayerDeath @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("PlayerDeath", @event);

                var victim = PlayerManager.GetPlayerEvent(@event.Userid);
                if (victim == null) return HookResult.Continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(victim.Index);
                if (playerInfo == null || playerInfo.IsDrawing) return HookResult.Continue;
                Instance.SkillAction(playerInfo.Skill.ToString(), "DisableSkill", [victim]);

                var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
                if (attacker == null || victim == attacker) return HookResult.Continue;
                if (Config.LoadedConfig.KillerSkillChatInfo)
                {
                    var attackerInfo = PlayerManager.GetPlayerByIndex(attacker!.Index);
                    if (attackerInfo != null)
                    {
                        var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == attackerInfo.Skill);
                        var specialSkillData = SkillData.Skills.FirstOrDefault(s => s.Skill == attackerInfo.SpecialSkill);
                        if (skillData == null || specialSkillData == null) return HookResult.Continue;
                        string skillDesc = victim.GetSkillDescription(skillData.Skill);

                        SkillUtils.PrintToChat(victim, 
                            $"{ChatColors.DarkRed}{(attackerInfo.SpecialSkill == Skills.None ? victim.GetSkillName(skillData.Skill) : $"{victim.GetSkillName(specialSkillData.Skill)} -> {victim.GetSkillName(skillData.Skill)}")}{ChatColors.Lime} - {skillDesc}",
                            title: $"{victim.GetTranslation("enemy_skill")} {ChatColors.DarkRed}\u202A{attacker.PlayerName}\u202C{ChatColors.Lime}");
                    }
                }
                return HookResult.Continue;
            }
        }

        private static void CheckUseSkill(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            lock (setLock)
            {
                string? button = Config.LoadedConfig.AlternativeSkillButton;
                if (string.IsNullOrEmpty(button) || button.Length < 2) return;

                string buttonName = $"{char.ToUpperInvariant(button[0])}{button[1..].ToLowerInvariant()}";
                if (!Enum.TryParse<PlayerButtons>(buttonName, out var skillButton)) return;

                if ((pressed & skillButton) == 0) return;

                if (SkillUtils.HasMenu(player)) return;

                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo == null || playerInfo.IsDrawing) return;

                if (SkillsInfo.GetValue<bool>(playerInfo.Skill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                    return;

                if (skillButton == PlayerButtons.Use)
                {
                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid) return;
                    if (pawn.AbsOrigin == null || pawn.AbsRotation == null) return;

                    if (pawn.IsDefusing) return;

                    Vector eyePos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
                    Vector endPos = eyePos + SkillUtils.GetForwardVector(pawn.EyeAngles) * 80;

                    ulong mask = (ulong)(InteractionLayers.MASK_WORLD_ONLY | InteractionLayers.Player | InteractionLayers.NPC);
                    ulong contents = 0;
                    var result = RayTrace.TraceShape(player, eyePos, endPos, mask, contents);

                    if (result.HasValue && result.Value.DidHit)
                    {
                        var entity = Activator.CreateInstance(typeof(CBaseEntity), result.Value.HitEntity) as CBaseEntity;
                        if (entity == null || !entity.IsValid) return;

                        string designer = entity.DesignerName;
                        if (designer.Contains("door") || designer.Contains("button") || designer.Contains("weapon") || designer.Contains("blocker")) return;
                    }
                }

                Debug.WriteToDebug($"Player {player.PlayerName} used the skill: {playerInfo.Skill} by PlayerButtons: {pressed}");
                Instance.SkillAction(playerInfo.Skill.ToString(), "UseSkill", [player]);
            }
        }

        private static void EntitySpawned(CEntityInstance entity)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("OnEntitySpawned", entity);
            }
        }

        private static HookResult BulletImpact(EventBulletImpact @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("BulletImpact", @event);
                return HookResult.Continue;
            }
        }

        private static void SetSkill()
        {
            setSkillTimer = null;
            lock (setLock)
            {
                if (Instance == null) return;

                if (Instance.GameRules != null && Instance.GameRules.WarmupPeriod == true)
                {
                    setSkillTimer?.Kill();
                    return;
                }

                var validPlayers = Utilities.GetPlayers()
                    .Where(p => p != null && p.IsValid && !p.IsHLTV)
                    .Where(p => {
                        try { return p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist; }
                        catch { return false; }
                    }).ToList();

                if (Config.LoadedConfig.GameMode == (int)Config.GameModes.TeamSkills)
                {
                    List<jSkill_SkillInfo> tSkills = [.. SkillData.Skills];
                    tSkills.RemoveAll(s => s.Skill == tSkill.Skill || s.Skill == Skills.None || counterterroristSkills.Any(s2 => s2.Name == s.Skill.ToString()));
                    tSkill = tSkills.Count == 0 ? noneSkill : tSkills[Instance.Random.Next(tSkills.Count)];

                    List<jSkill_SkillInfo> ctSkills = [.. SkillData.Skills];
                    ctSkills.RemoveAll(s => s.Skill == ctSkill.Skill || s.Skill == Skills.None || terroristSkills.Any(s2 => s2.Name == s.Skill.ToString()));
                    ctSkill = ctSkills.Count == 0 ? noneSkill : ctSkills[Instance.Random.Next(ctSkills.Count)];
                }
                else if (Config.LoadedConfig.GameMode == (int)Config.GameModes.SameSkills)
                {
                    List<jSkill_SkillInfo> allSkills = [.. SkillData.Skills];
                    allSkills.RemoveAll(s => s.Skill == allSkill.Skill || s.Skill == Skills.None || !allTeamsSkills.Any(s2 => s2.Name == s.Skill.ToString()));
                    allSkill = allSkills.Count == 0 ? noneSkill : allSkills[Instance.Random.Next(allSkills.Count)];
                }
                else if (Config.LoadedConfig.GameMode == (int)Config.GameModes.Debug && debugSkills.Count == 0)
                    debugSkills = [.. SkillData.Skills];

                Dictionary<Skills, int> assignmentCounts = new();
                foreach (var sp in Instance.SkillPlayer)
                {
                    if (sp == null) continue;
                    if (assignmentCounts.TryGetValue(sp.Skill, out var cnt)) assignmentCounts[sp.Skill] = cnt + 1;
                    else assignmentCounts[sp.Skill] = 1;
                }

                foreach (var player in validPlayers)
                {
                    if (player == null) continue;
                    var teammates = validPlayers.Where(p => p != null && p.IsValid && p.Team == player.Team && p != player).ToList();
                    string teammateSkills = "";

                    var skillPlayer = PlayerManager.GetPlayerByIndex(player!.Index);
                    if (skillPlayer == null) continue;

                    skillPlayer.IsDrawing = false;
                    if (player.PlayerPawn.Value == null || !player.PlayerPawn.IsValid)
                    {
                        skillPlayer.Skill = Skills.None;
                        continue;
                    }

                    jSkill_SkillInfo randomSkill = noneSkill;

                    Config.GameModes gameMode = (Config.GameModes)Config.LoadedConfig.GameMode;
                    if (gameMode == Config.GameModes.Normal || gameMode == Config.GameModes.FullRandom || gameMode == Config.GameModes.NoRepeat)
                    {
                        List<jSkill_SkillInfo> skillList = [.. SkillData.Skills];
                        skillList.RemoveAll(s => s?.Skill == Skills.None);
                        if (!player.IsBot)
                            skillList.RemoveAll(s => !string.IsNullOrEmpty(SkillsInfo.GetValue<string>(s.Skill, "requiredPermission")) && !AdminManager.PlayerHasPermissions(player, SkillsInfo.GetValue<string>(s.Skill, "requiredPermission")));

                        if (gameMode != Config.GameModes.FullRandom)
                            skillList.RemoveAll(s => s?.Skill == skillPlayer?.Skill || s?.Skill == skillPlayer?.SpecialSkill);

                        if (validPlayers.Count(p => p.Team == player.Team) == 1)
                        {
                            SkillsInfo.DefaultSkillInfo[] skillsNeedsTeammates = [.. SkillsInfo.LoadedConfig.Where(s => s.NeedsTeammates)];
                            skillList.RemoveAll(s => skillsNeedsTeammates.Any(s2 => s2.Name == s.Skill.ToString()));
                        }

                        if (player.Team == CsTeam.Terrorist)
                            skillList.RemoveAll(s => counterterroristSkills.Any(s2 => s2.Name == s.Skill.ToString()));
                        else
                            skillList.RemoveAll(s => terroristSkills.Any(s2 => s2.Name == s.Skill.ToString()));

                        if (gameMode == Config.GameModes.NoRepeat && playersSkills.TryGetValue(player.Index, out ConcurrentBag<jSkill_SkillInfo>? skills))
                        {
                            skillList.RemoveAll(s => skills.Any(s2 => s2.Skill == s.Skill));
                            if (skillList.Count == 0) skills.Clear();
                        }

                        randomSkill = skillList.Count == 0 ? noneSkill : ChooseSkillByRarityAndMax(skillList, assignmentCounts, gameMode);

                        if (gameMode == Config.GameModes.NoRepeat)
                        {
                            if (playersSkills.TryGetValue(player.Index, out ConcurrentBag<jSkill_SkillInfo>? value))
                                value.Add(randomSkill);
                            else
                                playersSkills.TryAdd(player.Index, [randomSkill]);
                        }
                    }
                    else if (gameMode == Config.GameModes.TeamSkills)
                        randomSkill = player.Team == CsTeam.Terrorist ? tSkill : ctSkill;
                    else if (gameMode == Config.GameModes.SameSkills)
                        randomSkill = allSkill;
                    else if (gameMode == Config.GameModes.Debug)
                    {
                        if (debugSkills.Count == 0)
                            debugSkills = [.. SkillData.Skills];
                        randomSkill = debugSkills[0];
                        debugSkills.RemoveAt(0);
                        player.PrintToChat($"{SkillData.Skills.Count - debugSkills.Count}/{SkillData.Skills.Count}");
                    }

                    Instance?.SkillAction(skillPlayer.Skill.ToString(), "DisableSkill", [player]);
                    skillPlayer.Skill = randomSkill.Skill;
                    skillPlayer.SpecialSkill = Skills.None;

                    if (randomSkill.Skill != Skills.None)
                    {
                        if (assignmentCounts.TryGetValue(randomSkill.Skill, out var cnt)) assignmentCounts[randomSkill.Skill] = cnt + 1;
                        else assignmentCounts[randomSkill.Skill] = 1;
                    }

                    if (randomSkill.Skill == Skills.Illiterate)
                        Illiterate.Enable();

                    Instance?.AddTimer(.2f, () =>
                    {
                        if(player == null || !player.IsValid) return;

                        if (randomSkill.Display)
                            SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(randomSkill.Skill)}{ChatColors.Lime}: {player.GetSkillDescription(randomSkill.Skill)}",
                                border: !Utilities.GetPlayers().Any(p => p != null && p.IsValid && p.Team == player.Team && p != player) ? "tb" : "t");

                        if (SkillsInfo.GetValue<bool>(randomSkill.Skill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                            Instance?.AddTimer(Config.LoadedConfig.SkillTimeBeforeStart, () =>
                            {
                                if (PlayerManager.GetPlayerByIndex(player!.Index)?.Skill != randomSkill.Skill) return;
                                Instance?.SkillAction(randomSkill.Skill.ToString(), "EnableSkill", [player]);
                            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                        else
                        {
                            Instance?.SkillAction(randomSkill.Skill.ToString(), "EnableSkill", [player]);
                        }
                    }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

                    Debug.WriteToDebug($"Player {skillPlayer.PlayerName} has got the skill \"{player.GetSkillName(randomSkill.Skill)}\".");

                    float hudExpired = Config.LoadedConfig.SkillHudDuration;
                    skillPlayer.SkillHudExpired = hudExpired == -1 ? DateTime.MaxValue : DateTime.Now.AddSeconds(hudExpired);

                    float descriptionHudExpired = Config.LoadedConfig.SkillDescriptionDuration;
                    skillPlayer.SkillDescriptionHudExpired = descriptionHudExpired == -1 ? DateTime.MaxValue : DateTime.Now.AddSeconds(descriptionHudExpired);
           
                    if (Config.LoadedConfig.TeamMateSkillChatInfo)
                    {
                        Instance?.AddTimer(.6f, () =>
                        {
                            if (player == null || !player.IsValid) return;

                            foreach (var teammate in teammates)
                            {
                                var teammateInfo = PlayerManager.GetPlayerByIndex(teammate.Index);
                                if (teammateInfo != null && teammateInfo?.Skill != null)
                                {
                                    var skillInfo = SkillData.Skills.FirstOrDefault(p => p.Skill == teammateInfo.Skill);
                                    teammateSkills += $" {ChatColors.DarkRed}\u202A{teammate.PlayerName}\u202C{ChatColors.Lime}: {(skillInfo == null ? player.GetSkillName(Skills.None) : player.GetSkillName(skillInfo.Skill, teammateInfo.SkillChance))}\n";
                                }
                            }

                            if (!string.IsNullOrEmpty(teammateSkills))
                            {
                                SkillUtils.PrintToChat(player, string.Empty, title: player.GetTranslation("teammate_skills"), border: "t");
                                foreach (string text in teammateSkills.Split("\n"))
                                    if (!string.IsNullOrEmpty(text))
                                        SkillUtils.PrintToChat(player, text, title: player.GetTranslation("teammate_skills"), border: "");
                                SkillUtils.PrintToChat(player, string.Empty, title: player.GetTranslation("teammate_skills"), border: "b");
                            }
                        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                    }
                }
            }
        }

        public static void SetRandomSkill(CCSPlayerController player)
        {
            lock (setLock)
            {
                var validPlayers = Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsHLTV && p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist).ToList();

                if (Config.LoadedConfig.GameMode == (int)Config.GameModes.TeamSkills)
                {
                    List<jSkill_SkillInfo> tSkills = [.. SkillData.Skills];
                    tSkills.RemoveAll(s => s.Skill == tSkill.Skill || s.Skill == Skills.None || counterterroristSkills.Any(s2 => s2.Name == s.Skill.ToString()));
                    tSkill = tSkills.Count == 0 ? noneSkill : tSkills[0];

                    List<jSkill_SkillInfo> ctSkills = [.. SkillData.Skills];
                    ctSkills.RemoveAll(s => s.Skill == ctSkill.Skill || s.Skill == Skills.None || terroristSkills.Any(s2 => s2.Name == s.Skill.ToString()));
                    ctSkill = ctSkills.Count == 0 ? noneSkill : ctSkills[0];
                }

                if (player == null) return;
                var skillPlayer = PlayerManager.GetPlayerByIndex(player!.Index);
                if (skillPlayer == null) return;

                skillPlayer.IsDrawing = false;
                if (player.PlayerPawn.Value == null || !player.PlayerPawn.IsValid)
                {
                    skillPlayer.Skill = Skills.None;
                    return;
                }

                jSkill_SkillInfo randomSkill = noneSkill;
                if (Instance?.GameRules != null && Instance?.GameRules.WarmupPeriod == false)
                {
                    Config.GameModes gameMode = (Config.GameModes)Config.LoadedConfig.GameMode;
                    if (staticSkills.TryGetValue(player.Index, out var staticSkill))
                        randomSkill = staticSkill;
                    else if (gameMode == Config.GameModes.Normal || gameMode == Config.GameModes.FullRandom || gameMode == Config.GameModes.NoRepeat)
                    {
                        List<jSkill_SkillInfo> skillList = [.. SkillData.Skills];
                        skillList.RemoveAll(s => s?.Skill == Skills.None);
                        if (!player.IsBot)
                            skillList.RemoveAll(s => !string.IsNullOrEmpty(SkillsInfo.GetValue<string>(s.Skill, "requiredPermission")) && !AdminManager.PlayerHasPermissions(player, SkillsInfo.GetValue<string>(s.Skill, "requiredPermission")));

                        if (gameMode != Config.GameModes.FullRandom)
                            skillList.RemoveAll(s => s?.Skill == skillPlayer?.Skill || s?.Skill == skillPlayer?.SpecialSkill);

                        if (validPlayers.Count(p => p.Team == player.Team) == 1)
                        {
                            SkillsInfo.DefaultSkillInfo[] skillsNeedsTeammates = [.. SkillsInfo.LoadedConfig.Where(s => s.NeedsTeammates)];
                            skillList.RemoveAll(s => skillsNeedsTeammates.Any(s2 => s2.Name == s.Skill.ToString()));
                        }

                        if (player.Team == CsTeam.Terrorist)
                            skillList.RemoveAll(s => counterterroristSkills.Any(s2 => s2.Name == s.Skill.ToString()));
                        else
                            skillList.RemoveAll(s => terroristSkills.Any(s2 => s2.Name == s.Skill.ToString()));

                        if (gameMode == Config.GameModes.NoRepeat && playersSkills.TryGetValue(player.Index, out ConcurrentBag<jSkill_SkillInfo>? skills))
                        {
                            skillList.RemoveAll(s => skills.Any(s2 => s2.Skill == s.Skill));
                            if (skillList.Count == 0) skills.Clear();
                        }

                        var assignmentCounts = new Dictionary<Skills, int>();
                        foreach (var sp in Instance.SkillPlayer)
                        {
                            if (sp == null) continue;
                            if (assignmentCounts.TryGetValue(sp.Skill, out var cnt)) assignmentCounts[sp.Skill] = cnt + 1;
                            else assignmentCounts[sp.Skill] = 1;
                        }

                        randomSkill = skillList.Count == 0 ? noneSkill : ChooseSkillByRarityAndMax(skillList, assignmentCounts, gameMode);
                    }
                    else if (gameMode == Config.GameModes.TeamSkills)
                        randomSkill = player.Team == CsTeam.Terrorist ? tSkill : ctSkill;
                    else if (gameMode == Config.GameModes.SameSkills)
                        randomSkill = allSkill;
                    else if (gameMode == Config.GameModes.Debug)
                    {
                        if (debugSkills.Count == 0)
                            debugSkills = [.. SkillData.Skills];
                        randomSkill = debugSkills[0];
                        debugSkills.RemoveAt(0);
                        player.PrintToChat($"{SkillData.Skills.Count - debugSkills.Count}/{SkillData.Skills.Count}");
                    }
                }

                Instance?.SkillAction(skillPlayer.Skill.ToString(), "DisableSkill", [player]);
                skillPlayer.Skill = randomSkill.Skill;
                skillPlayer.SpecialSkill = Skills.None;

                if (randomSkill.Display && Config.LoadedConfig.YourSkillChatInfo)
                    SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(randomSkill.Skill)}{ChatColors.Lime}: {player.GetSkillDescription(randomSkill.Skill)}",
                        border: !Utilities.GetPlayers().Any(p => p != null && p.IsValid && p.Team == player.Team && p != player) ? "tb" : "t");

                if (randomSkill.Skill == Skills.Illiterate)
                    Illiterate.Enable();

                Instance?.AddTimer(.2f, () =>
                {
                    if (SkillsInfo.GetValue<bool>(randomSkill.Skill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                        Instance?.AddTimer(Config.LoadedConfig.SkillTimeBeforeStart, () =>
                        {
                            if (PlayerManager.GetPlayerByIndex(player!.Index)?.Skill != randomSkill.Skill) return;
                            Instance?.SkillAction(randomSkill.Skill.ToString(), "EnableSkill", [player]);
                        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                    else
                        Instance?.SkillAction(randomSkill.Skill.ToString(), "EnableSkill", [player]);
                }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

                Debug.WriteToDebug($"Player {skillPlayer.PlayerName} has got the skill \"{player.GetSkillName(randomSkill.Skill)}\".");
                skillPlayer.SkillDescriptionHudExpired = DateTime.Now.AddSeconds(Config.LoadedConfig.SkillDescriptionDuration);
            }
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("CheckTransmit", infoList);
            }
        }

        public static void UpdateSkillHUD(CCSPlayerController? player, string? headerLine, string? centerLine, string? extraLine, bool isDescription)
        {
            lock (setLock)
            {
                if (player == null || !player.IsValid) return;

                if (Illiterate.CheckIlliterateSkill(player))
                {
                    headerLine = Illiterate.GetRandomText(headerLine);
                    centerLine = Illiterate.GetRandomText(centerLine);
                    extraLine = Illiterate.GetRandomText(extraLine);
                }

                var config = Config.LoadedConfig.HtmlHudCustomisation;
                var emptySymbol = $"<font class='fontSize-{(string.IsNullOrEmpty(headerLine) || string.IsNullOrEmpty(config.HeaderLineSize) ? "l" : "ml")}'> </font>";
                var emptySymbol2 = $"<font class='fontSize-ml'> </font>";

                string infoLine = string.IsNullOrEmpty(headerLine) || string.IsNullOrEmpty(config.HeaderLineSize)
                    ? ""
                    : $"<font class='fontWeight-Bold fontSize-{config.HeaderLineSize}' color='{config.HeaderLineColor}'>{headerLine}:</font><br>";

                string skillLine = $"{emptySymbol2}<font class='fontWeight-Bold fontSize-{config.SkillLineSize}'>{centerLine}</font>{emptySymbol2}";

                string remainingLine = string.IsNullOrWhiteSpace(extraLine)
                    ? ""
                    : $"<br>{emptySymbol}<font class='fontSize-{(isDescription ? config.SkillDescriptionLineSize : config.InfoLineSize)}' color='{(isDescription ? config.SkillDescriptionLineColor : config.InfoLineColor)}'>{extraLine}</font>{emptySymbol}";

                var hudContent = infoLine + skillLine + remainingLine;

                player.PrintToCenterHtml(hudContent);
            }
        }

        public static void EnableTransmit()
        {
            if (!isTransmitRegistered)
            {
                Instance?.RegisterListener<CheckTransmit>(CheckTransmit);
                isTransmitRegistered = true;
            }
        }

        public static DateTime GetFreezeTimeEnd() => freezeTimeEnd;
    }
}
