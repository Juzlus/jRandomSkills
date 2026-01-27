using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Grenadier : ISkill
    {
        private const Skills skillName = Skills.Grenadier;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (!Instance.IsPlayerValid(player)) return;
            SkillUtils.TryGiveWeapon(player, CsItem.HEGrenade);
        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            var player = @event.Userid;
            var weapon = @event.Weapon;
            if (weapon != "hegrenade" || !Instance.IsPlayerValid(player)) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);
            if (playerInfo?.Skill == skillName)
                player!.GiveNamedItem($"weapon_{weapon}");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#4a6e21", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}