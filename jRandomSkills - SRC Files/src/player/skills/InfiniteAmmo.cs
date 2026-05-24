using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class InfiniteAmmo : ISkill
    {
        private const Skills skillName = Skills.InfiniteAmmo;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void WeaponFire(EventWeaponFire @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(player)) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill == skillName)
                ApplyInfiniteAmmo(player!);

        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(player)) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill == skillName)
                player!.GiveNamedItem($"weapon_{@event.Weapon}");

        }

        public static void WeaponReload(EventWeaponReload @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(player)) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill == skillName)
                ApplyInfiniteAmmo(player!);
        }

        private static void ApplyInfiniteAmmo(CCSPlayerController player)
        {
            var activeWeaponHandle = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon;
            if (activeWeaponHandle?.Value != null)
                activeWeaponHandle.Value.Clip1 = 100;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#0000FF", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
        }
    }
}