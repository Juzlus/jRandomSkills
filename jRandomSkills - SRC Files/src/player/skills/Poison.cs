using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Poison : ISkill
    {
        private const Skills skillName = Skills.Poison;
        private static readonly ConcurrentDictionary<CCSPlayerController, byte> poisonedPlayers = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void NewRound()
        {
            lock (setLock)
                poisonedPlayers.Clear();
        }

        public static void OnTick()
        {
            if (Server.TickCount % (int)(64 * SkillsInfo.GetValue<float>(skillName, "Cooldown")) == 0)
            {
                foreach (var player in poisonedPlayers.Keys)
                {
                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid) continue;
                    if (pawn.Health <= SkillsInfo.GetValue<int>(skillName, "MinHealth")) continue;
                    SkillUtils.TakeHealth(pawn, SkillsInfo.GetValue<int>(skillName, "Damage"));
                }
            }

            if (Server.TickCount % 32 != 0) return;
            foreach (var player in Utilities.GetPlayers())
            {
                if (!SkillUtils.HasMenu(player)) continue;
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);

                if (playerInfo == null || playerInfo.Skill != skillName) continue;
                var enemies = Utilities.GetPlayers().Where(p => p.PawnIsAlive && p.Team != player.Team && p.IsValid && !p.IsBot && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();

                ConcurrentBag<(string, string)> menuItems = new(enemies.Select(e => (e.PlayerName, e.Index.ToString())));
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

            poisonedPlayers.TryAdd(enemy, 0);
            playerInfo.SkillUsed = true;
            player.PrintToChat($" {ChatColors.Green}" + player.GetTranslation("poison_player_info", enemy.PlayerName));
            enemy.PrintToChat($" {ChatColors.Red}" + enemy.GetTranslation("poison_enemy_info"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var enemies = Utilities.GetPlayers().Where(p => p.PawnIsAlive && p.Team != player.Team && p.IsValid && !p.IsBot && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();
            if (enemies.Length > 0)
            {
                ConcurrentBag<(string, string)> menuItems = new(enemies.Select(e => (e.PlayerName, e.Index.ToString())));
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            poisonedPlayers.TryRemove(player, out _);
            SkillUtils.CloseMenu(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#902eff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float cooldown = .85f, int damage = 1, int minHealth = 30) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int Damage { get; set; } = damage;
            public float Cooldown { get; set; } = cooldown;
            public int MinHealth { get; set; } = minHealth;
        }
    }
}