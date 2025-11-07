using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class ChillOut : ISkill
    {
        private const Skills skillName = Skills.ChillOut;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void BombBeginplant(EventBombBeginplant @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;

            var anyChillOut = Instance.SkillPlayer.FirstOrDefault(p => p.Skill == skillName);
            if (anyChillOut != null)
            {
                var bombEntities = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").ToList();
                if (bombEntities.Count != 0)
                {
                    var bomb = bombEntities.FirstOrDefault();
                    if (bomb != null)
                        bomb.ArmedTime = Server.CurrentTime + SkillsInfo.GetValue<float>(skillName, "bombArmedTime");
                }
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#343deb", CsTeam onlyTeam = CsTeam.CounterTerrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float bombArmedTime = 10f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float BombArmedTime { get; set; } = bombArmedTime;
        }
    }
}