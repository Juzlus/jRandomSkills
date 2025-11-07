using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class Teleporter : ISkill
    {
        private const Skills skillName = Skills.Teleporter;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var victim = @event.Userid;
            var attacker = @event.Attacker;

            if (!Instance.IsPlayerValid(victim) || !Instance.IsPlayerValid(attacker)) return;
            var attackerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker?.SteamID);

            if (attackerInfo?.Skill == skillName)
                TeleportPlayers(attacker!, victim!);
        }

        private static void TeleportPlayers(CCSPlayerController attacker, CCSPlayerController victim)
        {
            var attackerPawn = attacker.PlayerPawn.Value;
            var victimPawn = victim.PlayerPawn.Value;
            if (attackerPawn == null || !attackerPawn.IsValid || victimPawn == null || !victimPawn.IsValid) return;
            if (attackerPawn.AbsOrigin == null || victimPawn.AbsOrigin == null || attackerPawn.AbsRotation == null || victimPawn.AbsRotation == null) return;

            Vector attackerPosition = new(attackerPawn.AbsOrigin.X, attackerPawn.AbsOrigin.Y, attackerPawn.AbsOrigin.Z);
            QAngle attackerAngles = new(attackerPawn.AbsRotation.X, attackerPawn.AbsRotation.Y, attackerPawn.AbsRotation.Z);
            Vector attackerVelocity = new(attackerPawn.AbsVelocity.X, attackerPawn.AbsVelocity.Y, attackerPawn.AbsVelocity.Z);

            Vector victimPosition = new(victimPawn.AbsOrigin.X, victimPawn.AbsOrigin.Y, victimPawn.AbsOrigin.Z);
            QAngle victimAngles = new(victimPawn.AbsRotation.X, victimPawn.AbsRotation.Y, victimPawn.AbsRotation.Z);
            Vector victimVelocity = new(victimPawn.AbsVelocity.X, victimPawn.AbsVelocity.Y, victimPawn.AbsVelocity.Z);

            victimPawn.Teleport(attackerPosition, attackerAngles, attackerVelocity);
            attackerPawn.Teleport(victimPosition, victimAngles, victimVelocity);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8A2BE2", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}
