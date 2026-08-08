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
        private const int fallbackReserveAmmo = 100;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            if (!Instance.IsPlayerValid(attacker)) return;

            var attackerInfo = PlayerManager.GetPlayerByIndex(attacker!.Index);
            if (attackerInfo?.Skill != skillName) return;

            var pawn = attacker!.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;

            var weapon = pawn.WeaponServices.ActiveWeapon.Value;
            if (weapon == null || !weapon.IsValid || weapon.VData == null) return;

            int reserve = weapon.As<CCSWeaponBase>().GetVData<CCSWeaponBaseVData>()?.PrimaryReserveAmmoMax ?? 0;
            if (reserve <= 0) reserve = fallbackReserveAmmo;

            weapon.Clip1 = weapon.VData.MaxClip1;
            weapon.ReserveAmmo.Fill(reserve);

            Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1");
            Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_pReserveAmmo");
            SkillUtils.AddHealth(pawn, SkillsInfo.GetValue<int>(skillName, "healthToAdd"));

            float effectDuration = SkillsInfo.GetValue<float>(skillName, "effectDuration");
            if (effectDuration > 0)
            {
                pawn.HealthShotBoostExpirationTime = Server.CurrentTime + effectDuration;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flHealthShotBoostExpirationTime");
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ded678", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int healthToAdd = 100, float effectDuration = 1.0f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int HealthToAdd { get; set; } = healthToAdd;
            public float EffectDuration { get; set; } = effectDuration;
        }
    }
}