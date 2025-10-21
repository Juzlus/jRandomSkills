using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Deactivator : ISkill
    {
        private const Skills skillName = Skills.Deactivator;

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

                ConcurrentBag<(string, string)> menuItems = [];
                foreach (var enemy in enemies)
                {
                    var enemyInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == enemy.SteamID);
                    if (enemyInfo == null) continue;
                    var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == enemyInfo.Skill);
                    if (skillData == null) continue;
                    menuItems.Add(($"{enemy.PlayerName} : {player.GetSkillName(skillData.Skill)}", enemy.Index.ToString()));
                }
                SkillUtils.UpdateMenu(player, menuItems);
            }
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;
            if (!player.IsValid || !player.PawnIsAlive) return;

            string enemyId = commands[0];
            var enemy = Utilities.GetPlayers().FirstOrDefault(p => p.Team != player.Team && p.Index.ToString() == enemyId);

            if (enemy == null)
            {
                player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            DeacitvateSkill(player, enemy);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var enemies = Utilities.GetPlayers().Where(p => p.PawnIsAlive && p.Team != player.Team && p.IsValid && !p.IsBot && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();
            if (enemies.Length > 0)
            {
                ConcurrentBag<(string, string)> menuItems = [];
                foreach (var enemy in enemies)
                {
                    var enemyInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == enemy.SteamID);
                    if (enemyInfo == null) continue;
                    var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == enemyInfo.Skill);
                    if (skillData == null) continue;
                    menuItems.Add(($"{enemy.PlayerName} : {player.GetSkillName(skillData.Skill)}", enemy.Index.ToString()));
                }
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;
            playerInfo.SpecialSkill = Skills.None;
            SkillUtils.CloseMenu(player);
        }

        private static void DeacitvateSkill(CCSPlayerController player, CCSPlayerController enemy)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            var enemyInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == enemy.SteamID);

            if (playerInfo != null)
            {
                playerInfo.Skill = Skills.None;
                playerInfo.SpecialSkill = skillName;
                player.PrintToChat($" {ChatColors.Green}" + player.GetTranslation("deactivator_player_info", enemy.PlayerName));
            }

            if (enemyInfo != null)
            {
                Instance.SkillAction(enemyInfo.Skill.ToString(), "DisableSkill", [enemy]);
                enemyInfo.SpecialSkill = enemyInfo.Skill;
                enemyInfo.Skill = Skills.None;
                enemy.PrintToChat($" {ChatColors.Red}" + enemy.GetTranslation("deactivator_enemy_info"));
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#919191", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates)
        {
        }
    }
}