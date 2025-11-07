using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Zeus : ISkill
    {
        private const Skills skillName = Skills.Zeus;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillUtils.TryGiveWeapon(player, CsItem.Zeus);
        }

        public static void WeaponFire(EventWeaponFire @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);

            if (playerInfo?.Skill == skillName)
            {
                var pawn = player!.PlayerPawn!.Value!;
                if (pawn.WeaponServices == null || pawn.WeaponServices.ActiveWeapon == null || !pawn.WeaponServices.ActiveWeapon.IsValid) return;
                if (pawn.WeaponServices.ActiveWeapon.Value == null || !pawn.WeaponServices.ActiveWeapon.Value.IsValid) return;

                var activeWeapon = pawn.WeaponServices.ActiveWeapon.Value;
                if (activeWeapon.DesignerName != "weapon_taser") return;
                var taser = activeWeapon.As<CWeaponTaser>();
                Instance.AddTimer(.1f, () =>
                {
                    if (taser.IsValid)
                    {
                        taser.LastAttackTick = 0;
                        taser.FireTime = 0;
                    }
                });
            }

        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#fbff00", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}