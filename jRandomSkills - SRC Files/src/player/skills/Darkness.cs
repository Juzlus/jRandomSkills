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
        private static readonly ConcurrentDictionary<uint, byte> playersInDark = [];
        private static readonly ConcurrentDictionary<uint, uint> playersToTarget = [];
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
                    if (playersInDark.ContainsKey(player.Index))
                        DisableSkill(player);
                    SkillUtils.CloseMenu(player);
                }
                playersInDark.Clear();
                playersToTarget.Clear();
            }
        }

        public static void OnTick()
        {
            if (Server.TickCount % 32 != 0) return;
            foreach (var player in Utilities.GetPlayers())
            {
                if (!SkillUtils.HasMenu(player)) continue;
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);

                if (playerInfo == null || playerInfo.Skill != skillName) continue;
                var enemies = Utilities.GetPlayers().Where(p =>p != null &&p.IsValid).Select(p => PlayerManager.GetPlayerEvent(p)).Where(p =>p != null &&p.IsValid &&p.Team != player.Team &&p.PlayerPawn?.Value != null &&p.PlayerPawn.Value.IsValid &&p.PlayerPawn.Value.Health > 0 &&!p.IsHLTV &&p.Team != CsTeam.Spectator&& p.Team != CsTeam.None).ToArray();

                ConcurrentBag<(string, string)> menuItems = [.. enemies.Select(e => (e.PlayerName, e.Index.ToString()))];
                SkillUtils.UpdateMenu(player, menuItems);
            }
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            if (playerInfo.SkillUsed)
            {
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("areareaper_used_info")}");
                return;
            }

            string enemyId = commands[0];
            if (!uint.TryParse(enemyId, out uint enemyIndex)) { player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("selectplayerskill_incorrect_enemy_index")); return; }
            var enemy = Utilities.GetPlayerFromIndex((int)enemyIndex);

            if (enemy == null)
            {
                player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            SetUpPostProcessing(enemy);
            playersToTarget[player.Index] = enemy.Index;
            playerInfo.SkillUsed = true;

            player.PrintToChat($" {ChatColors.Green}" + player.GetTranslation("darkness_player_info", enemy.PlayerName));
            enemy.PrintToChat($" {ChatColors.Red}" + enemy.GetTranslation("darkness_enemy_info"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var enemies = Utilities.GetPlayers().Where(p =>p != null &&p.IsValid).Select(p => PlayerManager.GetPlayerEvent(p)).Where(p =>p != null &&p.IsValid &&p.Team != player.Team &&p.PlayerPawn?.Value != null &&p.PlayerPawn.Value.IsValid &&p.PlayerPawn.Value.Health > 0 &&!p.IsHLTV &&p.Team != CsTeam.Spectator&& p.Team != CsTeam.None).ToArray();
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
                if (playersToTarget.TryRemove(player.Index, out uint targetIndex))
                {
                    var target = Utilities.GetPlayerFromIndex((int)targetIndex);
                    if (target != null && target.IsValid)
                        SetUpPostProcessing(target, true);
                    playersInDark.TryRemove(targetIndex, out _);
                }

                SkillUtils.CloseMenu(player);
            }
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            SetUpPostProcessing(player, true);
            playersInDark.TryRemove(player.Index, out _);
            SkillUtils.CloseMenu(player);
        }

        private static void SetUpPostProcessing(CCSPlayerController player, bool turnOff = false)
        {
            if (player == null || !player.IsValid) return;

            uint playerIndex = player.Index;

            lock (setLock)
            {
                if (!turnOff)
                {
                    playersInDark.TryAdd(playerIndex, 0);
                    ApplyColor(player);

                    Timer? darkTimer = null;
                    darkTimer = Instance.AddTimer(5f, () => {
                        if (!playersInDark.ContainsKey(playerIndex))
                        {
                            darkTimer?.Kill();
                            return;
                        }

                        var target = Utilities.GetPlayerFromIndex((int)playerIndex);
                        if (target == null || !target.IsValid)
                        {
                            darkTimer?.Kill();
                            return;
                        }

                        if (target.PawnIsAlive)
                            ApplyColor(target);
                    }, TimerFlags.STOP_ON_MAPCHANGE | TimerFlags.REPEAT);
                }
                else
                {
                    SkillUtils.ApplyScreenColor(player, r: 0, g: 0, b: 0, a: 0, duration: 200, holdTime: 0);
                    playersInDark.TryRemove(playerIndex, out _);
                }
            }
        }

        private static void ApplyColor(CCSPlayerController? player)
        {
            SkillUtils.ApplyScreenColor(player,
                r: SkillsInfo.GetValue<int>(skillName, "R"),
                g: SkillsInfo.GetValue<int>(skillName, "G"),
                b: SkillsInfo.GetValue<int>(skillName, "B"),
                a: SkillsInfo.GetValue<int>(skillName, "A"),
                duration: 100,
                holdTime: 3000);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#383838", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Rare, int r = 0, int g = 0, int b = 0, int a = 230) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public int R { get; set; } = r;
            public int G { get; set; } = g;
            public int B { get; set; } = b;
            public int A { get; set; } = a;
        }
    }
}