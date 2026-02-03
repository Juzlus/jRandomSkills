using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class OnlyHead : ISkill
    {
        private const Skills skillName = Skills.OnlyHead;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var attacker = @event.Attacker;
            var victim = @event.Userid;
            int damage = @event.DmgHealth;
            var hitgroup = (HitGroup_t)@event.Hitgroup;

            if (!Instance.IsPlayerValid(attacker) || !Instance.IsPlayerValid(victim)) return;
            var victimInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == victim?.SteamID);
            if (victimInfo == null || victimInfo.Skill != skillName) return;

            HitGroup_t[] disabledHitbox = [HitGroup_t.HITGROUP_LEFTARM, HitGroup_t.HITGROUP_RIGHTARM, HitGroup_t.HITGROUP_LEFTLEG, HitGroup_t.HITGROUP_RIGHTLEG];
            if (hitgroup != HitGroup_t.HITGROUP_HEAD)
                SkillUtils.RestoreHealth(victim);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#3c47de", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}