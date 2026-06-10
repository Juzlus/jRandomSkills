using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Bankrupt : ISkill
    {
        private const Skills skillName = Skills.Bankrupt;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player != null && player.IsValid)
                    SkillUtils.CloseMenu(player);
            }
        }

        public static void OnTick()
        {
            if (Server.TickCount % 32 != 0) return;

            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid || !SkillUtils.HasMenu(player)) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo == null || playerInfo.Skill != skillName) continue;

                var enemies = Utilities.GetPlayers().Where(p => p != null && p.IsValid).Select(p => PlayerManager.GetPlayerEvent(p)).Where(p => p != null && p.IsValid && p.Team != player.Team && p.PlayerPawn?.Value != null && p.PlayerPawn.Value.IsValid && p.PlayerPawn.Value.Health > 0 && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();
                ConcurrentBag<(string, string)> menuItems = [];

                foreach (var e in enemies)
                {
                    int money = e.InGameMoneyServices?.Account ?? 0;
                    menuItems.Add(($"\u202A{e.PlayerName}\u202C : {money}$", e.Index.ToString()));
                }

                if (!menuItems.IsEmpty)
                    SkillUtils.UpdateMenu(player, menuItems);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            playerInfo.SkillUsed = false;

            var enemies = Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive && p.Team != player.Team && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();
            if (enemies.Length > 0)
            {
                ConcurrentBag<(string, string)> menuItems = [.. enemies.Select(e => ($"\u202A{e.PlayerName}\u202C : {(e.InGameMoneyServices?.Account ?? 0)}$", e.Index.ToString()))];
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
            {
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
            }
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || commands.Length < 1) return;

            string option = commands[0];
            if (string.IsNullOrEmpty(option)) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            if (playerInfo.SkillUsed)
            {
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("selectplayerskill_used_info")}");
                return;
            }

            if (uint.TryParse(option, out uint enemyIndex))
            {
                var enemy = Utilities.GetEntityFromIndex<CCSPlayerController>((int)enemyIndex);
                if (enemy != null && enemy.IsValid && enemy.PawnIsAlive && enemy.Team != player.Team)
                {
                    ResetMoney(enemy);
                    playerInfo.SkillUsed = true;
                    SkillUtils.CloseMenu(player);
                    player.PrintToChat($" {ChatColors.Lime}{player.GetTranslation("bankrupt_player_info", enemy.PlayerName)}");
                    enemy.PrintToChat($" {ChatColors.Red}{enemy.GetTranslation("bankrupt_enemy_info")}");
                    return;
                }
            }
            player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        private static void ResetMoney(CCSPlayerController enemy)
        {
            if (enemy == null || !enemy.IsValid) return;
            var enemyMoneyServices = enemy.InGameMoneyServices;
            if (enemyMoneyServices == null) return;

            enemyMoneyServices.Account = 0;
            Utilities.SetStateChanged(enemy, "CCSPlayerController", "m_pInGameMoneyServices");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player != null && player.IsValid)
                SkillUtils.CloseMenu(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#abab33", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
        }
    }
}