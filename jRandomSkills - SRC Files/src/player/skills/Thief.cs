using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Thief : ISkill
    {
        private const Skills skillName = Skills.Thief;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
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

        public static void NewRound()
        {
            foreach (var player in Utilities.GetPlayers())
                SkillUtils.CloseMenu(player);
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
            var enemy = Utilities.GetPlayers().FirstOrDefault(p => p.Index.ToString() == enemyId);

            if (enemy == null)
            {
                player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            StealSkill(player, enemy);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var enemies = Utilities.GetPlayers().Where(p => p.PawnIsAlive && p.Team != player.Team && p.IsValid && !p.IsBot && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();
            if (enemies.Length > 0)
            {
                ConcurrentBag<string> skills = [];
                ConcurrentBag<(string, string)> menuItems = [];
                foreach (var enemy in enemies)
                {
                    var enemyInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == enemy.SteamID);
                    if (enemyInfo == null) continue;
                    var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == enemyInfo.Skill);
                    if (skillData == null) continue;
                    skills.Add(skillData.Skill.ToString());
                    menuItems.Add(($"{enemy.PlayerName} : {player.GetSkillName(skillData.Skill)}", enemy.Index.ToString()));
                }

                int ctSkills = Event.counterterroristSkills.Count(s => skills.Contains(s.Name));
                int ttSkills = Event.terroristSkills.Count(s => skills.Contains(s.Name));
                if ((player.Team == CsTeam.Terrorist && ctSkills == skills.Count) || (player.Team == CsTeam.CounterTerrorist && ttSkills == skills.Count))
                {
                    Event.SetRandomSkill(player);
                    return;
                }

                SkillUtils.CreateMenu(player, menuItems);
                SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName)}", false);
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

        private static void StealSkill(CCSPlayerController player, CCSPlayerController enemy)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            var enemyInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == enemy.SteamID);
            if (playerInfo == null || enemyInfo == null) return;
            
            var enemySkill = enemyInfo.Skill;
            bool ctSkill = Event.counterterroristSkills.Any(s => s.Name == enemySkill.ToString());
            bool ttSkill = Event.terroristSkills.Any(s => s.Name == enemySkill.ToString());

            if ((player.Team == CsTeam.Terrorist && ctSkill) || (player.Team == CsTeam.CounterTerrorist && ttSkill))
            {
                Instance.AddTimer(.1f, () =>
                {
                    Instance.SkillAction(skillName.ToString(), "EnableSkill", [player]);
                    player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("thief_incorrect_skill", enemy.PlayerName));
                });
                return;
            }

            SkillUtils.CloseMenu(player);
            Instance.AddTimer(.1f, () =>
            {
                playerInfo.Skill = enemySkill;
                playerInfo.SpecialSkill = skillName;
                SkillUtils.CloseMenu(player);
                Instance.SkillAction(enemySkill.ToString(), "EnableSkill", [player]);
                player.PrintToChat($" {ChatColors.Green}" + player.GetTranslation("thief_player_info", enemy.PlayerName));

                if (SkillsInfo.GetValue<bool>(enemySkill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                    Instance?.AddTimer(Math.Max((float)(Event.GetFreezeTimeEnd() - DateTime.Now).TotalSeconds, 0), () => {
                        if (Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID && p.Skill == enemySkill) == null) return;
                        Instance?.SkillAction(enemySkill.ToString(), "EnableSkill", new[] { player });
                    });
                else
                    Instance?.SkillAction(enemySkill.ToString(), "EnableSkill", new[] { player });
            });

            Instance.AddTimer(.1f, () =>
            {
                Instance.SkillAction(enemySkill.ToString(), "DisableSkill", [enemy]);
                enemyInfo.SpecialSkill = enemySkill;
                enemyInfo.Skill = Skills.None;
                enemy.PrintToChat($" {ChatColors.Red}" + enemy.GetTranslation("thief_enemy_info"));
            });
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#adaec7", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}