using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class NoNades : ISkill
    {
        private const Skills skillName = Skills.NoNades;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        private const string infernoDamage = "inferno";

        public static bool PlayerHurtPre(EventPlayerHurt @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(player)) return false;

            if (@event.Weapon != infernoDamage && !WeaponPool.IsGrenade(@event.Weapon)) return false;
            if (PlayerManager.GetPlayerByIndex(player!.Index)?.Skill != skillName) return false;

            SkillUtils.RestoreHealth(player);
            return true;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#a38c1a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}