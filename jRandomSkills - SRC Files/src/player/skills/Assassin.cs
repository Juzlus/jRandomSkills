using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    public class Assassin : ISkill
    {
        private const Skills skillName = Skills.Assassin;
        private static readonly string[] nadeWeapons =
        [
            "weapon_inferno", "weapon_molotov", "weapon_incgrenade", "weapon_flashbang",
            "weapon_smokegrenade", "weapon_decoy", "weapon_hegrenade"
        ];

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

            CCSPlayerPawn attackerPawn = new(param2.Attacker.Value.Handle);
            CCSPlayerPawn victimPawn = new(param.Handle);

            if (attackerPawn.DesignerName != "player" || victimPawn.DesignerName != "player")
                return;

            if (attackerPawn.Controller?.Value == null || victimPawn.Controller?.Value == null)
                return;

            var attacker = PlayerManager.GetPlayerEvent(attackerPawn.Controller.Value.As<CCSPlayerController>());
            var victim = PlayerManager.GetPlayerEvent(victimPawn.Controller.Value.As<CCSPlayerController>());
            if (attacker == null || !attacker.IsValid || victim == null || !victim.IsValid) return;
            if (attacker.Index == victim.Index) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(attacker.Index);
            if (playerInfo?.Skill != skillName) return;

            var weapon = param2.Ability?.Value;
            if (weapon == null || !weapon.IsValid) return;
            if (nadeWeapons.Contains(weapon.DesignerName)) return;

            if (IsBehind(attacker, victim))
                param2.Damage *= SkillsInfo.GetValue<float>(skillName, "damageMultiplier");
        }

        private static bool IsBehind(CCSPlayerController attacker, CCSPlayerController victim)
        {
            var attackerPawn = attacker.PlayerPawn.Value;
            var victimPawn = victim.PlayerPawn.Value;

            if (attackerPawn == null || !attackerPawn.IsValid || victimPawn == null || !victimPawn.IsValid) return false;
            if (victimPawn.AbsRotation == null || attackerPawn.AbsRotation == null) return false;

            var angles = GetAngleRange(victimPawn.AbsRotation.Y);
            return IsBetween(angles.Item1, angles.Item2, attackerPawn.AbsRotation.Y);
        }

        private static (float, float) GetAngleRange(float angle)
        {
            var toleranceDeg = SkillsInfo.GetValue<float>(skillName, "toleranceDeg");
            float min = angle - toleranceDeg;
            float max = angle + toleranceDeg;

            if (min < -180) min += 360f;
            if (max > 180f) max -= 360f;

            return (min, max);
        }

        private static bool IsBetween(float a, float b, float target)
        {
            if (a <= b)
                return (target >= a && target <= b);
            return (target >= a || target <= b);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#d9d9d9", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float damageMultiplier = 2f, float toleranceDeg = 45f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float DamageMultiplier { get; set; } = damageMultiplier;
            public float ToleranceDeg { get; set; } = toleranceDeg;
        }
    }
}