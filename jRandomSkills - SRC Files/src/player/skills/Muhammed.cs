using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class Muhammed : ISkill
    {
        private const Skills skillName = Skills.Muhammed;
        private static readonly QAngle angle = new(10, -5, 9);

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (!IsDeadPlayerValid(player)) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);
            if (playerInfo?.Skill == skillName)
                SpawnExplosion(player!);
        }

        private static void SpawnExplosion(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;

            Vector pos = pawn.AbsOrigin;
            pos.Z += 10;

            SkillUtils.CreateHEGrenadeProjectile(pos, angle, new Vector(0, 0, -10), player.TeamNum);
           
            foreach (var _p in Utilities.GetPlayers().Where(p => p.IsValid && p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist && !p.IsBot))
                SkillUtils.PrintToChat(_p, $"{ChatColors.DarkRed}{player.PlayerName}: {ChatColors.Lime}{_p.GetTranslation("muhammed_explosion")}",
                    border: !Utilities.GetPlayers().Any(p => p.Team == player.Team && !p.IsBot && p != player) ? "tb" : "t");

            var fileNames = new[] { "radiobotfallback01", "radiobotfallback02", "radiobotfallback04" };
            var randomFile = fileNames[new Random().Next(fileNames.Length)];
            player.ExecuteClientCommand($"play vo/agents/balkan/{randomFile}.vsnd");
        }

        public static void OnEntitySpawned(CEntityInstance entity)
        {
            if (entity.DesignerName != "hegrenade_projectile") return;

            var heProjectile = entity.As<CBaseCSGrenadeProjectile>();
            if (heProjectile == null || !heProjectile.IsValid || heProjectile.AbsRotation == null) return;

            Server.NextFrame(() =>
            {
                if (heProjectile == null || !heProjectile.IsValid) return;
                if (!(NearlyEquals(angle.X, heProjectile.AbsRotation.X) && NearlyEquals(angle.Y, heProjectile.AbsRotation.Y) && NearlyEquals(angle.Z, heProjectile.AbsRotation.Z)))
                    return;

                heProjectile.TicksAtZeroVelocity = 100;
                heProjectile.Damage = SkillsInfo.GetValue<int>(skillName, "explosionDamage");
                heProjectile.DmgRadius = SkillsInfo.GetValue<float>(skillName, "explosionRadius");
                heProjectile.DetonateTime = 0;
            });
        }

        private static bool NearlyEquals(float a, float b, float epsilon = 0.001f) => Math.Abs(a - b) < epsilon;

        private static bool IsDeadPlayerValid(CCSPlayerController? player)
        {
            return player != null && player.IsValid && player.PlayerPawn?.Value != null;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#F5CB42", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float explosionRadius = 500.0f, int explosionDamage = 999) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float ExplosionRadius { get; set; } = explosionRadius;
            public int ExplosionDamage { get; set; } = explosionDamage;
        }
    }
}