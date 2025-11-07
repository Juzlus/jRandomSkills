using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Gambler : ISkill
    {
        private const Skills skillName = Skills.Gambler;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var player in Utilities.GetPlayers())
                SkillUtils.CloseMenu(player);
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

            var skill = SkillData.Skills.FirstOrDefault(s => player.GetSkillName(s.Skill).Equals(commands[0], StringComparison.OrdinalIgnoreCase) || s.Skill.ToString().Equals(commands[0], StringComparison.OrdinalIgnoreCase));
            if (skill == null)
            {
                player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("skill_not_found_setskill"));
                return;
            }

            if (skill.Skill == skillName && !TakeMoney(player))
            {
                player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("gambler_no_money"));
                return;
            }

            Instance.AddTimer(.1f, () =>
            {
                playerInfo.Skill = skill.Skill;
                if (skill.Skill != skillName)
                    playerInfo.SpecialSkill = skillName;
                playerInfo.SkillUsed = true;

                if (SkillsInfo.GetValue<bool>(skill.Skill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                    Instance?.AddTimer(Math.Max((float)(Event.GetFreezeTimeEnd() - DateTime.Now).TotalSeconds, 0), () => {
                        if (Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID && p.Skill == skill.Skill) == null) return;
                        Instance?.SkillAction(skill.Skill.ToString(), "EnableSkill", [player]);
                    });
                else
                    Instance?.SkillAction(skill.Skill.ToString(), "EnableSkill", [player]);
            });
        }

        private static bool TakeMoney(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.InGameMoneyServices == null) return false;
            int account = player.InGameMoneyServices.Account;
            int refreshPrice = SkillsInfo.GetValue<int>(skillName, "refreshPrice");

            int remainingMoney = account - refreshPrice;
            if (remainingMoney < 0)
                return false;

            player.InGameMoneyServices.Account = remainingMoney;
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
            return true;
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var skills = GetSkills(player);
            var firstSkill = skills[Instance.Random.Next(skills.Count)];
            skills.Remove(firstSkill);
            var secondSkill = skills[Instance.Random.Next(skills.Count)];

            ConcurrentBag<(string, string)> menuItems = [(player.GetSkillName(firstSkill.Skill), firstSkill.Skill.ToString()),
                                                   (player.GetSkillName(secondSkill.Skill), secondSkill.Skill.ToString())];
            SkillUtils.CreateMenu(player, menuItems, (player.GetTranslation("gambler_more", SkillsInfo.GetValue<int>(skillName, "refreshPrice")), skillName.ToString(), false));
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillUtils.CloseMenu(player);
        }

        private static List<jSkill_SkillInfo> GetSkills(CCSPlayerController player)
        {
            var skillPlayer = Instance?.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (skillPlayer == null) return [Event.noneSkill];

            List<jSkill_SkillInfo> skillList = [.. SkillData.Skills];
            skillList.RemoveAll(s => s?.Skill == skillPlayer?.Skill || s?.Skill == skillPlayer?.SpecialSkill || s?.Skill == Skills.None);

            if (Utilities.GetPlayers().FindAll(p => p.Team == player.Team && p.IsValid && !p.IsBot && !p.IsHLTV && p.Team != CsTeam.Spectator).Count == 1)
            {
                SkillsInfo.DefaultSkillInfo[] skillsNeedsTeammates = SkillsInfo.LoadedConfig.Where(s => s.NeedsTeammates).ToArray();
                skillList.RemoveAll(s => skillsNeedsTeammates.Any(s2 => s2.Name == s.Skill.ToString()));
            }

            if (player.Team == CsTeam.Terrorist)
                skillList.RemoveAll(s => Event.counterterroristSkills.Any(s2 => s2.Name == s.Skill.ToString()));
            else
                skillList.RemoveAll(s => Event.terroristSkills.Any(s2 => s2.Name == s.Skill.ToString()));

            return skillList.Count == 0 ? [Event.noneSkill] : skillList;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#7eff47", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int refreshPrice = 150) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int RefreshPrice { get; set; } = refreshPrice;
        }
    }
}