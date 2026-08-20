using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    public class Cutter : ISkill
    {
        private const Skills skillName = Skills.Cutter;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damagedEntity.Entity == null || damageInfo == null || damageInfo.Attacker == null || damageInfo.Attacker.Value == null)
                return;

            CCSPlayerPawn attackerPawn = new(damageInfo.Attacker.Value.Handle);
            CCSPlayerPawn victimPawn = new(damagedEntity.Handle);

            if (attackerPawn.DesignerName != "player" || victimPawn.DesignerName != "player")
                return;

            if (SkillUtils.IsFriendlyFireBlocked(skillName, damageInfo, victimPawn)) return;

            if (attackerPawn.Controller?.Value == null || victimPawn.Controller?.Value == null)
                return;

            var attacker = PlayerManager.GetPlayerEvent(attackerPawn.Controller.Value.As<CCSPlayerController>());
            if (attacker == null || !attacker.IsValid) return;
            if (attacker.Index == victimPawn.Controller.Value.Index) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(attacker.Index);
            if (playerInfo?.Skill != skillName) return;

            var weapon = damageInfo.Ability?.Value;
            if (weapon == null || !weapon.IsValid) return;
            if (weapon.DesignerName != "weapon_knife" && weapon.DesignerName != "weapon_bayonet") return;

            damageInfo.Damage = 9999f;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#88a31a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, bool friendlyFire = true) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        public bool FriendlyFire { get; set; } = friendlyFire;
        }
    }
}
