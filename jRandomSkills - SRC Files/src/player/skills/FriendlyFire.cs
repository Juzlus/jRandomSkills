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

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTakeDamage(DynamicHook h)
        {
            CEntityInstance param = h.GetParam<CEntityInstance>(0);
            CTakeDamageInfo param2 = h.GetParam<CTakeDamageInfo>(1);

            if (param == null || param.Entity == null || param2 == null || param2.Attacker == null || param2.Attacker.Value == null)
                return;

            if (param2.AmmoType == 255)
                return;

            CCSPlayerPawn attackerPawn = new(param2.Attacker.Value.Handle);
            CCSPlayerPawn victimPawn = new(param.Handle);

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

            var playerInfo = PlayerManager.GetPlayerByIndex(PlayerManager.GetPlayerEvent(attacker)!.Index);
            if (playerInfo?.Skill != skillName || attacker!.Team != victim!.Team) return;

            float damage = param2.Damage;
            param2.Damage = 0;

            Server.ExecuteCommand("mp_autokick 0");
        
            SkillUtils.AddHealth(
                victimPawn,
                (int)(damage * SkillsInfo.GetValue<float>(skillName, "healthDamageMultiplier")),
                victimPawn.MaxHealth
            );
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff0000", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = true, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float healthDamageMultiplier = .3f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float HealthDamageMultiplier { get; set; } = healthDamageMultiplier;
        }
    }
}