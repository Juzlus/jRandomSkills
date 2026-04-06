using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Hermit : ISkill
    {
        private const Skills skillName = Skills.Hermit;
        private static readonly ConcurrentDictionary<string, int> ConcurrentDictionary = new(
        [
            new KeyValuePair<string, int>("weapon_glock", 3), new KeyValuePair<string, int>("weapon_usp_silencer", 2), new KeyValuePair<string, int>("weapon_hkp2000", 4), new KeyValuePair<string, int>("weapon_p250", 3),
            new KeyValuePair<string, int>("weapon_cz75", 2), new KeyValuePair<string, int>("weapon_deagle", 3), new KeyValuePair<string, int>("weapon_fiveseven", 2), new KeyValuePair<string, int>("weapon_elite", 2),
            new KeyValuePair<string, int>("weapon_tec9", 3), new KeyValuePair<string, int>("weapon_revolver", 2), new KeyValuePair<string, int>("weapon_mac10", 3), new KeyValuePair<string, int>("weapon_mp9", 2),
            new KeyValuePair<string, int>("weapon_mp7", 3), new KeyValuePair<string, int>("weapon_mp5", 3), new KeyValuePair<string, int>("weapon_mp5sd", 3), new KeyValuePair<string, int>("weapon_ump45", 3),
            new KeyValuePair<string, int>("weapon_p90", 2), new KeyValuePair<string, int>("weapon_bizon", 2), new KeyValuePair<string, int>("weapon_ak47", 3), new KeyValuePair<string, int>("weapon_m4a1", 4),
            new KeyValuePair<string, int>("weapon_m4a1_silencer", 3), new KeyValuePair<string, int>("weapon_galilar", 4), new KeyValuePair<string, int>("weapon_famas", 4), new KeyValuePair<string, int>("weapon_aug", 3),
            new KeyValuePair<string, int>("weapon_sg556", 3), new KeyValuePair<string, int>("weapon_ssg08", 2), new KeyValuePair<string, int>("weapon_awp", 2), new KeyValuePair<string, int>("weapon_scar20", 2),
            new KeyValuePair<string, int>("weapon_g3sg1", 2), new KeyValuePair<string, int>("weapon_nova", 32), new KeyValuePair<string, int>("weapon_xm1014", 32), new KeyValuePair<string, int>("weapon_sawedoff", 32),
            new KeyValuePair<string, int>("weapon_mag7", 3), new KeyValuePair<string, int>("weapon_m249", 2), new KeyValuePair<string, int>("weapon_negev", 2)
        ]);
        private static readonly ConcurrentDictionary<string, int> maxReserveAmmo = ConcurrentDictionary;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var attacker = @event.Attacker;
            if (!Instance.IsPlayerValid(attacker)) return;

            var attackerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker?.SteamID);
            if (attackerInfo?.Skill != skillName) return;

            var pawn = attacker!.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;

            var weapon = pawn.WeaponServices.ActiveWeapon.Value;
            if (weapon == null || !weapon.IsValid || weapon.VData == null) return;

            var maxReserveAmmoClip = maxReserveAmmo.TryGetValue(weapon.DesignerName, out var reserve) ? reserve : 100;
            weapon.Clip1 = weapon.VData.MaxClip1;
            weapon.ReserveAmmo.Fill(maxReserveAmmoClip);

            Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1");
            Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_pReserveAmmo");
            SkillUtils.AddHealth(pawn, SkillsInfo.GetValue<int>(skillName, "healthToAdd"));
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ded678", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int healthToAdd = 100) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int HealthToAdd { get; set; } = healthToAdd;
        }
    }
}