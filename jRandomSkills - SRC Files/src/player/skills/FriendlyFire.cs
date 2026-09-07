using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    public class FriendlyFire : ISkill
    {
        private const Skills skillName = Skills.FriendlyFire;
        private static bool defaultAutoKick = true;
        private static bool autoKickOverridden;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            if (!autoKickOverridden) return;

            autoKickOverridden = false;
            if (defaultAutoKick) Server.ExecuteCommand("mp_autokick 1");
        }

        private static bool TrySuppressAutoKick()
        {
            if (autoKickOverridden) return true;
            if (!SkillsInfo.GetValue<bool>(skillName, "manageAutoKick")) return false;

            bool live;
            try { live = SkillUtils.CvarValue("mp_autokick", false); }
            catch { return false; }

            if (!live) return false;

            defaultAutoKick = true;
            autoKickOverridden = true;
            Server.ExecuteCommand("mp_autokick 0");
            return true;
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damagedEntity.Entity == null || damageInfo == null || damageInfo.Attacker == null || damageInfo.Attacker.Value == null)
                return;

            if (damageInfo.AmmoType == 255)
                return;

            CCSPlayerPawn attackerPawn = new(damageInfo.Attacker.Value.Handle);
            CCSPlayerPawn victimPawn = new(damagedEntity.Handle);

            if (attackerPawn == null || !attackerPawn.IsValid || victimPawn == null || !victimPawn.IsValid)
                return;

            if (attackerPawn.DesignerName != "player" || victimPawn.DesignerName != "player")
                return;

            if (attackerPawn == null || attackerPawn.Controller?.Value == null || victimPawn == null || victimPawn.Controller?.Value == null)
                return;

            var attackerController = attackerPawn.Controller.Value;
            if (attackerController == null || !attackerController.IsValid) return;

            var victimController = victimPawn.Controller.Value;
            if (victimController == null || !victimController.IsValid) return;

            var attacker = attackerController.As<CCSPlayerController>();
            if (attacker == null || !attacker.IsValid) return;

            var victim = victimController.As<CCSPlayerController>();
            if (victim == null || !victim.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex((PlayerManager.GetPlayerEvent(attacker)?.Index ?? attacker.Index));
            if (playerInfo?.Skill != skillName || attacker!.Team != victim!.Team) return;

            float damage = damageInfo.Damage;
            damageInfo.Damage = 0;

            TrySuppressAutoKick();

            SkillUtils.AddHealth(
                victimPawn,
                (int)(damage * SkillsInfo.GetValue<float>(skillName, "healthDamageMultiplier")),
                victimPawn.MaxHealth
            );
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff0000", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = true, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float healthDamageMultiplier = .3f, bool manageAutoKick = true) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float HealthDamageMultiplier { get; set; } = healthDamageMultiplier;
            public bool ManageAutoKick { get; set; } = manageAutoKick;
        }
    }
}
