using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    public class AntyHead : ISkill
    {
        private const Skills skillName = Skills.AntyHead;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            int hitgroup = @event.Hitgroup;

            if (victim == null || !victim.IsValid || attacker == null || !attacker.IsValid || attacker == victim) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(victim.Index);
            if (playerInfo?.Skill == skillName && hitgroup == (int)HitGroup_t.HITGROUP_HEAD)
                SkillUtils.RestoreHealth(victim);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8B4513", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
        }
    }
}