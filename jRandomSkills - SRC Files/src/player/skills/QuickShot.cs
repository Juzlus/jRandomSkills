using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class QuickShot : ISkill
    {
        private const Skills skillName = Skills.QuickShot;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (!Instance.IsPlayerValid(player)) continue;
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);

                if (playerInfo?.Skill == skillName)
                {
                    var pawn = player.PlayerPawn.Value!;
                    var weaponServices = pawn.WeaponServices;
                    if (weaponServices == null || weaponServices.ActiveWeapon == null || !weaponServices.ActiveWeapon.IsValid) continue;
                   
                    var weapon = weaponServices.ActiveWeapon.Value;
                    if (weapon == null || !weapon.IsValid || pawn.CameraServices == null) continue;
                    
                    pawn.AimPunchTickBase = 0;
                    pawn.AimPunchTickFraction = 0f;
                    pawn.CameraServices.CsViewPunchAngleTick = 0;
                    pawn.CameraServices.CsViewPunchAngleTickRatio = 0f;

                    Schema.SetSchemaValue<Int32>(weapon.Handle, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick", Server.TickCount);
                    Schema.SetSchemaValue<Int32>(weapon.Handle, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick", Server.TickCount);
                }
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8a42f5", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}