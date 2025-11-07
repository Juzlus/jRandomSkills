using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    public class None : ISkill
    {
        private const Skills skillName = Skills.None;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#FFFFFF", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}