using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.player;
using static jRandomSkills.jRandomSkills;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace jRandomSkills
{
    public class Teleporter : ISkill
    {
        private const Skills skillName = Skills.Teleporter;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, Config.GetValue<string>(skillName, "color"));
            
            Instance.RegisterEventHandler<EventPlayerHurt>((@event, info) =>
            {
                var victim = @event.Userid;
                var attacker = @event.Attacker;

                if (!Instance.IsPlayerValid(victim) || !Instance.IsPlayerValid(attacker)) return HookResult.Continue;

                var attackerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker?.SteamID);

                if (attackerInfo?.Skill == skillName)
                {
                    TeleportPlayers(attacker!, victim!);
                }

                return HookResult.Continue;
            });
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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8A2BE2", CsTeam onlyTeam = CsTeam.None, bool needsTeammates = false) : Config.DefaultSkillInfo(skill, active, color, onlyTeam, needsTeammates)
        {
        }
    }
}
