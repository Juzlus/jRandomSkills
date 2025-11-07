using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class LifeSwap : ISkill
    {
        private const Skills skillName = Skills.LifeSwap;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var player in Utilities.GetPlayers())
                SkillUtils.CloseMenu(player);
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

                ConcurrentBag<(string, string)> menuItems = new(enemies.Select(e => ($"{e.PlayerName} : {e.PawnHealth} HP", e.Index.ToString())));
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

            SwapHealth(player, enemy);
            playerInfo.SkillUsed = true;
            player.PrintToChat($" {ChatColors.Green}" + player.GetTranslation("lifeswap_player_info", enemy.PlayerName));
            enemy.PrintToChat($" {ChatColors.Red}" + enemy.GetTranslation("lifeswap_enemy_info"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var enemies = Utilities.GetPlayers().Where(p => p.PawnIsAlive && p.Team != player.Team && p.IsValid && !p.IsBot && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();
            if (enemies.Length > 0)
            {
                ConcurrentBag<(string, string)> menuItems = new(enemies.Select(e => ($"{e.PlayerName} : {e.PawnHealth} HP", e.Index.ToString())));
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        private static void SwapHealth(CCSPlayerController player, CCSPlayerController enemy)
        {
            var playerPawn = player.PlayerPawn.Value;
            var enemyPawn = enemy.PlayerPawn.Value;

            if (playerPawn == null || !playerPawn.IsValid || enemyPawn == null || !enemyPawn.IsValid) return;
            if (playerPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || enemyPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            (enemyPawn.Health, playerPawn.Health) = (playerPawn.Health, enemyPawn.Health);
            Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_iHealth");
            Utilities.SetStateChanged(enemyPawn, "CBaseEntity", "m_iHealth");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillUtils.CloseMenu(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#a3651a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}