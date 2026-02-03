using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player.skills
{
    public class Darkness : ISkill
    {
        private const Skills skillName = Skills.Darkness;
        private static readonly ConcurrentDictionary<ulong, byte> playersInDark = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                foreach (var player in Utilities.GetPlayers())
                {
                    if (playersInDark.ContainsKey(player.SteamID))
                        DisableSkill(player);
                    SkillUtils.CloseMenu(player);
                }
                playersInDark.Clear();
            }
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null) return;
            DisableSkill(player);
        }

        public static void OnTick()
        {
            if (Server.TickCount % 32 != 0) return;
            foreach (var player in Utilities.GetPlayers())
            {
                if (!SkillUtils.HasMenu(player)) continue;
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);

                if (playerInfo == null || playerInfo.Skill != skillName) continue;
                var enemies = Utilities.GetPlayers().Where(p => p.PawnIsAlive && p.Team != player.Team && p.IsValid && !p.IsBot && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();

                ConcurrentBag<(string, string)> menuItems = [.. enemies.Select(e => (e.PlayerName, e.Index.ToString()))];
                SkillUtils.UpdateMenu(player, menuItems);
            }
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || !player.PawnIsAlive) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;

            if (playerInfo.SkillUsed)
            {
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("areareaper_used_info")}");
                return;
            }

            string enemyId = commands[0];
            var enemy = Utilities.GetPlayers().FirstOrDefault(p => p.Team != player.Team && p.Index.ToString() == enemyId);

            if (enemy == null)
            {
                player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            SetUpPostProcessing(enemy);
            playerInfo.SkillUsed = true;
            player.PrintToChat($" {ChatColors.Green}" + player.GetTranslation("darkness_player_info", enemy.PlayerName));
            enemy.PrintToChat($" {ChatColors.Red}" + enemy.GetTranslation("darkness_enemy_info"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var enemies = Utilities.GetPlayers().Where(p => p.PawnIsAlive && p.Team != player.Team && p.IsValid && !p.IsBot && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();
            if (enemies.Length > 0)
            {
                ConcurrentBag<(string, string)> menuItems = [.. enemies.Select(e => (e.PlayerName, e.Index.ToString()))];
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            lock (setLock)
            {
                SetUpPostProcessing(player, true);
                SkillUtils.CloseMenu(player);
            }
        }

        private static void SetUpPostProcessing(CCSPlayerController player, bool turnOff = false)
        {
            if (player == null || !player.IsValid) return;
            ulong playerSteamID = player.SteamID;

            lock (setLock)
            {
                if (!turnOff)
                {
                    playersInDark.TryAdd(playerSteamID, 0);
                    ApplyColor(player);

                    Timer? darkTimer = null;
                    darkTimer = Instance.AddTimer(5f, () => {
                        if (!playersInDark.ContainsKey(playerSteamID))
                        {
                            darkTimer?.Kill();
                            return;
                        }

                        var target = Utilities.GetPlayerFromSteamId(playerSteamID);
                        if (target == null || !target.IsValid)
                        {
                            darkTimer?.Kill();
                            return;
                        }

                        if (target.PawnIsAlive)
                            ApplyColor(player);
                    }, TimerFlags.STOP_ON_MAPCHANGE | TimerFlags.REPEAT);
                }
                else
                {
                    SkillUtils.ApplyScreenColor(player, r: 0, g: 0, b: 0, a: 0, duration: 200, holdTime: 0);
                    playersInDark.TryRemove(player.SteamID, out _);
                }
            }
        }

        private static void ApplyColor(CCSPlayerController player)
        {
            SkillUtils.ApplyScreenColor(player,
                r: SkillsInfo.GetValue<int>(skillName, "R"),
                g: SkillsInfo.GetValue<int>(skillName, "G"),
                b: SkillsInfo.GetValue<int>(skillName, "B"),
                a: SkillsInfo.GetValue<int>(skillName, "A"),
                duration: 100,
                holdTime: 3000);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#383838", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int r = 0, int g = 0, int b = 0, int a = 230) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int R { get; set; } = r;
            public int G { get; set; } = g;
            public int B { get; set; } = b;
            public int A { get; set; } = a;
        }
    }
}