using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Prosthesis : ISkill
    {
        private const Skills skillName = Skills.Prosthesis;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            var hitgroup = (HitGroup_t)@event.Hitgroup;

            if (!Instance.IsPlayerValid(attacker) || !Instance.IsPlayerValid(victim)) return;
            var victimInfo = PlayerManager.GetPlayerByIndex(victim!.Index);
            if (victimInfo == null || victimInfo.Skill != skillName) return;

            HitGroup_t[] disabledHitbox = [HitGroup_t.HITGROUP_LEFTARM, HitGroup_t.HITGROUP_RIGHTARM, HitGroup_t.HITGROUP_LEFTLEG, HitGroup_t.HITGROUP_RIGHTLEG];
            if (disabledHitbox.Contains(hitgroup))
                SkillUtils.RestoreHealth(victim);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#9c9c9c", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
        }
    }
}