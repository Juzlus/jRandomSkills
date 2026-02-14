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

        private static readonly ConcurrentDictionary<ulong, ConcurrentBag<jSkill_SkillInfo>> playersSkills = [];
        public static readonly ConcurrentDictionary<ulong, jSkill_SkillInfo> staticSkills = [];
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
            Instance.RegisterEventHandler<EventBombPlanted>(BombPlanted);
            Instance.RegisterEventHandler<EventBombBegindefuse>(BombBegindefuse);

            Instance.RegisterEventHandler<EventDecoyStarted>(DecoyStarted);
            Instance.RegisterEventHandler<EventDecoyDetonate>(DecoyDetonate);

            Instance.RegisterEventHandler<EventSmokegrenadeDetonate>(SmokegrenadeDetonate);
            Instance.RegisterEventHandler<EventSmokegrenadeExpired>(SmokegrenadeExpired);

            Instance.RegisterListener<OnPlayerButtonsChanged>(CheckUseSkill);
            Instance.RegisterListener<OnEntitySpawned>(EntitySpawned);
            Instance.RegisterListener<OnTick>(OnTick);

            Instance.HookUserMessage(208, PlayerMakeSound);
            VirtualFunctions.CBaseEntity_TakeDamageOldFunc.Hook(OnTakeDamage, HookMode.Pre);

            VirtualFunctions.CBaseTrigger_StartTouchFunc.Hook(OnTriggerEnter, HookMode.Post);
            VirtualFunctions.CBaseTrigger_EndTouchFunc.Hook(OnTriggerExit, HookMode.Pre);
        }

        private static HookResult PlayerMakeSound(UserMessage um)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "PlayerMakeSound", [um]);
                return HookResult.Continue;
            }
        }

        private static HookResult WeaponFire(EventWeaponFire @event, GameEventInfo info)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "WeaponFire", [@event]);
                return HookResult.Continue;
            }
        }

        private static HookResult WeaponEquip(EventItemEquip @event, GameEventInfo info)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "WeaponEquip", [@event]);
                return HookResult.Continue;
            }
        }

        private static HookResult WeaponPickup(EventItemPickup @event, GameEventInfo info)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "WeaponPickup", [@event]);
                return HookResult.Continue;
            }
        }

        private static HookResult WeaponReload(EventWeaponReload @event, GameEventInfo info)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "WeaponReload", [@event]);
                return HookResult.Continue;
            }
        }

        private static HookResult GrenadeThrown(EventGrenadeThrown @event, GameEventInfo info)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "GrenadeThrown", [@event]);
                return HookResult.Continue;
            }
        }

        private static HookResult BombBeginplant(EventBombBeginplant @event, GameEventInfo info)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "BombBeginplant", [@event]);
                return HookResult.Continue;
            }
        }

        private static HookResult BombPlanted(EventBombPlanted @event, GameEventInfo info)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "BombPlanted", [@event]);
                return HookResult.Continue;
            }
        }

        private static HookResult BombBegindefuse(EventBombBegindefuse @event, GameEventInfo info)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "BombBegindefuse", [@event]);
                return HookResult.Continue;
            }
        }

        private static HookResult DecoyStarted(EventDecoyStarted @event, GameEventInfo info)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "DecoyStarted", [@event]);
                return HookResult.Continue;
            }
        }

        private static HookResult DecoyDetonate(EventDecoyDetonate @event, GameEventInfo info)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "DecoyDetonate", [@event]);
                return HookResult.Continue;
            }
        }

        private static HookResult SmokegrenadeDetonate(EventSmokegrenadeDetonate @event, GameEventInfo info)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "SmokegrenadeDetonate", [@event]);
                return HookResult.Continue;
            }
        }

        private static HookResult SmokegrenadeExpired(EventSmokegrenadeExpired @event, GameEventInfo info)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "SmokegrenadeExpired", [@event]);
                return HookResult.Continue;
            }
        }

        private static HookResult PlayerHurt(EventPlayerHurt @event, GameEventInfo info)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                if (!playerSkill.IsDrawing)
                    Instance.SkillAction(playerSkill.Skill.ToString(), "PlayerHurt", [@event]);
                return HookResult.Continue;
            }
        }

        private static HookResult PlayerJump(EventPlayerJump @event, GameEventInfo info)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "PlayerJump", [@event]);
                return HookResult.Continue;
            }
        }

        private static HookResult PlayerBlind(EventPlayerBlind @event, GameEventInfo info)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "PlayerBlind", [@event]);
                return HookResult.Continue;
            }
        }

        private static HookResult OnTakeDamage(DynamicHook h)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "OnTakeDamage", [h]);
                return HookResult.Continue;
            }
        }

        private static HookResult OnTriggerEnter(DynamicHook hook)
        {
            lock (setLock)
            {
                CBaseTrigger trigger = hook.GetParam<CBaseTrigger>(0);
                CBaseEntity entity = hook.GetParam<CBaseEntity>(1);

                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "OnTriggerEnter", [trigger, entity]);
                return HookResult.Continue;
            }
        }

        private static HookResult OnTriggerExit(DynamicHook hook)
        {
            lock (setLock)
            {
                CBaseTrigger trigger = hook.GetParam<CBaseTrigger>(0);
                CBaseEntity entity = hook.GetParam<CBaseEntity>(1);

                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "OnTriggerExit", [trigger, entity]);
                return HookResult.Continue;
            }
        }

        private static void OnTick()
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        if (SkillsInfo.GetValue<bool>(playerSkill.Skill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                            return;
                        else
                            Instance.SkillAction(playerSkill.Skill.ToString(), "OnTick");
            }
        }

        private static HookResult PlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            lock (setLock)
            {
                var player = @event.Userid;
                if (player == null || !player.IsValid) return HookResult.Continue;

                Instance.SkillPlayer.Add(new jSkill_PlayerInfo
                {
                    SteamID = player.SteamID,
                    PlayerName = player.PlayerName,
                    Skill = Skills.None,
                    SpecialSkill = Skills.None,
                    IsDrawing = false,
                    SkillChance = 1,
                    PrintHTML = null,
                    DisplayHUD = true,
                    SkillUsed = false,
                });

                string welcomeMsg = player.GetTranslation("welcome_message", "welcome");
                foreach (string line in welcomeMsg.Split("\n"))
                    player.PrintToChat($" {ChatColors.Green}" + line.Replace("{PLAYER}", $" {ChatColors.Red}{player.PlayerName}{ChatColors.Green}", StringComparison.OrdinalIgnoreCase)
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
                var player = @event.Userid;
                if (player == null || !player.IsValid) return HookResult.Continue;

                var skillPlayer = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (skillPlayer == null) return HookResult.Continue;

                var items = Instance.SkillPlayer.ToList();
                Instance.SkillPlayer = [.. items.Where(p => p.SteamID != skillPlayer.SteamID)];

                return HookResult.Continue;
            }
        }

        private static HookResult PlayerChat(EventPlayerChat @event, GameEventInfo info)
        {
            var userID = @event.Userid;
            if (userID == 0 || string.IsNullOrEmpty(@event.Text)) return HookResult.Continue;
  
            string text = @event.Text.Split(' ')[0].Trim();

            string commandName = text.StartsWith('!') || text.StartsWith('/') ? text[1..] : text;
            
            foreach (var cmd in Instance.CommandDefinitions)
                Server.PrintToChatAll("1. " + cmd.Name);

            foreach (var cmd in Instance.CommandListeners.Keys)
                Server.PrintToChatAll("2. " + cmd?.Target?.ToString());

            var player = Utilities.GetPlayerFromUserid(userID);
            if (player == null || !player.IsValid) return HookResult.Continue;

            var skillPlayer = Instance?.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (skillPlayer == null) return HookResult.Continue;

            var temp = skillPlayer.SkillHudExpired;
            skillPlayer.SkillHudExpired = DateTime.MinValue;
;
            Instance.AddTimer(5f, () =>
            {
                if (skillPlayer.SkillHudExpired == DateTime.MinValue)
                    skillPlayer.SkillHudExpired = temp;
            });

            return HookResult.Continue;
        }

        private static HookResult PlayerSpawned(EventPlayerSpawned @event, GameEventInfo info)
        {
            lock (setLock)
            {
                var player = @event.Userid;
                if (player == null || !player.IsValid) return HookResult.Continue;

                var skillPlayer = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
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
                isTransmitRegistered = false;
                Instance.AddTimer(.1f, () => DisableAll());
                foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV && p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist))
                {
                    var skillPlayer = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                    if (skillPlayer == null) continue;
                    skillPlayer.IsDrawing = true;
                    skillPlayer.PrintHTML = null;
                }

                Instance.RemoveListener<CheckTransmit>(CheckTransmit);
                int freezetime = ConVar.Find("mp_freezetime")?.GetPrimitiveValue<Int32>() ?? 0;
                freezeTimeEnd = DateTime.Now.AddSeconds(freezetime + (Instance?.GameRules?.TeamIntroPeriod == true ? 7 : 0));

                setSkillTimer?.Kill();

                float timeToDraw = (Instance?.GameRules?.TeamIntroPeriod == true ? 7 : 0) + Math.Max(freezetime - Config.LoadedConfig.SkillTimeBeforeStart, 0) + .3f;
                setSkillTimer = Instance?.AddTimer(timeToDraw, SetSkill);
                return HookResult.Continue;
            }
        }

        private static void DisableAll()
        {
            lock (setLock)
            {
                foreach (var player in Utilities.GetPlayers().Where(p => p != null && !p.IsBot && p.IsValid))
                {
                    var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                    if (playerInfo == null) continue;

                    Instance.SkillAction(playerInfo.Skill.ToString(), "DisableSkill", [player]);
                    Instance.SkillAction(playerInfo.Skill.ToString(), "NewRound");

                    playerInfo.Skill = noneSkill.Skill;
                    playerInfo.SpecialSkill = noneSkill.Skill;
                    playerInfo.PrintHTML = null;
                    playerInfo.SkillChance = 1;
                    playerInfo.SkillUsed = false;
                }
            }
        }

        public static void OnMapChange()
        {
            lock (setLock)
            {
                isTransmitRegistered = false;
                Instance.AddTimer(.1f, () => {
                    DisableAll();
                    ConVar.Find("sv_legacy_jump")?.SetValue("1");
                });
                Instance.RemoveListener<CheckTransmit>(CheckTransmit);
                playersSkills.Clear();
                staticSkills.Clear();
                ctSkill = noneSkill;
                tSkill = noneSkill;
                allSkill = noneSkill;
            }
        }

        private static HookResult RoundEnd(EventRoundEnd @event, GameEventInfo info)
        {
            Illiterate.Disable();
            lock (setLock)
            {
                foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid))
                {
                    Instance.AddTimer(.5f, () =>
                    {
                        var _players = Utilities.GetPlayers().Where(p => p.IsValid && p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist).OrderBy(p => p.Team);

                        string skillsText = "";
                        foreach (var _player in _players)
                        {
                            var _playerSkill = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == _player.SteamID);
                            if (_playerSkill != null)
                            {
                                var skillInfo = SkillData.Skills.FirstOrDefault(p => p.Skill == _playerSkill.Skill);
                                var specialSkillInfo = SkillData.Skills.FirstOrDefault(s => s.Skill == _playerSkill.SpecialSkill);
                                if (skillInfo == null) continue;
                                skillsText += $" {ChatColors.DarkRed}{_player.PlayerName}{ChatColors.Lime}: {(_playerSkill.SpecialSkill == Skills.None || specialSkillInfo == null ? player.GetSkillName(skillInfo.Skill, _playerSkill.SkillChance) : $"{player.GetSkillName(specialSkillInfo.Skill)} -> {player.GetSkillName(skillInfo.Skill, _playerSkill.SkillChance)}")}\n";
                            }
                        }

                        if (Config.LoadedConfig.SummaryAfterTheRound && !string.IsNullOrEmpty(skillsText))
                        {
                            SkillUtils.PrintToChat(player, string.Empty, title: player.GetTranslation("summary"), border: "t");
                            foreach (string text in skillsText.Split("\n"))
                                if (!string.IsNullOrEmpty(text))
                                    SkillUtils.PrintToChat(player, text, title: player.GetTranslation("teammate_skills"), border: "");
                            SkillUtils.PrintToChat(player, string.Empty, border: "b");
                        }
                    });
                }

                if (Config.LoadedConfig.DisableSkillsOnRoundEnd)
                {
                    isTransmitRegistered = false;
                    Instance.AddTimer(1f, () => DisableAll());
                    Instance.RemoveListener<CheckTransmit>(CheckTransmit);
                }
                return HookResult.Continue;
            }
        }

        private static HookResult PlayerDeath(EventPlayerDeath @event, GameEventInfo info)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "PlayerDeath", [@event]);

                var victim = @event.Userid;
                var attacker = @event.Attacker;
                if (victim == null || attacker == null) return HookResult.Continue;

                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == victim.SteamID);
                if (playerInfo == null || playerInfo.IsDrawing) return HookResult.Continue;
                Instance.SkillAction(playerInfo.Skill.ToString(), "DisableSkill", [victim]);

                if (victim == attacker) return HookResult.Continue;
                if (Config.LoadedConfig.KillerSkillChatInfo)
                {
                    var attackerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker.SteamID);
                    if (attackerInfo != null)
                    {
                        var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == attackerInfo.Skill);
                        var specialSkillData = SkillData.Skills.FirstOrDefault(s => s.Skill == attackerInfo.SpecialSkill);
                        if (skillData == null || specialSkillData == null) return HookResult.Continue;
                        string skillDesc = victim.GetSkillDescription(skillData.Skill);

                        SkillUtils.PrintToChat(victim, 
                            $"{ChatColors.DarkRed}{(attackerInfo.SpecialSkill == Skills.None ? victim.GetSkillName(skillData.Skill) : $"{victim.GetSkillName(specialSkillData.Skill)} -> {victim.GetSkillName(skillData.Skill)}")}{ChatColors.Lime} - {skillDesc}",
                            title: $"{victim.GetTranslation("enemy_skill")} {ChatColors.DarkRed}{attacker.PlayerName}{ChatColors.Lime}");
                    }
                }
                return HookResult.Continue;
            }
        }

        private static void CheckUseSkill(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
        {
            lock (setLock)
            {
                string? button = Config.LoadedConfig.AlternativeSkillButton;
                if (string.IsNullOrEmpty(button) || button.Length < 2) return;
                string buttonName = $"{char.ToUpper(button[0])}{button[1..].ToLower()}";

                if (Enum.TryParse<PlayerButtons>(buttonName, out var skillButton))
                {
                    if (pressed != skillButton) return;
                }
                else return;

                if (player == null) return;
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo == null || playerInfo.IsDrawing) return;

                if (SkillsInfo.GetValue<bool>(playerInfo.Skill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                    return;

                var playerPawn = player.PlayerPawn.Value;
                if (playerPawn?.CBodyComponent == null) return;
                if (!player.IsValid || !player.PawnIsAlive) return;

                Debug.WriteToDebug($"Player {player.PlayerName} used the skill: {playerInfo.Skill} by PlayerButtons: {pressed}");
                Instance.SkillAction(playerInfo.Skill.ToString(), "UseSkill", [player]);
            }
        }

        private static void EntitySpawned(CEntityInstance entity)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "OnEntitySpawned", [entity]);
            }
        }

        private static void SetSkill()
        {
            setSkillTimer = null;
            lock (setLock)
            {
                var validPlayers = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV && p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist).ToList();

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

                foreach (var player in validPlayers)
                {
                    if (player == null) continue;
                    var teammates = validPlayers.Where(p => p.Team == player.Team && p != player);
                    string teammateSkills = "";

                    var skillPlayer = Instance?.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                    if (skillPlayer == null) continue;

                    skillPlayer.IsDrawing = false;
                    if (player.PlayerPawn.Value == null || !player.PlayerPawn.IsValid)
                    {
                        skillPlayer.Skill = Skills.None;
                        continue;
                    }

                    jSkill_SkillInfo randomSkill = noneSkill;

                    if (Instance?.GameRules != null && Instance?.GameRules.WarmupPeriod == false)
                    {
                        Config.GameModes gameMode = (Config.GameModes)Config.LoadedConfig.GameMode;
                        if (staticSkills.TryGetValue(player.SteamID, out var staticSkill))
                            randomSkill = staticSkill;
                        else if (gameMode == Config.GameModes.Normal || gameMode == Config.GameModes.FullRandom || gameMode == Config.GameModes.NoRepeat)
                        {
                            List<jSkill_SkillInfo> skillList = [.. SkillData.Skills];
                            skillList.RemoveAll(s => s?.Skill == Skills.None);
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

                            if (gameMode == Config.GameModes.NoRepeat && playersSkills.TryGetValue(player.SteamID, out ConcurrentBag<jSkill_SkillInfo>? skills))
                            {
                                skillList.RemoveAll(s => skills.Any(s2 => s2.Skill == s.Skill));
                                if (skillList.Count == 0) skills.Clear();
                            }

                            randomSkill = skillList.Count == 0 ? noneSkill : skillList[Instance.Random.Next(skillList.Count)];
                            if (gameMode == Config.GameModes.NoRepeat)
                            {
                                if (playersSkills.TryGetValue(player.SteamID, out ConcurrentBag<jSkill_SkillInfo>? value))
                                    value.Add(randomSkill);
                                else
                                    playersSkills.TryAdd(player.SteamID, [randomSkill]);
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
                    }

                    if (randomSkill.Display)
                        SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(randomSkill.Skill)}{ChatColors.Lime}: {player.GetSkillDescription(randomSkill.Skill)}",
                            border: !Utilities.GetPlayers().Any(p => p.Team == player.Team && !p.IsBot && p != player) ? "tb" : "t");

                    Instance?.SkillAction(skillPlayer.Skill.ToString(), "DisableSkill", [player]);
                    skillPlayer.Skill = randomSkill.Skill;
                    skillPlayer.SpecialSkill = Skills.None;

                    if (randomSkill.Skill == Skills.Illiterate)
                        Illiterate.Enable();

                    Instance?.AddTimer(.2f, () =>
                    {
                        if (SkillsInfo.GetValue<bool>(randomSkill.Skill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                            Instance?.AddTimer(Config.LoadedConfig.SkillTimeBeforeStart, () =>
                            {
                                if (Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID && p.Skill == randomSkill.Skill) == null) return;
                                Instance?.SkillAction(randomSkill.Skill.ToString(), "EnableSkill", [player]);
                            });
                        else
                            Instance?.SkillAction(randomSkill.Skill.ToString(), "EnableSkill", [player]);
                    });

                    Debug.WriteToDebug($"Player {skillPlayer.PlayerName} has got the skill \"{player.GetSkillName(randomSkill.Skill)}\".");
                    skillPlayer.SkillHudExpired = DateTime.Now.AddSeconds(Config.LoadedConfig.SkillHudDuration);
                    skillPlayer.SkillDescriptionHudExpired = DateTime.Now.AddSeconds(Config.LoadedConfig.SkillDescriptionDuration);

                    if (Config.LoadedConfig.TeamMateSkillChatInfo)
                    {
                        Instance?.AddTimer(.6f, () =>
                        {
                            foreach (var teammate in teammates)
                            {
                                var teammateInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == teammate.SteamID);
                                if (teammateInfo != null && teammateInfo?.Skill != null)
                                {
                                    var skillInfo = SkillData.Skills.FirstOrDefault(p => p.Skill == teammateInfo.Skill);
                                    teammateSkills += $" {ChatColors.DarkRed}{teammate.PlayerName}{ChatColors.Lime}: {(skillInfo == null ? player.GetSkillName(Skills.None) : player.GetSkillName(skillInfo.Skill, teammateInfo.SkillChance))}\n";
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
                        });
                    }
                }
            }
        }

        public static void SetRandomSkill(CCSPlayerController player)
        {
            lock (setLock)
            {
                var validPlayers = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV && p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist).ToList();

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
                var skillPlayer = Instance?.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
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
                    if (staticSkills.TryGetValue(player.SteamID, out var staticSkill))
                        randomSkill = staticSkill;
                    else if (gameMode == Config.GameModes.Normal || gameMode == Config.GameModes.FullRandom || gameMode == Config.GameModes.NoRepeat)
                    {
                        List<jSkill_SkillInfo> skillList = [.. SkillData.Skills];
                        skillList.RemoveAll(s => s?.Skill == Skills.None);
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

                        if (gameMode == Config.GameModes.NoRepeat && playersSkills.TryGetValue(player.SteamID, out ConcurrentBag<jSkill_SkillInfo>? skills))
                        {
                            skillList.RemoveAll(s => skills.Any(s2 => s2.Skill == s.Skill));
                            if (skillList.Count == 0) skills.Clear();
                        }

                        randomSkill = skillList.Count == 0 ? noneSkill : skillList[Instance.Random.Next(skillList.Count)];
                        if (gameMode == Config.GameModes.NoRepeat)
                        {
                            if (playersSkills.TryGetValue(player.SteamID, out ConcurrentBag<jSkill_SkillInfo>? value))
                                value.Add(randomSkill);
                            else
                                playersSkills.TryAdd(player.SteamID, [randomSkill]);
                        }
                    }
                    else if (gameMode == Config.GameModes.TeamSkills)
                        randomSkill = player.Team == CsTeam.Terrorist ? tSkill : ctSkill;
                    else if (gameMode == Config.GameModes.Debug)
                        return;
                }

                if (randomSkill.Display && Config.LoadedConfig.YourSkillChatInfo)
                    SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(randomSkill.Skill)}{ChatColors.Lime}: {player.GetSkillDescription(randomSkill.Skill)}",
                        border: !Utilities.GetPlayers().Any(p => p.Team == player.Team && !p.IsBot && p != player) ? "tb" : "t");

                skillPlayer.Skill = randomSkill.Skill;
                skillPlayer.SpecialSkill = Skills.None;

                if (randomSkill.Skill == Skills.Illiterate)
                    Illiterate.Enable();

                Instance?.AddTimer(.2f, () =>
                {
                    if (SkillsInfo.GetValue<bool>(randomSkill.Skill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                        Instance?.AddTimer(Config.LoadedConfig.SkillTimeBeforeStart, () =>
                        {
                            if (Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID && p.Skill == randomSkill.Skill) == null) return;
                            Instance?.SkillAction(randomSkill.Skill.ToString(), "EnableSkill", [player]);
                        });
                    else
                        Instance?.SkillAction(randomSkill.Skill.ToString(), "EnableSkill", [player]);
                });

                Debug.WriteToDebug($"Player {skillPlayer.PlayerName} has got the skill \"{player.GetSkillName(randomSkill.Skill)}\".");
                skillPlayer.SkillDescriptionHudExpired = DateTime.Now.AddSeconds(Config.LoadedConfig.SkillDescriptionDuration);
            }
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            lock (setLock)
            {
                foreach (var playerSkill in Instance.SkillPlayer)
                    if (!playerSkill.IsDrawing)
                        Instance.SkillAction(playerSkill.Skill.ToString(), "CheckTransmit", [infoList]);
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
                var emptySymbol = $"<font class='fontSize-{(string.IsNullOrEmpty(headerLine) ? "l" : "ml")}'> </font>";
                var emptySymbol2 = $"<font class='fontSize-ml'> </font>";

                string infoLine = string.IsNullOrEmpty(headerLine)
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