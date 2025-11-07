using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Assassin : ISkill
    {
        private const Skills skillName = Skills.Assassin;
        private static readonly string[] nades = ["inferno", "flashbang", "smokegrenade", "decoy", "hegrenade"];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var damage = @event.DmgHealth;
            var victim = @event.Userid;
            var attacker = @event.Attacker;
            var weapon = @event.Weapon;
            HitGroup_t hitgroup = (HitGroup_t)@event.Hitgroup;

            if (!Instance.IsPlayerValid(attacker) || !Instance.IsPlayerValid(victim) || attacker == victim) return;
            if (nades.Contains(weapon)) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker?.SteamID);
            if (playerInfo?.Skill != skillName) return;

            if (IsBehind(attacker!, victim!))
                SkillUtils.TakeHealth(victim!.PlayerPawn.Value, (int)(damage * (SkillsInfo.GetValue<float>(skillName, "damageMultiplier") - 1f)));
        }

        private static bool IsBehind(CCSPlayerController attacker, CCSPlayerController victim)
        {
            var attackerPawn = attacker.PlayerPawn.Value;
            var victimPawn = victim.PlayerPawn.Value;
            if (attackerPawn == null || !attackerPawn.IsValid || victimPawn == null || !victimPawn.IsValid) return false;
            if (victimPawn.AbsRotation == null || attackerPawn.AbsRotation == null) return false;
            var angles = GetAngleRange(victimPawn.AbsRotation.Y);
            return IsBeetween(angles.Item1, angles.Item2, attackerPawn.AbsRotation.Y);
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

        private static bool IsBeetween(float a, float b, float target)
        {
            if (a <= b)
                return (target >= a && target <= b);
            return (target >= a || target <= b);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#d9d9d9", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float damageMultiplier = 2f, float toleranceDeg = 45f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float DamageMultiplier { get; set; } = damageMultiplier;
            public float ToleranceDeg { get; set; } = toleranceDeg;
        }
    }
}