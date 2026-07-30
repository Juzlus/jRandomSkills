using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    public class Knockback : ISkill
    {
        private const Skills skillName = Skills.Knockback;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void WeaponFire(EventWeaponFire @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo?.Skill != skillName) return;
            if (!SkillUtils.FiresBullets(@event.Weapon)) return;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.Health <= 0) return;
            if ((pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0) return;

            float force = SkillsInfo.GetValue<float>(skillName, "knockbackUnits");
            if (force <= 0) return;

            Vector push = SkillUtils.GetForwardVector(pawn.EyeAngles) * -force;

            pawn.AbsVelocity.X += push.X;
            pawn.AbsVelocity.Y += push.Y;
            pawn.AbsVelocity.Z += push.Z;

            float maxSpeed = SkillsInfo.GetValue<float>(skillName, "maxSpeed");
            if (maxSpeed <= 0) return;

            float speed = pawn.AbsVelocity.Length();
            if (speed <= maxSpeed) return;

            float scale = maxSpeed / speed;
            pawn.AbsVelocity.X *= scale;
            pawn.AbsVelocity.Y *= scale;
            pawn.AbsVelocity.Z *= scale;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff8c42", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float knockbackUnits = 120f, float maxSpeed = 1200f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float KnockbackUnits { get; set; } = knockbackUnits;
            public float MaxSpeed { get; set; } = maxSpeed;
        }
    }
}
