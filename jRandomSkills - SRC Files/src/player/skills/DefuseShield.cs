using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    public class DefuseShield : ISkill
    {
        private const Skills skillName = Skills.DefuseShield;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damagedEntity.Entity == null || damageInfo == null) return;
            if (damagedEntity.DesignerName != "player") return;

            CCSPlayerPawn victimPawn = new(damagedEntity.Handle);
            if (!victimPawn.IsDefusing) return;

            var victim = PlayerManager.GetPlayerEvent(victimPawn.Controller?.Value?.As<CCSPlayerController>());
            if (victim == null || PlayerManager.GetPlayerByIndex(victim.Index)?.Skill != skillName) return;

            damageInfo.Damage *= SkillsInfo.GetValue<float>(skillName, "damageTakenMultiplier");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#3a8fe8", CsTeam onlyTeam = CsTeam.CounterTerrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float damageTakenMultiplier = .5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float DamageTakenMultiplier { get; set; } = damageTakenMultiplier;
        }
    }
}
