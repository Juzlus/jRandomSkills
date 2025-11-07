using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class ExplosiveShot : ISkill
    {
        private const Skills skillName = Skills.ExplosiveShot;

        private static readonly QAngle angle = new(5, 10, -4);
        private static int lastTick = 0;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;
            float newChance = (float)Instance.Random.NextDouble() * (SkillsInfo.GetValue<float>(skillName, "ChanceTo") - SkillsInfo.GetValue<float>(skillName, "ChanceFrom")) + SkillsInfo.GetValue<float>(skillName, "ChanceFrom");
            playerInfo.SkillChance = newChance;
            SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName, newChance)}", false);
        }

        private static void SpawnExplosion(Vector vector)
        {
            lastTick = Server.TickCount;
            SkillUtils.CreateHEGrenadeProjectile(vector, angle, new Vector(0, 0, 0), 0);
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
                heProjectile.TeamNum = (byte)CsTeam.None;
                heProjectile.Damage = SkillsInfo.GetValue<float>(skillName, "damage");
                heProjectile.DmgRadius = SkillsInfo.GetValue<float>(skillName, "damageRadius");
                heProjectile.DetonateTime = 0;
            });
        }

        private static bool NearlyEquals(float a, float b, float epsilon = 0.001f) => Math.Abs(a -b) < epsilon;

        public static void OnTakeDamage(DynamicHook h)
        {
            if (lastTick == Server.TickCount) return;

            CEntityInstance param = h.GetParam<CEntityInstance>(0);
            CTakeDamageInfo param2 = h.GetParam<CTakeDamageInfo>(1);

            if (param == null || param.Entity == null || param2 == null || param2.Attacker == null || param2.Attacker.Value == null)
                return;

            CCSPlayerPawn attackerPawn = new(param2.Attacker.Value.Handle);
            if (attackerPawn.DesignerName != "player")
                return;

            if (attackerPawn == null || attackerPawn.Controller?.Value == null)
                return;

            CCSPlayerController attacker = attackerPawn.Controller.Value.As<CCSPlayerController>();
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker.SteamID);
            if (playerInfo == null || playerInfo.Skill != skillName) return;

            if (Instance.Random.NextDouble() <= playerInfo.SkillChance)
                SpawnExplosion(param2.DamagePosition);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#9c0000", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float damage = 25f, float damageRadius = 210f, float chanceFrom = .15f, float chanceTo = .3f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float Damage { get; set; } = damage;
            public float DamageRadius { get; set; } = damageRadius;
            public float ChanceFrom { get; set; } = chanceFrom;
            public float ChanceTo { get; set; } = chanceTo;
        }
    }
}