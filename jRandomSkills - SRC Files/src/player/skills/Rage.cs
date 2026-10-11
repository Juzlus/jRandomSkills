using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Rage : ISkill
    {
        private const Skills skillName = Skills.Rage;
        private static readonly Skills[] parts = [Skills.Wallhack, Skills.Aimbot];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo == null || playerInfo.Skill != skillName) return;

            var granted = parts.Where(p => SkillData.GetInfo(p) != null && !IsSkillBlockedByMode(p)).ToArray();
            playerInfo.ExtraSkills = granted;
            foreach (var part in granted)
                Instance.SkillAction(part.ToString(), "EnableSkill", [player]);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo == null || playerInfo.ExtraSkills.Length == 0) return;

            var extras = playerInfo.ExtraSkills;
            playerInfo.ExtraSkills = [];
            foreach (var extra in extras)
                Instance.SkillAction(extra.ToString(), "DisableSkill", [player]);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#d40000", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 1, Rarity rarity = Rarity.Legendary) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}
