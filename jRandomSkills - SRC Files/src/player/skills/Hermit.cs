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
            new KeyValuePair<string, int>("weapon_glock", 120), new KeyValuePair<string, int>("weapon_usp_silencer", 24), new KeyValuePair<string, int>("weapon_hkp2000", 52), new KeyValuePair<string, int>("weapon_p250", 26),
            new KeyValuePair<string, int>("weapon_cz75", 12), new KeyValuePair<string, int>("weapon_deagle", 35), new KeyValuePair<string, int>("weapon_fiveseven", 100), new KeyValuePair<string, int>("weapon_elite", 120),
            new KeyValuePair<string, int>("weapon_tec9", 90), new KeyValuePair<string, int>("weapon_revolver", 8), new KeyValuePair<string, int>("weapon_mac10", 100), new KeyValuePair<string, int>("weapon_mp9", 120),
            new KeyValuePair<string, int>("weapon_mp7", 120), new KeyValuePair<string, int>("weapon_mp5", 120), new KeyValuePair<string, int>("weapon_mp5sd", 120), new KeyValuePair<string, int>("weapon_ump45", 100),
            new KeyValuePair<string, int>("weapon_p90", 100), new KeyValuePair<string, int>("weapon_bizon", 120), new KeyValuePair<string, int>("weapon_ak47", 90), new KeyValuePair<string, int>("weapon_m4a1", 90),
            new KeyValuePair<string, int>("weapon_m4a1_silencer", 80), new KeyValuePair<string, int>("weapon_galilar", 90), new KeyValuePair<string, int>("weapon_famas", 90), new KeyValuePair<string, int>("weapon_aug", 90),
            new KeyValuePair<string, int>("weapon_sg556", 90), new KeyValuePair<string, int>("weapon_ssg08", 90), new KeyValuePair<string, int>("weapon_awp", 30), new KeyValuePair<string, int>("weapon_scar20", 90),
            new KeyValuePair<string, int>("weapon_g3sg1", 90), new KeyValuePair<string, int>("weapon_nova", 32), new KeyValuePair<string, int>("weapon_xm1014", 32), new KeyValuePair<string, int>("weapon_sawedoff", 32),
            new KeyValuePair<string, int>("weapon_mag7", 32), new KeyValuePair<string, int>("weapon_m249", 200), new KeyValuePair<string, int>("weapon_negev", 300)
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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ded678", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int healthToAdd = 25) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int HealthToAdd { get; set; } = healthToAdd;
        }
    }
}