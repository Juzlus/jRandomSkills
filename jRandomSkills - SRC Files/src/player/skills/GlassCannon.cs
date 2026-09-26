using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    public class GlassCannon : ISkill
    {
        private const Skills skillName = Skills.GlassCannon;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damagedEntity.Entity == null || damageInfo == null) return;
            if (damagedEntity.DesignerName != "player") return;

            CCSPlayerPawn victimPawn = new(damagedEntity.Handle);
            var victim = victimPawn.Controller?.Value?.As<CCSPlayerController>();
            if (victim == null || !victim.IsValid) return;

            // Glass: every source of damage hurts the holder more.
            if (PlayerManager.GetPlayerByIndex(PlayerManager.GetPlayerEvent(victim)?.Index)?.Skill == skillName)
                damageInfo.Damage *= SkillsInfo.GetValue<float>(skillName, "damageTakenMultiplier");

            var attackerEnt = damageInfo.Attacker?.Value;
            if (attackerEnt == null || !attackerEnt.IsValid || attackerEnt.DesignerName != "player") return;
            if (attackerEnt.Handle == victimPawn.Handle) return;
            if (SkillUtils.IsFriendlyFireBlocked(damageInfo, victimPawn)) return;

            CCSPlayerPawn attackerPawn = new(attackerEnt.Handle);
            var attacker = PlayerManager.GetPlayerEvent(attackerPawn.Controller?.Value?.As<CCSPlayerController>());
            if (attacker == null || attacker.Team == victim.Team) return;

            // Cannon: the holder's hits on enemies deal more damage.
            if (PlayerManager.GetPlayerByIndex(attacker.Index)?.Skill == skillName)
                damageInfo.Damage *= SkillsInfo.GetValue<float>(skillName, "damageDealtMultiplier");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#7fd6e8", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float damageDealtMultiplier = 1.75f, float damageTakenMultiplier = 1.5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float DamageDealtMultiplier { get; set; } = damageDealtMultiplier;
            public float DamageTakenMultiplier { get; set; } = damageTakenMultiplier;
        }
    }
}
